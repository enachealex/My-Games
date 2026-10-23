// New builds of the app: GitHub Releases on My-Games, tagged
// graveway-unity-v<version>-<code> with the APK attached (build-android.ps1
// -Release makes them). The web version's own updater only looks at
// graveway-v<version>-<build> tags, so it never takes one of these for its own.
//
// The game updates itself (Plugins/Android/Updater.java): on Wi-Fi it
// downloads a newer build in the background (on mobile data, when tapped),
// checks it against the checksum GitHub gives, and has it ready in an install
// session; then when the game goes into the background, Android installs it
// over this one, without asking from Android 12 on. The next time it's
// opened, it's the new build. Same package and signing key, so progress
// stays, and Android refuses anything signed with another key.
//
// Once, the player has to let Graveway install apps (Install unknown apps):
// the menu says so and opens the page. Before Android 12, or where a phone
// insists, Android asks "Update?" itself: tap the menu's button. If it can't
// install itself at all (or in the editor), the button downloads the APK in
// the browser, as before.

using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
        static readonly Regex UpdateFile = new Regex(@"^update-(\d+)\.apk(\.part)?$");
        const string SessionKey = "graveway.update.session", SessionCode = "graveway.update.code";

        public sealed class Release
        {
            public string version, apk, page;
            public string sha256 = "";           // GitHub's checksum of the APK, lower-case hex ("" if it gave none)
            public long size;                    // bytes (0 if it didn't say)
            public int code;
        }

        // Where an update is: nothing newer; one to get; on its way; here and
        // checked; in an install session, ready to go in; waiting for the
        // player to let Graveway install apps; going in; or gone wrong.
        public enum Stage { None, Available, Downloading, Downloaded, Ready, Allow, Installing, Failed }

        public Release newer;                // a build to install, or null
        public Stage stage;
        public float progress;               // of the download, 0-1
        public string failure = "";
        public bool busy;
        public string status = "";
        public float checkedAt = -1e9f;      // Time.realtimeSinceStartup of the last check
        public event Action Changed;
        public MonoBehaviour runner;         // what runs the download (the HUD)

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

        // Whether this build can install an update itself (on a phone, with the plugin).
        public static bool CanSelfUpdate => Installer.Here;

        // Whether it does on its own: downloading on Wi-Fi, installing when the game's put away.
        public static bool Automatic => CanSelfUpdate && Save.Data.settings.autoUpdate;

        // The newest release with a higher code and an APK; drafts don't count.
        public static Release Pick(string json, int current)
        {
            Release best = null;
            foreach (var r in JArray.Parse(json))
            {
                if ((bool?)r["draft"] == true) continue;
                var m = Tag.Match((string)r["tag_name"] ?? "");
                if (!m.Success || !int.TryParse(m.Groups[2].Value, out int c) || c <= current) continue;
                JToken apk = null;
                if (r["assets"] is JArray assets)
                    foreach (var a in assets)
                        if (((string)a["name"] ?? "").EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) { apk = a; break; }
                if (apk == null) continue;
                if (best != null && c <= best.code) continue;
                string digest = (string)apk["digest"] ?? "";
                best = new Release
                {
                    version = m.Groups[1].Value, code = c, apk = (string)apk["browser_download_url"], page = (string)r["html_url"],
                    sha256 = digest.StartsWith("sha256:", StringComparison.Ordinal) ? digest.Substring(7).ToLowerInvariant() : "",
                    size = (long?)apk["size"] ?? 0,
                };
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
            checkedAt = Time.realtimeSinceStartup;
            Settle();
            Changed?.Invoke();
        }

        // ── Getting it ───────────────────────────────────────────────────
        static string Folder => Application.persistentDataPath;
        static string FileFor(int c) => Path.Combine(Folder, $"update-{c}.apk");

        // After a check: where the update stands, picking up what was done
        // before the game was last closed (a download, a prepared session),
        // and on Wi-Fi, fetching it.
        void Settle()
        {
            Tidy(Folder, Code);
            if (newer == null) { stage = Stage.None; return; }
            if (stage == Stage.Downloading || stage == Stage.Installing) return;
            if (!CanSelfUpdate) { stage = Stage.Available; return; }
            int prepared = PlayerPrefs.GetInt(SessionKey, -1);
            if (PlayerPrefs.GetInt(SessionCode, 0) == newer.code && Installer.Has(prepared)) stage = Installer.CanInstall ? Stage.Ready : Stage.Allow;
            else if (File.Exists(FileFor(newer.code))) stage = Installer.CanInstall ? Stage.Downloaded : Stage.Allow;
            else
            {
                stage = Stage.Available;
                if (ShouldFetch(Automatic, Application.internetReachability, false)) Get();
            }
        }

        // Download now: automatically only on Wi-Fi (or a cable), so a 40 MB
        // build doesn't come out of a data plan unasked; on mobile data, when
        // the player taps.
        public static bool ShouldFetch(bool automatic, NetworkReachability net, bool asked) =>
            net != NetworkReachability.NotReachable && (asked || automatic && net == NetworkReachability.ReachableViaLocalAreaNetwork);

        void Get()
        {
            if (runner != null && newer != null && stage != Stage.Downloading) runner.StartCoroutine(Fetch());
        }

        IEnumerator Fetch()
        {
            var r = newer;
            string file = FileFor(r.code), part = file + ".part";
            stage = Stage.Downloading;
            progress = 0;
            failure = "";
            Changed?.Invoke();
            using (var req = new UnityWebRequest(r.apk, UnityWebRequest.kHttpVerbGET)
                   { downloadHandler = new DownloadHandlerFile(part) { removeFileOnAbort = true } })
            {
                req.SetRequestHeader("User-Agent", "Graveway");
                var op = req.SendWebRequest();
                float shown = 0;
                while (!op.isDone)
                {
                    progress = req.downloadProgress;
                    if (progress - shown >= 0.02f) { shown = progress; Changed?.Invoke(); }
                    yield return null;
                }
                if (req.result != UnityWebRequest.Result.Success)
                {
                    TryDelete(part);
                    stage = Stage.Available;                  // tried again next time
                    failure = "The download stopped";
                    Changed?.Invoke();
                    yield break;
                }
            }
            // Checked off the main thread: hashing 40 MB takes a moment.
            var check = Task.Run(() => Verify(part, r.sha256, r.size));
            while (!check.IsCompleted) yield return null;
            if (check.IsFaulted || !check.Result)
            {
                TryDelete(part);
                stage = Stage.Failed;
                failure = "The download was damaged";
                Changed?.Invoke();
                yield break;
            }
            TryDelete(file);
            File.Move(part, file);
            stage = Installer.CanInstall ? Stage.Downloaded : Stage.Allow;
            Changed?.Invoke();
        }

        // The file is the one GitHub has: its size, and its SHA-256 where
        // GitHub gave one. (Android checks the signature on top.)
        public static bool Verify(string file, string sha256, long size)
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length == 0 || size > 0 && info.Length != size) return false;
            if (string.IsNullOrEmpty(sha256)) return true;
            using var sha = SHA256.Create();
            using var f = File.OpenRead(file);
            var hash = sha.ComputeHash(f);
            return BitConverter.ToString(hash).Replace("-", "").Equals(sha256, StringComparison.OrdinalIgnoreCase);
        }

        // Downloads of this build or older aren't needed any more.
        public static void Tidy(string folder, int current)
        {
            if (!Directory.Exists(folder)) return;
            foreach (var path in Directory.GetFiles(folder, "update-*"))
            {
                var m = UpdateFile.Match(Path.GetFileName(path));
                if (m.Success && int.TryParse(m.Groups[1].Value, out int c) && (c <= current || m.Groups[2].Success)) TryDelete(path);
            }
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) { Debug.LogWarning($"Graveway: couldn't delete {path} ({e.Message})"); }
        }

        // ── Putting it in ────────────────────────────────────────────────
        // Into an install session, ready to go in quickly. Only when the game
        // isn't in a run: writing 40 MB takes a moment.
        public void PrepareIfIdle(bool inRun)
        {
            if (inRun || stage != Stage.Downloaded || newer == null) return;
            Prepare();
        }

        bool Prepare()
        {
            int id = Installer.Prepare(FileFor(newer.code));
            if (id < 0)
            {
                stage = Stage.Failed;
                failure = Installer.Why();
                Changed?.Invoke();
                return false;
            }
            PlayerPrefs.SetInt(SessionKey, id);
            PlayerPrefs.SetInt(SessionCode, newer.code);
            PlayerPrefs.Save();
            stage = Stage.Ready;
            Changed?.Invoke();
            return true;
        }

        void Commit()
        {
            if (Installer.Commit(PlayerPrefs.GetInt(SessionKey, -1)))
            {
                stage = Stage.Installing;
                Changed?.Invoke();
            }
            else
            {
                stage = Stage.Failed;
                failure = Installer.Why();
                Changed?.Invoke();
            }
        }

        // The game going into the background, its run already saved: the
        // moment to put the update in, where Android allows it without asking.
        public void OnBackground()
        {
            Installer.SetForeground(false);
            if (!Automatic || !Installer.Silent || !Installer.CanInstall || newer == null) return;
            if (stage == Stage.Downloaded && !Prepare()) return;
            if (stage == Stage.Ready) Commit();
        }

        // Back in front: what happened meanwhile.
        public void OnForeground()
        {
            Installer.SetForeground(true);
            if (!CanSelfUpdate || newer == null) return;
            string s = Installer.Status();
            if (stage == Stage.Installing)
            {
                if (s == "waiting") { stage = Stage.Ready; Installer.Clear(); }    // Android wants a tap
                else if (s.StartsWith("failed")) { stage = Stage.Failed; failure = s.Substring(Math.Min(8, s.Length)).Trim(); Installer.Clear(); }
            }
            if (stage == Stage.Allow && Installer.CanInstall)
                stage = PlayerPrefs.GetInt(SessionCode, 0) == newer.code && Installer.Has(PlayerPrefs.GetInt(SessionKey, -1)) ? Stage.Ready : Stage.Downloaded;
            Changed?.Invoke();
        }

        // The menu's button: whatever's next, now.
        public void Install()
        {
            if (newer == null) return;
            if (!CanSelfUpdate || stage == Stage.Failed) { Application.OpenURL(newer.apk); return; }
            switch (stage)
            {
                case Stage.Available: if (ShouldFetch(true, Application.internetReachability, true)) Get(); break;
                case Stage.Allow: Installer.OpenSettings(); break;
                case Stage.Downloaded: if (Prepare()) Commit(); break;
                case Stage.Ready: Commit(); break;
            }
        }

        // ── The plugin ───────────────────────────────────────────────────
        static class Installer
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            static AndroidJavaClass cls;
            static bool missing;

            static AndroidJavaClass C
            {
                get
                {
                    if (cls == null && !missing)
                    {
                        try { cls = new AndroidJavaClass("com.thejumpvault.graveway.Updater"); cls.CallStatic<bool>("silent"); }
                        catch (Exception e) { missing = true; cls = null; Debug.LogWarning($"Graveway: no updater plugin ({e.Message})"); }
                    }
                    return cls;
                }
            }

            static AndroidJavaObject Activity => AndroidApplication.currentActivity;

            static T Call<T>(string name, T fallback, params object[] args)
            {
                try { return C != null ? C.CallStatic<T>(name, args) : fallback; }
                catch (Exception e) { Debug.LogWarning($"Graveway: updater {name} failed ({e.Message})"); return fallback; }
            }

            static void Do(string name, params object[] args)
            {
                try { C?.CallStatic(name, args); }
                catch (Exception e) { Debug.LogWarning($"Graveway: updater {name} failed ({e.Message})"); }
            }

            public static bool Here => C != null;
            public static bool Silent => Call("silent", false);
            public static bool CanInstall => Call("canInstall", false, Activity);
            public static bool Has(int id) => Call("has", false, Activity, id);
            public static int Prepare(string file) => Call("prepare", -1, Activity, file);
            public static bool Commit(int id) => Call("commit", false, Activity, id);
            public static string Status() => Call("status", "");
            public static void Clear() => Do("clearStatus");
            public static void SetForeground(bool on) => Do("setForeground", on);
            public static void OpenSettings() => Do("openInstallSettings", Activity);
            public static string Why()
            {
                string s = Status();
                return s.StartsWith("failed:") ? s.Substring(7).Trim() : "Android wouldn't install it";
            }
#else
            public static bool Here => false;
            public static bool Silent => false;
            public static bool CanInstall => false;
            public static bool Has(int id) => false;
            public static int Prepare(string file) => -1;
            public static bool Commit(int id) => false;
            public static string Status() => "";
            public static void Clear() { }
            public static void SetForeground(bool on) { }
            public static void OpenSettings() { }
            public static string Why() => "";
#endif
        }
    }
}
