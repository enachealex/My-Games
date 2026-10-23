// What the app's manifest needs that Unity doesn't put there:
//   - allowBackup: Android's own backup copies the save to the player's
//     Google account and puts it back on a new phone. (It's on by default;
//     this says so, so nothing turns it off unnoticed.)
//   - hasFragileUserData: uninstalling asks whether to keep the app's data,
//     so installing again picks the save back up (Android 10 and up).
//   - REQUEST_INSTALL_PACKAGES and UPDATE_PACKAGES_WITHOUT_USER_ACTION: so it
//     can install a newer build of itself, without asking from Android 12 on
//     (Plugins/Android/Updater.java).

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
            foreach (var name in new[] { "android.permission.REQUEST_INSTALL_PACKAGES", "android.permission.UPDATE_PACKAGES_WITHOUT_USER_ACTION" })
            {
                if (doc.SelectSingleNode($"/manifest/uses-permission[@*[local-name()='name']='{name}']") != null) continue;
                var use = doc.CreateElement("uses-permission");
                var a = doc.CreateAttribute("android", "name", Android);
                a.Value = name;
                use.Attributes.Append(a);
                doc.DocumentElement.InsertBefore(use, app);
            }
            doc.Save(file);
        }
    }
}
#endif
