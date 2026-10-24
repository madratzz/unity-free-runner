# unity-free-runner

Captures screenshots of every scene in public Unity projects, without opening them by hand, on free GitHub Actions runners.

Actions → **Scene screenshots** → Run workflow, then list repos (`owner/repo` or `owner/repo@branch`, one per line). Each repo gets an artifact with a `camera` and an `overview` PNG per scene plus `manifest.json` and the Unity log.

Needs a repo secret `UNITY_LICENSE`: the contents of a Unity Personal `.ulf` licence file.
