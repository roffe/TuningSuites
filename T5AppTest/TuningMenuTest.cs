using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TxSuite.ViewModels;
using TxSuite.Views;
using SuiteApp.ViewModels;
using SuiteApp.Views;
using T5App.ViewModels;
using T5App.Views;
using Trionic5Tools;

namespace T5AppTest
{
    public partial class AppTest
    {
        private static string[] Headers(MenuItem menu) => menu.Items.OfType<MenuItem>().Where(i => i.IsVisible).Select(i => (string)i.Header!).ToArray();

        private static MenuItem TuningEntry(MenuItem menu, params string[] path)
        {
            foreach (string caption in path) menu = menu.Items.OfType<MenuItem>().Single(i => i.IsVisible && (string?)i.Header == MenuTextConverter.Escape(caption));
            return menu;
        }

        /// <summary>The Tuning menu is txlogger's T5 map menus after the wizards: T5.2's maps by its own names.</summary>
        [TestMethod]
        public void TuningMenuIsTxloggersWithTheBinsMaps()
        {
            string t55 = CopyOfStockBin("tuning.bin"), t52 = CopyOfStockBin("tuning52.bin", "4300810.bin");
            s_session!.Dispatch(async () =>
            {
                var vm = new MainViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(t55, true));
                var tuning = window.View.FindControl<MenuItem>("QuickMapsMenu")!;
                CollectionAssert.AreEqual(new[] { "Tuning wizards", "Injection [Fuel]", "Ignition", "Turbo control [M]", "Turbo control [A]",
                    "Knock detection", "Warmup", "Idle" }, Headers(tuning));
                string Opened(MenuItem item)
                {
                    item.Command!.Execute(item.CommandParameter);
                    return ((MapViewerViewModel)vm.SelectedViewer!).MapName;
                }
                Assert.AreEqual("Ign_map_0!", Opened(TuningEntry(tuning, "Ignition", "Ignition normal")));
                Assert.AreEqual("Insp_mat!", Opened(TuningEntry(tuning, "Injection [Fuel]", "VE map - normal")));
                Assert.AreEqual("Tryck_vakt_tab!", Opened(TuningEntry(tuning, "Injection [Fuel]", "Fuel cut in overboost")));
                Assert.AreEqual("Regl_tryck_fgaut!", Opened(TuningEntry(tuning, "Turbo control [A]", "Boost limit in 1st gear")));
                Assert.AreEqual("Knock_ref_matrix!", Opened(TuningEntry(tuning, "Knock detection", "Knock sensitivity map")));
                CollectionAssert.AreEqual(new[] { "Boost request map", "Boost control bias", "P factors", "I factors", "D factors", "Boost limit in 1st gear",
                    "Boost limit in 2nd gear" }, Headers(TuningEntry(tuning, "Turbo control [M]")));

                // T5.2: the automatic 1st gear limit and the knock reference by their T5.2 names, no manual gear limits or retard limit
                Assert.IsTrue(await vm.OpenPlainFileAsync(t52, true));
                Assert.IsFalse(((T5Binary)vm.Binary!).IsTrionic55);
                var limit = TuningEntry(tuning, "Turbo control [A]", "Boost limit in 1st gear");
                Assert.AreEqual("Regl_tryck_fga!", ToolTip.GetTip(limit));
                Assert.AreEqual("Regl_tryck_fga!", Opened(limit));
                Assert.AreEqual("Knock_ref_tab!", Opened(TuningEntry(tuning, "Knock detection", "Knock sensitivity map")));
                CollectionAssert.AreEqual(new[] { "Boost request map", "Boost control bias", "P factors", "I factors", "D factors" },
                    Headers(TuningEntry(tuning, "Turbo control [M]")));
                CollectionAssert.AreEqual(new[] { "Knock sensitivity map", "Boost reduction map" }, Headers(TuningEntry(tuning, "Knock detection")));
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
