// Builds the Android APK without opening the editor. build-android.ps1 (next
// to the project) runs this and can install the result over USB:
//
//   Unity -batchmode -quit -projectPath graveway-unity -buildTarget Android
//         -executeMethod Graveway.EditorTools.BuildScript.Android
//         -buildVersion 0.1 -buildCode 3 -out Builds/graveway-0.1.apk
//
// Signing: with a signing file, the APK is signed with the release key, so
// every build installs over the last one on any phone. The file lives
// outside the repo, at %USERPROFILE%\.graveway\signing.properties (or where
// GRAVEWAY_SIGNING points), with these lines:
//   keystore=C:\path\to\graveway-release.keystore
//   storepass=...
//   alias=graveway
//   keypass=...
// Without it the build uses Unity's debug key, and Android won't install it
// over a release-signed copy.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Graveway.EditorTools
{
    public static class BuildScript
    {
        public static void Android()
        {
            string version = Arg("-buildVersion", "0.1");
            int code = int.Parse(Arg("-buildCode", "1"));
            string output = Arg("-out", $"Builds/graveway-{version}.apk");

            PlayerSettings.bundleVersion = version;
            PlayerSettings.Android.bundleVersionCode = code;
            EditorUserBuildSettings.buildAppBundle = false;

            bool signed = Sign();
            try
            {
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ProjectSetup.ScenePath },
                    locationPathName = output,
                    target = BuildTarget.Android,
                    options = BuildOptions.None,
                });
                var s = report.summary;
                // The file's own size: the report's total counts more than the APK.
                float mb = File.Exists(output) ? new FileInfo(output).Length / 1048576f : 0;
                Debug.Log($"Graveway: build {s.result}: {output} ({mb:0.0} MB, {s.totalTime.TotalSeconds:0} s, " +
                          $"{(signed ? "release key" : "debug key")})");
                if (s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
            }
            finally
            {
                // The keystore path and passwords never stay in ProjectSettings.
                PlayerSettings.Android.useCustomKeystore = false;
                PlayerSettings.Android.keystoreName = "";
                PlayerSettings.Android.keyaliasName = "";
                PlayerSettings.Android.keystorePass = "";
                PlayerSettings.Android.keyaliasPass = "";
                // Written out now: a build that fails can leave the path saved.
                AssetDatabase.SaveAssets();
            }
        }

        static bool Sign()
        {
            string file = Environment.GetEnvironmentVariable("GRAVEWAY_SIGNING");
            if (string.IsNullOrEmpty(file))
                file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".graveway", "signing.properties");
            if (!File.Exists(file))
            {
                Debug.LogWarning($"Graveway: no signing file at {file}; signing with the debug key.");
                return false;
            }
            var props = new Dictionary<string, string>();
            foreach (var line in File.ReadAllLines(file))
            {
                int eq = line.IndexOf('=');
                if (eq > 0 && !line.TrimStart().StartsWith("#")) props[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            string Need(string key) => props.TryGetValue(key, out var v) && v.Length > 0 ? v
                : throw new InvalidOperationException($"Graveway: {file} has no '{key}='.");
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = Need("keystore");
            PlayerSettings.Android.keystorePass = Need("storepass");
            PlayerSettings.Android.keyaliasName = Need("alias");
            PlayerSettings.Android.keyaliasPass = props.TryGetValue("keypass", out var kp) && kp.Length > 0 ? kp : Need("storepass");
            return true;
        }

        static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
    }
}
