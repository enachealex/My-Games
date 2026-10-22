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
        public List<string> owned = new List<string> { "pistol" };   // guns bought
        public List<string> loadout;         // guns equipped besides the survivor's own; null until first chosen
        public Dictionary<string, GunParts> parts = new Dictionary<string, GunParts>();
        public int refunded;                 // coins given back for old upgrades, shown once
        public Dictionary<string, int> pup = new Dictionary<string, int>();   // survivor upgrades, e.g. maxHp: 1
        public Settings settings = new Settings();
        public RunRecord running;            // the run under way, in case the app closes mid-run; null between runs
    }

    // What a run has earned so far.
    [Preserve, Serializable]
    public sealed class RunRecord
    {
        public int coins;                    // picked up, plus stage bonuses
        public int kills, distance;
        public bool records = true;          // false for a run started further up the road (playtest tool)
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
            Persist();
            return best;
        }

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

        // The guns equipped in the Armory: up to LoadoutSlots - 1, carried
        // besides the survivor's own. Until first chosen, the priciest owned.
        public static List<string> Loadout()
        {
            var d = Data;
            if (d.loadout == null)
            {
                d.loadout = Tunables.Weapons.Where(w => d.owned.Contains(w.id)).OrderByDescending(w => w.price)
                    .Take(Tunables.LoadoutSlots - 1).Select(w => w.id).ToList();
            }
            return d.loadout;
        }

        // What a survivor carries into a run: their own gun first, then the
        // equipped ones in Armory order.
        public static List<string> RunWeapons(CharacterDef c)
        {
            var eq = Loadout();
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
