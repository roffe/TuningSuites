using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TxSuiteTest
{
    public partial class SwitchTest
    {
        /// <summary>One settings dialog: Common goes to every suite, an ECU tab's options to its suite only.</summary>
        [TestMethod]
        public void SettingsHaveCommonAndAnEcuTabEach()
        {
            s_session!.Dispatch(async () =>
            {
                var t7 = new T7App.ViewModels.T7MainWindowViewModel();
                var settings = new TxSuite.TxSettingsViewModel(t7);
                var window = new TxSuite.TxSettingsWindow { DataContext = settings };
                window.Show();
                var tabs = window.FindControl<TabControl>("Tabs")!;
                CollectionAssert.AreEqual(new[] { "Common", "T5", "T7", "T8" }, tabs.Items.OfType<TabItem>().Select(t => (string)t.Header!).ToArray());
                Avalonia.Size? size = null;
                for (int i = 0; i < 4; i++)
                {
                    tabs.SelectedIndex = i;
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    // the first tab's size for all
                    size ??= window.Bounds.Size;
                    Assert.AreEqual(size, window.Bounds.Size, $"tab {i}");
                    CollectionAssert.AreEqual(Array.Empty<string>(), Untipped(window), $"tab {i}");
                    Save(window, "settings-" + ((string)((TabItem)tabs.Items[i]!).Header!).ToLowerInvariant());
                }

                bool hex = settings.Common.ShowAddressesInHex;
                settings.Common.ShowAddressesInHex = !hex;
                bool legion = settings.T8.UseLegionBootloader;
                settings.T8.UseLegionBootloader = !legion;
                bool t7Pbus = settings.T7.OnlyPBus, t8Pbus = settings.T8.OnlyPBus;
                settings.T7.OnlyPBus = !t7Pbus;
                settings.Apply();
                window.Close();

                // the suite in front has it at once, the others in their files
                Assert.AreEqual(!hex, t7.Settings.ShowAddressesInHex);
                var t5 = new AppSettings(new Trionic5Tools.T5SuiteRegistry());
                var t8 = new AppSettings(new T8SuitePro.T8SuiteRegistry());
                Assert.AreEqual(!hex, t5.ShowAddressesInHex);
                Assert.AreEqual(!hex, t8.ShowAddressesInHex);
                Assert.AreEqual(!legion, t8.UseLegionBootloader);
                Assert.AreEqual(!t7Pbus, t7.Settings.OnlyPBus);
                Assert.AreEqual(t8Pbus, t8.OnlyPBus);
                t7.Shutdown();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        // as the suites' SettingsTooltips: a control with no tip on it or on its row
        private static string[] Untipped(Window settings) => settings.GetVisualDescendants().OfType<Control>()
            .Where(c => c is CheckBox or ComboBox or NumericUpDown or TextBox && c.IsEffectivelyVisible)
            .Where(c => !c.GetSelfAndVisualAncestors().OfType<Control>().TakeWhile(a => a is not HeaderedContentControl).Any(a => ToolTip.GetTip(a) != null))
            .Select(c => c is CheckBox b ? $"{b.Content}" : c.GetType().Name)
            .ToArray();

        private static void Save(Window w, string name)
        {
            WriteableBitmap frame = w.CaptureRenderedFrame()!;
            if (Environment.GetEnvironmentVariable("TXSUITE_DUMP") is { Length: > 0 } dir)
            {
                Directory.CreateDirectory(dir);
                frame.Save(Path.Combine(dir, name + ".png"), new PngBitmapEncoderOptions());
            }
        }
    }
}
