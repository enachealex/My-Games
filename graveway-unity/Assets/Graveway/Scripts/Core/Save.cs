// Progress, kept on this device: a port of graveway/src/save.js, as JSON
// files in the app's own storage (Application.persistentDataPath), which
// survive app updates and go along with Android's own backup.
//
// Nothing a player has earned is thrown away by accident:
//   - every change is written straight away, to a new file that's flushed to
//     the disk before it takes the old one's place; the one before is kept
//     as a backup
//   - a file that won't read is copied aside (never overwritten), and the
//     save comes from the backup instead
//   - a run under way is written down every few seconds and whenever the app
//     goes to the background, so a run the app closes in the middle of is
//     banked the next time it starts (Game.cs)
// Anything missing from a stored save (an older one) keeps its default.
//
// Playtest notes are kept apart from progress, so "Reset progress" doesn't
// throw away feedback.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting;

namespace Graveway
{
    [Preserve, Serializable]
    public sealed class Settings
    {
        public bool sound = true, haptics = true;
        public string quality = "high";      // or "low": Battery saver
        public bool god, fps, powers;        // playtest tools: invincible, frame rate, lots of power-ups
        public bool allHats;                 // playtest tool: every hat to try on
        public bool online = true;           // runs sent to the boards online (Online.cs)
        public bool autoUpdate = true;       // newer builds downloaded on Wi-Fi and put in when the game's away (Updates.cs)
        public int startAt;                  // playtest tool: start runs this many metres up
    }

    [Preserve, Serializable]
    public sealed class GunParts
    {
        public List<string> owned = new List<string>();            // "slot:partId"
        public Dictionary<string, string> fitted = new Dictionary<string, string>();
    }

    [Preserve, Serializable]
    public sealed class SaveData
    {
        public int coins;
        public int best;                     // furthest run, metres
        public int runs;
        public int kills;
        public string character = "rook";    // survivor picked last time
        public List<string> survivors;       // survivors unlocked; null in a save from before they were sold
        public List<string> owned = new List<string> { "pistol" };   // guns bought
        public Dictionary<string, List<string>> loadouts = new Dictionary<string, List<string>>();   // by survivor: guns carried besides their own
        public List<string> loadout;         // the one loadout everyone shared until October 2026, where each survivor's starts
        public Dictionary<string, GunParts> parts = new Dictionary<string, GunParts>();
        public int refunded;                 // coins given back for old upgrades, shown once
        public Dictionary<string, int> pup = new Dictionary<string, int>();   // survivor upgrades, e.g. maxHp: 1
        public Settings settings = new Settings();
        public RunRecord running;            // the run under way, in case the app closes mid-run; null between runs
        public List<string> backpack = new List<string>();   // Black Market items, by id, kept between runs
        public bool seenEvac;                // the evac's cut scene seen through once: a tap skips it now
        public int lastCode;                 // the build last opened, to say when it's been updated
        public string playerName = "";       // the name the last score went under, and the next will
        public List<Score> scores = new List<Score>();   // the scoreboard on this device, best first
        public Dictionary<string, int> progress;   // towards hats (Tunables.Hats); null in a save from before them
        public List<string> hatsOwned = new List<string>();   // hats unlocked
        public Dictionary<string, string> hats = new Dictionary<string, string>();   // by survivor: the hat they picked, "" for none; unpicked, their own
    }

    // A run on the scoreboard.
    [Preserve, Serializable]
    public sealed class Score
    {
        public string id;                    // its own: the run's id online too
        public string name = "";             // whoever played it; "" if they didn't say
        public string character;             // survivor id
        public int distance, kills, coins, time;   // metres, zombies, coins, seconds
        public int place;                    // the furthest place reached (0 downtown, 1 the docks...)
        public string at;                    // ISO time, UTC
        public bool sent;                    // on the boards online, under this name (Online.cs)
        public bool refused;                 // the boards turned it away: not tried again
        public int world, region;            // where it placed online when sent (region 0: no country)
        public string country = "";          // where it was sent from, by the server
    }

    // What a run has earned so far.
    [Preserve, Serializable]
    public sealed class RunRecord
    {
        public int coins;                    // picked up, plus stage bonuses
        public int kills, distance;
        public bool records = true;          // false for a run started further up the road (playtest tool)
        public bool board = true;            // false with any playtest tool on: not on the scoreboard
        public string character;             // survivor id
        public int time;                     // seconds
        public int bosses;                   // bosses beaten
        public int place;                    // the furthest place reached: the nth of the run (Tunables.PlaceAt)
    }

    [Preserve, Serializable]
    public sealed class Note
    {
        public string at;                    // ISO time
        public List<string> tags = new List<string>();
        public string text = "", ctx = "";
        public string sent;                  // ISO time it was sent, or null
    }

    public static class Save
    {
        const string V1Key = "graveway.save.v1";   // the first Unity slice's, in PlayerPrefs
        static string Key = "graveway.save.v2";    // the file's name; in PlayerPrefs until October 2026
        static string NotesKey = "graveway.notes.v1";
        static SaveData data;
        static List<Note> notes;

        static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            NullValueHandling = NullValueHandling.Include,
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };

        // Tests keep their own save, away from real progress.
        public static void UseKey(string key)
        {
            Key = key;
            NotesKey = key + ".notes";
            Reload();
        }

        // Forgets what's in memory, so the next use reads the disk again, as
        // the app does when it starts.
        public static void Reload()
        {
            data = null;
            notes = null;
        }

        public static string Folder => Application.persistentDataPath;
        public static string FileOf(string key) => Path.Combine(Folder, key + ".json");
        public static string SaveFile => FileOf(Key);

        public static SaveData Data
        {
            get
            {
                if (data != null) return data;
                data = Read(Key, out bool found, () => new SaveData()) ?? new SaveData();
                if (!found) Import();
                Repair();
                return data;
            }
        }

        // Older homes, the first time: PlayerPrefs (until October 2026), then
        // the first slice's save. They're left where they were.
        static void Import()
        {
            try
            {
                var json = PlayerPrefs.GetString(Key, "");
                if (!string.IsNullOrEmpty(json)) JsonConvert.PopulateObject(json, data, Json);
                else if (!ImportV1()) return;
                Write(Key, JsonConvert.SerializeObject(data, Json));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Graveway: couldn't read the old save ({e.Message}); it stays where it was");
                data = new SaveData();
            }
        }

        // The newest copy of a file that reads: the file itself, a new one
        // that was still being swapped in, or the one before. A copy that
        // won't read is kept aside, not thrown away. found: whether any copy
        // was there at all.
        static T Read<T>(string key, out bool found, Func<T> make) where T : class
        {
            string path = FileOf(key);
            found = false;
            foreach (var f in new[] { path, path + ".tmp", path + ".bak" })
            {
                if (!File.Exists(f)) continue;
                found = true;
                try
                {
                    var into = make();
                    JsonConvert.PopulateObject(File.ReadAllText(f, Encoding.UTF8), into, Json);
                    return into;
                }
                catch (Exception e)
                {
                    string aside = $"{f}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
                    try { File.Copy(f, aside, true); } catch (Exception) { /* the original stays put anyway */ }
                    Debug.LogWarning($"Graveway: {Path.GetFileName(f)} won't read ({e.Message}); kept as {Path.GetFileName(aside)}");
                }
            }
            return null;
        }

        // Written to a new file, flushed to the disk, and only then swapped
        // in, keeping the one before. Whenever the app might die, one of the
        // three is complete.
        static void Write(string key, string json)
        {
            string path = FileOf(key), tmp = path + ".tmp", bak = path + ".bak";
            Directory.CreateDirectory(Folder);
            var bytes = Encoding.UTF8.GetBytes(json);
            using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                f.Write(bytes, 0, bytes.Length);
                f.Flush(true);
            }
            if (File.Exists(path))
            {
                if (File.Exists(bak)) File.Delete(bak);
                File.Move(path, bak);
            }
            File.Move(tmp, path);
        }

        // The first Unity slice kept coins, best, runs, kills and guns.
        static bool ImportV1()
        {
            var json = PlayerPrefs.GetString(V1Key, "");
            if (string.IsNullOrEmpty(json)) return false;
            var old = JsonConvert.DeserializeObject<V1>(json);
            if (old == null) return false;
            data.coins = old.coins; data.best = old.best; data.runs = old.runs; data.kills = old.kills;
            if (!string.IsNullOrEmpty(old.character)) data.character = old.character;
            if (old.owned != null && old.owned.Count > 0) data.owned = old.owned;
            data.settings.sound = old.sound;
            data.settings.haptics = old.haptics;
            return true;
        }

        [Preserve]
        sealed class V1
        {
            public int coins, best, runs, kills;
            public string character;
            public List<string> owned;
            public bool sound = true, haptics = true;
        }

        static void Repair()
        {
            if (data.owned == null || data.owned.Count == 0) data.owned = new List<string> { "pistol" };
            data.parts ??= new Dictionary<string, GunParts>();
            data.pup ??= new Dictionary<string, int>();
            // Survivors are sold from October 2026: a save from before keeps the one
            // it last ran as, and nobody's left on one that isn't theirs.
            data.survivors ??= new List<string> { data.character };
            data.survivors.RemoveAll(id => Tunables.Characters.All(c => c.id != id || c.price == 0));
            data.survivors = data.survivors.Distinct().ToList();
            if (!HasSurvivor(Tunables.Character(data.character)) || Tunables.Character(data.character).id != data.character)
                data.character = Tunables.Characters[0].id;
            data.loadouts ??= new Dictionary<string, List<string>>();
            data.playerName = CleanName(data.playerName);
            data.scores ??= new List<Score>();
            data.scores.RemoveAll(s => s == null);
            // Hats came in October 2026: what was done before counts towards them.
            data.progress ??= new Dictionary<string, int> { { "best", data.best }, { "kills", data.kills }, { "runs", data.runs } };
            // Kills in one run counted from 24 October 2026: this phone's
            // scoreboard has the most so far. (A hat won before stays won.)
            if (!data.progress.ContainsKey("runKills")) data.progress["runKills"] = data.scores.Count > 0 ? data.scores.Max(s => s.kills) : 0;
            data.hatsOwned ??= new List<string>();
            data.hatsOwned = data.hatsOwned.Where(id => Tunables.Hat(id) != null).Distinct().ToList();
            Unlock();
            data.hats ??= new Dictionary<string, string>();
            foreach (var k in data.hats.Keys.ToList())
            {
                string h = data.hats[k] ?? "";
                var def = Tunables.Hat(h);
                if (h.Length > 0 && (def == null || def.owner != null && def.owner != k)) data.hats.Remove(k);
            }
            data.backpack ??= new List<string>();
            data.backpack.RemoveAll(id => Tunables.Item(id) == null);
            if (data.backpack.Count > Tunables.BackpackSize) data.backpack.RemoveRange(Tunables.BackpackSize, data.backpack.Count - Tunables.BackpackSize);
            data.settings ??= new Settings();
            if (data.settings.quality != "low") data.settings.quality = "high";
        }

        public static void Persist()
        {
            try { Write(Key, JsonConvert.SerializeObject(Data, Json)); }
            catch (Exception e) { Debug.LogWarning($"Graveway: couldn't save ({e.Message})"); }
        }

        // A run's takings into the wallet and the records. Returns whether it
        // set a new best.
        public static bool Bank(RunRecord r)
        {
            var d = Data;
            d.coins += r.coins;
            d.kills += r.kills;
            d.runs++;
            bool best = r.records && r.distance > d.best;
            if (best) d.best = r.distance;
            d.running = null;
            LastScore = r.board && r.distance > 0 ? AddScore(r) : null;
            NewHats = r.board ? Count(r) : new List<string>();
            Persist();
            return best;
        }

        // ── Hats ─────────────────────────────────────────────────────────
        // Hats the last run banked unlocked.
        public static List<string> NewHats { get; private set; } = new List<string>();

        public static int Stat(string k) => Data.progress.TryGetValue(k, out var v) ? v : 0;

        // A run's part towards the hats, and the ones it unlocks.
        static List<string> Count(RunRecord r)
        {
            var p = Data.progress;
            p["best"] = Math.Max(Stat("best"), r.distance);
            p["metres"] = Stat("metres") + r.distance;
            p["kills"] = Stat("kills") + r.kills;
            p["runKills"] = Math.Max(Stat("runKills"), r.kills);
            p["bosses"] = Stat("bosses") + r.bosses;
            p["runs"] = Stat("runs") + 1;
            p["place"] = Math.Max(Stat("place"), r.place);
            return Unlock();
        }

        static List<string> Unlock()
        {
            var won = new List<string>();
            foreach (var h in Tunables.Hats)
                if (!data.hatsOwned.Contains(h.id) && Stat(h.stat) >= h.target) { data.hatsOwned.Add(h.id); won.Add(h.id); }
            return won;
        }

        // One of the hats to unlock, and yours. (A survivor's own hat is only
        // ever theirs.)
        public static bool HasHat(string id) => Data.hatsOwned.Contains(id) || Data.settings.allHats && Tunables.Hat(id)?.owner == null;

        // The hat a survivor wears: the one they last picked ("" for none),
        // or until they've picked, their own if they came with one.
        public static string Hat(string survivor)
        {
            string own = Tunables.OwnHat(survivor)?.id ?? "";
            if (!Data.hats.TryGetValue(survivor, out var h)) return own;
            return h == "" || h == own || HasHat(h) ? h : own;
        }

        public static void Wear(string survivor, string hat)
        {
            Data.hats[survivor] = hat ?? "";
            Persist();
        }

        // ── The scoreboard ───────────────────────────────────────────────
        public const int KeepScores = 50, NameLength = 16;

        // Where the last run banked went on the scoreboard; null if it didn't.
        public static Score LastScore { get; private set; }

        // A run onto the scoreboard under the last name used. Best first:
        // furthest, then most kills, then whoever got there first. Only the
        // best KeepScores stay.
        static Score AddScore(RunRecord r)
        {
            var s = new Score
            {
                id = Guid.NewGuid().ToString("N"), name = Data.playerName, character = r.character ?? Tunables.Characters[0].id,
                distance = r.distance, kills = r.kills, coins = r.coins, time = r.time, place = r.place, at = DateTime.UtcNow.ToString("o"),
            };
            var list = Data.scores;
            int i = 0;
            while (i < list.Count && (list[i].distance > s.distance || list[i].distance == s.distance && list[i].kills >= s.kills)) i++;
            if (i >= KeepScores) return null;
            list.Insert(i, s);
            if (list.Count > KeepScores) list.RemoveRange(KeepScores, list.Count - KeepScores);
            return s;
        }

        public static int Rank(Score s) => Data.scores.IndexOf(s) + 1;

        // The name a score is under, and the one the next will go under.
        public static void NameScore(Score s, string name)
        {
            name = CleanName(name);
            Data.playerName = name;
            if (s != null && s.name != name)
            {
                s.name = name;
                if (!s.refused) s.sent = false;           // the new name goes online too
            }
            Persist();
        }

        // One line, single spaces, no more than NameLength letters.
        public static string CleanName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var sb = new StringBuilder();
            foreach (char c in name.Trim())
            {
                if (char.IsWhiteSpace(c)) { if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' '); }
                else if (!char.IsControl(c)) sb.Append(c);
            }
            var s = sb.ToString().Trim();
            return s.Length > NameLength ? s.Substring(0, NameLength).TrimEnd() : s;
        }

        // Names on the scoreboard, most recently used first.
        public static List<string> RecentNames() =>
            Data.scores.Where(s => s.name.Length > 0).OrderByDescending(s => s.at, StringComparer.Ordinal).Select(s => s.name).Distinct().ToList();

        // Writes down the run under way, in case the app closes.
        public static void Running(RunRecord r)
        {
            Data.running = r;
            Persist();
        }

        // A run the app closed in the middle of, banked now; null if there
        // wasn't one.
        public static RunRecord BankUnfinished()
        {
            var r = Data.running;
            if (r == null) return null;
            Bank(r);
            return r;
        }

        // Back to a fresh start, keeping settings (and notes, stored apart).
        public static void Reset()
        {
            var keep = Data.settings;
            data = new SaveData { settings = keep };
            Repair();
            Persist();
        }

        // One gun's parts: what you've bought for it and what's fitted.
        public static GunParts Parts(string gun)
        {
            var d = Data;
            if (!d.parts.TryGetValue(gun, out var g) || g == null) d.parts[gun] = g = new GunParts();
            g.owned ??= new List<string>();
            g.fitted ??= new Dictionary<string, string>();
            return g;
        }

        // slot -> part id for a gun, keeping only parts it owns and that still fit.
        public static Dictionary<string, string> FittedParts(string gun)
        {
            var def = Tunables.Weapon(gun);
            var g = Parts(gun);
            var output = new Dictionary<string, string>();
            foreach (var slot in def.slots)
            {
                if (!g.fitted.TryGetValue(slot, out var want)) continue;
                var part = Tunables.PartsFor(def, slot).Find(p => p.id == want);
                if (part != null && (part.price == 0 || g.owned.Contains($"{slot}:{part.id}"))) output[slot] = part.id;
            }
            return output;
        }

        public static bool HasSurvivor(CharacterDef c) => c.price == 0 || Data.survivors.Contains(c.id);

        // The guns a survivor carries besides their own: up to LoadoutSlots - 1,
        // equipped in the Armory, each survivor their own. Their own gun is
        // never one of them, so no gun is carried twice. Until first chosen:
        // the loadout everyone used to share, or else the priciest guns owned.
        public static List<string> Loadout(string survivor = null)
        {
            var d = Data;
            var c = Tunables.Character(survivor ?? d.character);
            if (!d.loadouts.TryGetValue(c.id, out var list) || list == null)
            {
                var from = d.loadout ?? Tunables.Weapons.Where(w => d.owned.Contains(w.id)).OrderByDescending(w => w.price).Select(w => w.id).ToList();
                d.loadouts[c.id] = list = from.Where(id => id != c.startingWeapon).Take(Tunables.LoadoutSlots - 1).ToList();
            }
            list.RemoveAll(id => id == c.startingWeapon);
            return list;
        }

        // What a survivor carries into a run: their own gun first, then the
        // equipped ones in Armory order. Each is the Armory's gun, with the
        // parts fitted to it there.
        public static List<string> RunWeapons(CharacterDef c)
        {
            var eq = Loadout(c.id);
            var list = new List<string> { c.startingWeapon };
            foreach (var w in Tunables.Weapons)
                if (w.id != c.startingWeapon && eq.Contains(w.id) && Data.owned.Contains(w.id)) list.Add(w.id);
            return list;
        }

        public static int Upgrade(string id) => Data.pup.TryGetValue(id, out var v) ? v : 0;

        // ── Playtest notes ───────────────────────────────────────────────
        public static List<Note> Notes
        {
            get
            {
                if (notes != null) return notes;
                notes = Read(NotesKey, out bool found, () => new List<Note>());
                if (!found)
                {
                    try
                    {
                        var json = PlayerPrefs.GetString(NotesKey, "");
                        if (!string.IsNullOrEmpty(json)) notes = JsonConvert.DeserializeObject<List<Note>>(json, Json);
                    }
                    catch (Exception e) { Debug.LogWarning($"Graveway: couldn't read the old notes ({e.Message})"); }
                }
                notes ??= new List<Note>();
                return notes;
            }
        }

        public static void PersistNotes()
        {
            try { Write(NotesKey, JsonConvert.SerializeObject(Notes, Json)); }
            catch (Exception e) { Debug.LogWarning($"Graveway: couldn't save notes ({e.Message})"); }
        }
    }
}
