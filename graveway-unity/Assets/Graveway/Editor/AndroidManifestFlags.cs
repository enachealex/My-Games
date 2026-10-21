// Two flags on the app's manifest, for players' progress:
//   - allowBackup: Android's own backup copies the save to the player's
//     Google account and puts it back on a new phone. (It's on by default;
//     this says so, so nothing turns it off unnoticed.)
//   - hasFragileUserData: uninstalling asks whether to keep the app's data,
//     so installing again picks the save back up (Android 10 and up).

#if UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;

namespace Graveway.EditorTools
{
    public sealed class AndroidManifestFlags : IPostGenerateGradleAndroidProject
    {
        const string Android = "http://schemas.android.com/apk/res/android";

        public int callbackOrder => 0;

        // path: the generated unityLibrary module; its manifest merges into the app's.
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string file = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            var doc = new XmlDocument();
            doc.Load(file);
            var app = (XmlElement)doc.SelectSingleNode("/manifest/application");
            if (app == null)
            {
                app = doc.CreateElement("application");
                doc.DocumentElement.AppendChild(app);
            }
            void Flag(string name)
            {
                var a = doc.CreateAttribute("android", name, Android);
                a.Value = "true";
                app.Attributes.SetNamedItem(a);
            }
            Flag("allowBackup");
            Flag("hasFragileUserData");
            doc.Save(file);
        }
    }
}
#endif
