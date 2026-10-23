// The online scoreboard's address and key (Scripts/Core/Online.cs), copied
// into Resources/Online.json from outside the repo, where they live:
// %USERPROFILE%\.graveway\online.json (or where GRAVEWAY_ONLINE points), like
// this:
//   { "url": "https://graveway-api.thejumpvault.com", "key": "<Supabase anon key>" }
// Resources/Online.json is in .gitignore. Without the file the game is built
// offline, and says so.

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Graveway.EditorTools
{
    public static class OnlineSettings
    {
        public const string Target = "Assets/Graveway/Resources/Online.json";

        public static bool Copy()
        {
            string file = Environment.GetEnvironmentVariable("GRAVEWAY_ONLINE");
            if (string.IsNullOrEmpty(file))
                file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".graveway", "online.json");
            if (!File.Exists(file))
            {
                if (File.Exists(Target)) AssetDatabase.DeleteAsset(Target);
                Debug.LogWarning($"Graveway: no online settings at {file}; built without the online scoreboard.");
                return false;
            }
            string text = File.ReadAllText(file);
            if (!File.Exists(Target) || File.ReadAllText(Target) != text)
            {
                File.WriteAllText(Target, text);
                AssetDatabase.ImportAsset(Target);
            }
            return true;
        }
    }
}
