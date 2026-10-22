// Which release the app offers (Updates.cs): its own tags only, a higher
// code than the build it's in, with an APK, and never a draft.

using NUnit.Framework;

namespace Graveway.Tests
{
    public class UpdatesTests
    {
        const string Releases = @"[
          { ""tag_name"": ""graveway-unity-v0.3-3"", ""draft"": true, ""html_url"": ""p3"",
            ""assets"": [ { ""name"": ""graveway-unity-0.3.apk"", ""browser_download_url"": ""https://x/0.3.apk"" } ] },
          { ""tag_name"": ""graveway-unity-v0.2-2"", ""draft"": false, ""prerelease"": true, ""html_url"": ""p2"",
            ""assets"": [ { ""name"": ""notes.txt"", ""browser_download_url"": ""https://x/notes.txt"" },
                          { ""name"": ""graveway-unity-0.2.apk"", ""browser_download_url"": ""https://x/0.2.apk"" } ] },
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
        public void NothingWhenUpToDate()
        {
            Assert.That(Updates.Pick(Releases, 2), Is.Null);
            Assert.That(Updates.Pick("[]", 1), Is.Null);
        }
    }
}
