using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CommonSuite;

namespace SuiteCoreTest
{
    [TestClass]
    public class UpdateCheckTest
    {
        private static string Release(string tag, bool prerelease = false, bool draft = false, string asset = "T7Suite.msi") =>
            $$"""{"tag_name":"{{tag}}","draft":{{(draft ? "true" : "false")}},"prerelease":{{(prerelease ? "true" : "false")}},"html_url":"https://github.com/roffe/TuningSuites/releases/tag/{{tag}}","assets":[{"browser_download_url":"https://github.com/roffe/TuningSuites/releases/download/{{tag}}/T7Suite.zip"},{"browser_download_url":"https://github.com/roffe/TuningSuites/releases/download/{{tag}}/{{asset}}"}]}""";

        [TestMethod]
        public void NewestT7SuiteReleaseAmongTheSuites()
        {
            // newest first as GitHub lists them: a T8Suite release, the nightly, a draft and a labelled tag don't count
            string json = "[" + string.Join(",",
                Release("T8suite_v3.0.0"),
                Release("T7suite_nightly", prerelease: true),
                Release("T7suite_v2.2.0", draft: true),
                Release("T7suite_v2.1.0-beta"),
                Release("T7suite_v2.0.1"),
                Release("T7suite_v2.0"),
                Release("T7suite_v0.1.59.0", asset: "T7Suite.md5")) + "]";
            Release r = UpdateCheck.Newest(json, "T7suite_v");
            Assert.AreEqual(new Version(2, 0, 1, 0), r.Version);
            Assert.AreEqual("T7suite_v2.0.1", r.Tag);
            Assert.AreEqual("https://github.com/roffe/TuningSuites/releases/download/T7suite_v2.0.1/T7Suite.msi", r.Msi);
            StringAssert.EndsWith(r.Page, "/tag/T7suite_v2.0.1");

            Assert.IsNull(UpdateCheck.Newest("[" + Release("T8suite_v3.0.0") + "]", "T7suite_v"));
            Assert.AreEqual(new Version(3, 0, 0, 0), UpdateCheck.Newest(json, "T8suite_v").Version);
            Assert.IsNull(UpdateCheck.Newest("[]", "T7suite_v"));
            Assert.IsNull(UpdateCheck.Newest("[" + Release("T7suite_v0.1.59.0", asset: "T7Suite.md5") + "]", "T7suite_v").Msi);
            Assert.AreEqual(new Version(2, 0, 0, 0), UpdateCheck.Pad(new Version(2, 0)));
        }
    }
}
