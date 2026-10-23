// The online scoreboard: runs sent to the boards on the home server, and the
// boards read back, in the world and in the player's country. The server is
// Supabase behind a Cloudflare tunnel (graveway-unity/Server), which knows
// the country from where a run is sent; the phone never says.
//
// Its address and key come from Resources/Online.json, which isn't in git:
// Graveway → Set up project and the build copy it in from
// %USERPROFILE%\.graveway\online.json. Without it, everything here stays
// offline and the game is as it was.
//
// A run banked with no playtest tools on waits in the save (Score.sent false)
// until it's sent; so does one renamed after it was. In the editor (and so in
// tests) runs are only ever sent as dry runs: checked and ranked, never kept.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Graveway
{
    public static class Online
    {
        sealed class Config { public string url, key; }

        public sealed class Row
        {
            public int r, d, k, t, p;            // rank, metres, kills, seconds, place
            public string n, s, c, h;            // name, survivor, country, the first 12 of md5(id)
        }

        public sealed class Board
        {
            public string country, region;       // where it was asked from; the board's country (null: the world's)
            public List<Row> scores = new List<Row>();
        }

        static Config config;
        static bool loaded;
        static bool? dry;

        static Config Settings()
        {
            if (loaded) return config;
            loaded = true;
            try
            {
                var t = Resources.Load<TextAsset>("Online");
                var c = t ? JsonConvert.DeserializeObject<Config>(t.text) : null;
                if (c != null && !string.IsNullOrEmpty(c.url) && !string.IsNullOrEmpty(c.key)) config = c;
            }
            catch (Exception e) { Debug.LogWarning($"Graveway: couldn't read the online settings ({e.Message})"); }
            return config;
        }

        // Set up in this build (it has an address and a key).
        public static bool Configured => Settings() != null;

        // Set up, and the player hasn't turned it off.
        public static bool On => Configured && Save.Data.settings.online;

        // Runs only checked and ranked, never kept: always in the editor, so
        // testing never puts a made-up run on the real boards.
        public static bool DryRun { get => dry ?? Application.isEditor; set => dry = value; }

        public static bool Sending { get; private set; }

        // How the server knows a run without saying its id: the first 12 of
        // md5 of the id as Postgres writes a uuid.
        public static string Hash(string id)
        {
            if (!Guid.TryParse(id, out var g)) return "";
            using var md5 = MD5.Create();
            var bytes = md5.ComputeHash(Encoding.ASCII.GetBytes(g.ToString("D")));
            var sb = new StringBuilder();
            for (int i = 0; i < 6; i++) sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }

        // Runs waiting to go: not sent yet (or renamed since), and long
        // enough to be a run (a second at least).
        public static List<Score> Waiting()
        {
            var list = new List<Score>();
            foreach (var s in Save.Data.scores) if (!s.sent && s.time >= 1 && s.distance >= 1) list.Add(s);
            return list;
        }

        // The body of a run as graveway_submit_score takes it.
        public static string Body(Score s, bool dryRun) => JsonConvert.SerializeObject(new Dictionary<string, object>
        {
            { "p_id", Guid.TryParse(s.id, out var g) ? g.ToString("D") : s.id },
            { "p_name", s.name ?? "" },
            { "p_survivor", s.character },
            { "p_distance", s.distance },
            { "p_kills", s.kills },
            { "p_seconds", s.time },
            { "p_place", s.place },
            { "p_build", Updates.Code },
            { "p_dry_run", dryRun },
        });

        public static Board ParseBoard(string json)
        {
            var o = JObject.Parse(json);
            var b = new Board { country = (string)o["country"] ?? "", region = (string)o["region"] };
            if (o["scores"] is JArray a)
                foreach (var e in a)
                    b.scores.Add(new Row
                    {
                        r = (int?)e["r"] ?? 0, n = (string)e["n"] ?? "", s = (string)e["s"] ?? "", d = (int?)e["d"] ?? 0,
                        k = (int?)e["k"] ?? 0, t = (int?)e["t"] ?? 0, p = (int?)e["p"] ?? 0, c = (string)e["c"] ?? "", h = (string)e["h"] ?? "",
                    });
            return b;
        }

        static UnityWebRequest Post(string function, string body)
        {
            var c = Settings();
            var req = new UnityWebRequest(c.url.TrimEnd('/') + "/rest/v1/rpc/" + function, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 15,
            };
            req.SetRequestHeader("apikey", c.key);
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Accept", "application/json");
            // Cloudflare turns away requests with no user agent (its error 1010).
            req.SetRequestHeader("User-Agent", $"Graveway/{Updates.Code} (Unity)");
            return req;
        }

        // What went wrong, for the player: their connection, or the server.
        static string Problem(UnityWebRequest req) =>
            req.result == UnityWebRequest.Result.ConnectionError ? "Couldn't reach the scoreboard. Are you online?"
            : req.responseCode == 429 || (req.downloadHandler?.text ?? "").Contains("54000") ? "Too many runs sent from here. Try again later."
            : $"The scoreboard isn't answering ({req.responseCode}).";

        // A board: "world", or "region" (the player's country). done gets the
        // board, or null and what went wrong.
        public static IEnumerator Fetch(string scope, Action<Board, string> done, int limit = 50)
        {
            if (!Configured) { done(null, "Not set up in this build."); yield break; }
            string body = JsonConvert.SerializeObject(new Dictionary<string, object> { { "p_scope", scope }, { "p_limit", limit } });
            using var req = Post("graveway_scores", body);
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) { done(null, Problem(req)); yield break; }
            Board b = null;
            try { b = ParseBoard(req.downloadHandler.text); }
            catch (Exception e) { done(null, $"The scoreboard sent something odd ({e.Message})."); yield break; }
            done(b, null);
        }

        // Every run waiting, one at a time, until they've all gone or one
        // can't (no connection: the rest wait for next time). A run the
        // server refuses isn't tried again. Each that goes keeps where it
        // placed. changed is called after each.
        public static IEnumerator SendWaiting(Action changed = null)
        {
            if (Sending || !On) yield break;
            Sending = true;
            try
            {
                foreach (var s in Waiting())
                {
                    using var req = Post("graveway_submit_score", Body(s, DryRun));
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        try
                        {
                            var o = JObject.Parse(req.downloadHandler.text);
                            s.world = (int?)o["world"] ?? 0;
                            s.region = (int?)o["region"] ?? 0;
                            s.country = (string)o["country"] ?? "";
                        }
                        catch (Exception e) { Debug.LogWarning($"Graveway: odd answer sending a run ({e.Message})"); }
                        s.sent = true;
                    }
                    else if (req.result == UnityWebRequest.Result.ProtocolError && req.responseCode >= 400 && req.responseCode < 500 && req.responseCode != 429
                             && !(req.downloadHandler?.text ?? "").Contains("54000"))
                    {
                        // Refused (not a possible run, or the id's taken): don't try again.
                        Debug.LogWarning($"Graveway: the scoreboard refused a run: {req.downloadHandler?.text}");
                        s.sent = true;
                        s.refused = true;
                    }
                    else break;                  // no connection, or busy: next time
                    Save.Persist();
                    changed?.Invoke();
                }
            }
            finally { Sending = false; }
        }

        // A country's name from its two letters, where the phone knows it.
        public static string CountryName(string code)
        {
            if (string.IsNullOrEmpty(code)) return "";
            try { return new System.Globalization.RegionInfo(code).EnglishName; }
            catch { return code; }
        }
    }
}
