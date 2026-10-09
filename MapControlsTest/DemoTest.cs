using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MapControlsTest
{
    /// <summary>The demo window on a real bin: symbols, axes, factors and open-loop limits from T7Core end up in the controls.</summary>
    [TestClass]
    public class DemoTest
    {
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

        [TestMethod]
        public void ShowsRealMaps()
        {
            Headless.Run(() =>
            {
                var window = new MapControlsDemo.DemoWindow(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"));
                window.Show();
                Headless.Capture(window, "demo");
                window.Close();
            });
        }
    }
}
