// Progress, kept on this device: a port of graveway/src/save.js. Stored as
// JSON in PlayerPrefs. Anything missing from a stored save (an older one)
// keeps its default, and storage that fails just means the game forgets.
//
// Playtest notes are kept apart from progress, so "Reset progress" doesn't
// throw away feedback.

using System;
using System.Collections.Generic;
using System.Linq;
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
        const string V1Key = "graveway.save.v1";   // the first Unity slice's
        static string Key = "graveway.save.v2";
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
            data = null;
            notes = null;
        }

        public static SaveData Data
        {
            get
            {
                if (data != null) return data;
                data = new SaveData();
                try
                {
                    var json = PlayerPrefs.GetString(Key, "");
                    if (!string.IsNullOrEmpty(json)) JsonConvert.PopulateObject(json, data, Json);
                    else ImportV1();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Graveway: save unreadable, starting fresh ({e.Message})");
                    data = new SaveData();
                }
                Repair();
                return data;
            }
        }

        // The first Unity slice kept coins, best, runs, kills and guns.
        static void ImportV1()
        {
            var json = PlayerPrefs.GetString(V1Key, "");
            if (string.IsNullOrEmpty(json)) return;
            var old = JsonConvert.DeserializeObject<V1>(json);
            if (old == null) return;
            data.coins = old.coins; data.best = old.best; data.runs = old.runs; data.kills = old.kills;
            if (!string.IsNullOrEmpty(old.character)) data.character = old.character;
            if (old.owned != null && old.owned.Count > 0) data.owned = old.owned;
            data.settings.sound = old.sound;
            data.settings.haptics = old.haptics;
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
            try
            {
                PlayerPrefs.SetString(Key, JsonConvert.SerializeObject(Data, Json));
                PlayerPrefs.Save();
            }
            catch (Exception e) { Debug.LogWarning($"Graveway: couldn't save ({e.Message})"); }
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

        // The guns equipped in the Black Market: up to LoadoutSlots - 1, carried
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
        // equipped ones in Black Market order.
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
                try
                {
                    var json = PlayerPrefs.GetString(NotesKey, "");
                    notes = string.IsNullOrEmpty(json) ? new List<Note>() : JsonConvert.DeserializeObject<List<Note>>(json, Json);
                }
                catch { notes = null; }
                notes ??= new List<Note>();
                return notes;
            }
        }

        public static void PersistNotes()
        {
            try
            {
                PlayerPrefs.SetString(NotesKey, JsonConvert.SerializeObject(Notes, Json));
                PlayerPrefs.Save();
            }
            catch (Exception e) { Debug.LogWarning($"Graveway: couldn't save notes ({e.Message})"); }
        }
    }
}
