// Opens every scene of a Unity project in the editor (batch mode, with graphics)
// and saves two images per scene: what the scene's own camera sees, and a wide
// angled shot framing everything in the scene. Writes manifest.json next to them.
//
// Run (under xvfb; Personal seats cannot use -batchmode): Unity -projectPath <proj> -executeMethod SceneShots.Capture -outDir <dir>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SceneShots
{
    const int Width = 1280, Height = 720;

    public static void Capture()
    {
        var outDir = Arg("-outDir") ?? "SceneShots";
        Directory.CreateDirectory(outDir);
        var entries = new List<string>();
        var code = 0;
        try
        {
            foreach (var path in ScenePaths())
                entries.Add(CaptureScene(path, outDir));
        }
        catch (Exception e)
        {
            Debug.LogError("SceneShots failed: " + e);
            code = 1;
        }
        File.WriteAllText(Path.Combine(outDir, "manifest.json"),
            "{\"unity\":\"" + Application.unityVersion + "\",\"scenes\":[\n" + string.Join(",\n", entries) + "\n]}\n");
        EditorApplication.Exit(code);
    }

    // Scenes in the build list first (in order), then any other scene under Assets/.
    static IEnumerable<string> ScenePaths()
    {
        var build = EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path)).Select(s => s.path).ToList();
        var rest = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !build.Contains(p) && !IsVendorPath(p))
            .OrderBy(p => p);
        return build.Concat(rest);
    }

    // Skip sample scenes that ship with asset packs and templates.
    static bool IsVendorPath(string p)
    {
        var l = p.ToLowerInvariant();
        return l.Contains("/demo") || l.Contains("/sample") || l.Contains("/example") || l.Contains("textmesh pro") || l.Contains("/plugins/");
    }

    static string CaptureScene(string path, string outDir)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var safe = string.Concat(path.Replace("Assets/", "").Replace(".unity", "").Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_'));
        var shots = new List<string>();
        string error = null;
        try
        {
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var bounds = SceneBounds();

            var cam = Camera.main ?? UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault(c => c.enabled);
            if (cam != null)
            {
                Save(cam, Path.Combine(outDir, safe + " camera.png"));
                shots.Add(safe + " camera.png");
            }

            if (bounds.HasValue)
            {
                var go = new GameObject("__SceneShotsCamera");
                var wide = go.AddComponent<Camera>();
                if (cam != null) { wide.clearFlags = cam.clearFlags; wide.backgroundColor = cam.backgroundColor; }
                Frame(wide, bounds.Value, cam != null && cam.orthographic);
                Save(wide, Path.Combine(outDir, safe + " overview.png"));
                shots.Add(safe + " overview.png");
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
        catch (Exception e)
        {
            error = e.Message;
            Debug.LogError("SceneShots: " + path + ": " + e);
        }
        var sb = new StringBuilder();
        sb.Append("{\"path\":").Append(Json(path)).Append(",\"name\":").Append(Json(name));
        sb.Append(",\"images\":[").Append(string.Join(",", shots.Select(Json))).Append("]");
        if (error != null) sb.Append(",\"error\":").Append(Json(error));
        return sb.Append("}").ToString();
    }

    static Bounds? SceneBounds()
    {
        Bounds? b = null;
        foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (r.bounds.size.magnitude > 5000) continue; // skyboxes, giant ground planes
            if (b == null) b = r.bounds; else { var x = b.Value; x.Encapsulate(r.bounds); b = x; }
        }
        return b;
    }

    // 2D scenes: straight-on orthographic fit. 3D scenes: angled perspective from above.
    static void Frame(Camera c, Bounds b, bool twoD)
    {
        var aspect = (float)Width / Height;
        if (twoD)
        {
            c.orthographic = true;
            c.orthographicSize = Mathf.Max(b.extents.y, b.extents.x / aspect) * 1.1f + 0.5f;
            c.transform.position = new Vector3(b.center.x, b.center.y, b.center.z - 100f);
            c.transform.rotation = Quaternion.identity;
            c.nearClipPlane = 0.01f; c.farClipPlane = 1000f;
            return;
        }
        c.fieldOfView = 50f;
        var radius = Mathf.Max(b.extents.magnitude, 1f);
        var dist = radius / Mathf.Sin(c.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
        var dir = new Vector3(-0.55f, 0.75f, -0.55f).normalized;
        c.transform.position = b.center + dir * dist;
        c.transform.LookAt(b.center);
        c.nearClipPlane = Mathf.Max(0.05f, dist - radius * 2f);
        c.farClipPlane = dist + radius * 2f;
    }

    static void Save(Camera c, string file)
    {
        var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prevTarget = c.targetTexture;
        var prevActive = RenderTexture.active;
        c.targetTexture = rt;
        c.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        c.targetTexture = prevTarget;
        RenderTexture.active = prevActive;
        UnityEngine.Object.DestroyImmediate(tex);
        rt.Release();
        UnityEngine.Object.DestroyImmediate(rt);
    }

    static string Arg(string name)
    {
        var a = Environment.GetCommandLineArgs();
        var i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    static string Json(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
