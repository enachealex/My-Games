// New builds of the app: GitHub Releases on My-Games, tagged
// graveway-unity-v<version>-<code> with the APK attached (build-android.ps1
// -Release makes them). The web version's own updater only looks at
// graveway-v<version>-<build> tags, so it never takes one of these for its own.
//
// A Unity game is compiled into the app and can't swap its own code the way
// the web version swaps its files, so a newer build is offered, not applied:
// tapping it opens the APK's download, and Android asks to install it over
// this one. Same package and signing key, so progress stays.

using System;
using System.Collections;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Graveway
{
    public sealed class Updates
    {
        public const string Repo = "enachealex/My-Games";
        const string Api = "https://api.github.com/repos/" + Repo + "/releases?per_page=30";
        static readonly Regex Tag = new Regex(@"^graveway-unity-v(.+)-(\d+)$");

        public sealed class Release
        {
            public string version, apk, page;
            public int code;
        }

        public Release newer;                // a build to install, or null
        public bool busy;
        public string status = "";
        public event Action Changed;

        // This build's Android version code: 0 in the editor, which only
        // checks when asked.
        public static int Code
        {
            get
            {
                if (code >= 0) return code;
                code = 0;
#if UNITY_ANDROID && !UNITY_EDITOR
                try
                {
                    var activity = AndroidApplication.currentActivity;
                    using var pm = activity.Call<AndroidJavaObject>("getPackageManager");
                    using var info = pm.Call<AndroidJavaObject>("getPackageInfo", activity.Call<string>("getPackageName"), 0);
                    code = info.Get<int>("versionCode");
                }
                catch (Exception e) { Debug.LogWarning($"Graveway: couldn't read the version code ({e.Message})"); }
#endif
                return code;
            }
        }
        static int code = -1;

        // The newest release with a higher code and an APK; drafts don't count.
        public static Release Pick(string json, int current)
        {
            Release best = null;
            foreach (var r in JArray.Parse(json))
            {
                if ((bool?)r["draft"] == true) continue;
                var m = Tag.Match((string)r["tag_name"] ?? "");
                if (!m.Success || !int.TryParse(m.Groups[2].Value, out int c) || c <= current) continue;
                string apk = null;
                if (r["assets"] is JArray assets)
                    foreach (var a in assets)
                        if (((string)a["name"] ?? "").EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) { apk = (string)a["browser_download_url"]; break; }
                if (apk == null) continue;
                if (best == null || c > best.code) best = new Release { version = m.Groups[1].Value, code = c, apk = apk, page = (string)r["html_url"] };
            }
            return best;
        }

        public IEnumerator Check(bool manual)
        {
            if (busy || (Code == 0 && !manual)) yield break;
            busy = true;
            status = "Checking…";
            Changed?.Invoke();
            using (var req = UnityWebRequest.Get(Api))
            {
                req.SetRequestHeader("Accept", "application/vnd.github+json");
                req.SetRequestHeader("User-Agent", "Graveway");
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                    status = req.responseCode == 403 ? "GitHub says try again later" : "Couldn't check for updates";
                else
                {
                    try
                    {
                        newer = Pick(req.downloadHandler.text, Code);
                        status = newer != null ? $"{newer.version} is out" : "Up to date";
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Graveway: unexpected answer from GitHub ({e.Message})");
                        status = "Couldn't check for updates";
                    }
                }
            }
            busy = false;
            Changed?.Invoke();
        }

        // Downloads it in the phone's browser; Android then offers to install it.
        public void Install()
        {
            if (newer != null) Application.OpenURL(newer.apk);
        }
    }
}
