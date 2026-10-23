// Which release the app offers (Updates.cs): its own tags only, a higher
// code than the build it's in, with an APK, and never a draft. And getting
// it: GitHub's checksum and size read, downloads only on Wi-Fi unless asked,
// the file checked, old downloads cleared away.

using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace Graveway.Tests
{
    public class UpdatesTests
    {
        const string Releases = @"[
          { ""tag_name"": ""graveway-unity-v0.3-3"", ""draft"": true, ""html_url"": ""p3"",
            ""assets"": [ { ""name"": ""graveway-unity-0.3.apk"", ""browser_download_url"": ""https://x/0.3.apk"" } ] },
          { ""tag_name"": ""graveway-unity-v0.2-2"", ""draft"": false, ""prerelease"": true, ""html_url"": ""p2"",
            ""assets"": [ { ""name"": ""notes.txt"", ""browser_download_url"": ""https://x/notes.txt"" },
                          { ""name"": ""graveway-unity-0.2.apk"", ""browser_download_url"": ""https://x/0.2.apk"", ""size"": 42926299,
                            ""digest"": ""sha256:C538FBC4EF4CA7975F02B9CF8F8D7C5C7F1D9ECF06AE95961E4A8EEEF7BBB9CB"" } ] },
          { ""tag_name"": ""graveway-v0.9.0-40"", ""draft"": false, ""html_url"": ""web"",
            ""assets"": [ { ""name"": ""graveway-0.9.0.apk"", ""browser_download_url"": ""https://x/web.apk"" } ] },
          { ""tag_name"": ""graveway-unity-v0.5-5"", ""draft"": false, ""html_url"": ""p5"", ""assets"": [] },
          { ""tag_name"": ""graveway-unity-v0.1-1"", ""draft"": false, ""html_url"": ""p1"",
            ""assets"": [ { ""name"": ""graveway-unity-0.1.apk"", ""browser_download_url"": ""https://x/0.1.apk"" } ] }
        ]";

        [Test]
        public void OffersTheNewestRealBuild()
        {
            var r = Updates.Pick(Releases, 1);
            Assert.That(r, Is.Not.Null);
            Assert.That(r.code, Is.EqualTo(2), "not the draft, not the web version's, not one without an APK");
            Assert.That(r.version, Is.EqualTo("0.2"));
            Assert.That(r.apk, Is.EqualTo("https://x/0.2.apk"));
            Assert.That(r.page, Is.EqualTo("p2"));
        }

        [Test]
        public void ReadsTheChecksumAndSize()
        {
            var r = Updates.Pick(Releases, 1);
            Assert.That(r.sha256, Is.EqualTo("c538fbc4ef4ca7975f02b9cf8f8d7c5c7f1d9ecf06ae95961e4a8eeef7bbb9cb"));
            Assert.That(r.size, Is.EqualTo(42926299));
            var bare = Updates.Pick(Releases, 0);
            Assert.That(bare.code, Is.EqualTo(2));
            var old = Updates.Pick(@"[{ ""tag_name"": ""graveway-unity-v0.1-1"", ""assets"": [ { ""name"": ""a.apk"", ""browser_download_url"": ""u"" } ] }]", 0);
            Assert.That((old.sha256, old.size), Is.EqualTo(("", 0L)), "an older release with neither");
        }

        // 40 MB only comes out of a data plan when the player asks.
        [Test]
        public void DownloadsOnWifiOrWhenAsked()
        {
            Assert.That(Updates.ShouldFetch(true, NetworkReachability.ReachableViaLocalAreaNetwork, false));
            Assert.That(Updates.ShouldFetch(true, NetworkReachability.ReachableViaCarrierDataNetwork, false), Is.False);
            Assert.That(Updates.ShouldFetch(true, NetworkReachability.ReachableViaCarrierDataNetwork, true));
            Assert.That(Updates.ShouldFetch(false, NetworkReachability.ReachableViaLocalAreaNetwork, false), Is.False, "automatic updates off");
            Assert.That(Updates.ShouldFetch(true, NetworkReachability.NotReachable, true), Is.False);
        }

        [Test]
        public void ChecksTheDownloadAndClearsOldOnes()
        {
            string dir = Path.Combine(Path.GetTempPath(), "graveway-update-tests");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            try
            {
                string file = Path.Combine(dir, "update-9.apk");
                var bytes = Encoding.ASCII.GetBytes("not really an apk, but bytes all the same");
                File.WriteAllBytes(file, bytes);
                string sha;
                using (var h = SHA256.Create()) sha = System.BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                Assert.That(Updates.Verify(file, sha, bytes.Length));
                Assert.That(Updates.Verify(file, sha.ToUpperInvariant(), 0), "case and a missing size don't matter");
                Assert.That(Updates.Verify(file, "", bytes.Length), "no checksum from GitHub: the size will do");
                Assert.That(Updates.Verify(file, sha, bytes.Length + 1), Is.False, "the wrong size");
                Assert.That(Updates.Verify(file, new string('0', 64), bytes.Length), Is.False, "the wrong checksum");
                Assert.That(Updates.Verify(Path.Combine(dir, "missing.apk"), sha, 0), Is.False);

                foreach (var n in new[] { "update-7.apk", "update-8.apk", "update-10.apk.part", "update-10.apk", "notes.txt" })
                    File.WriteAllText(Path.Combine(dir, n), "x");
                Updates.Tidy(dir, 8);
                Assert.That(Directory.GetFiles(dir), Is.EquivalentTo(new[] { "notes.txt", "update-9.apk", "update-10.apk" }.Select(n => Path.Combine(dir, n))),
                    "this build's and older go, and half-downloads; newer ones stay");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        public void NothingWhenUpToDate()
        {
            Assert.That(Updates.Pick(Releases, 2), Is.Null);
            Assert.That(Updates.Pick("[]", 1), Is.Null);
        }
    }
}
