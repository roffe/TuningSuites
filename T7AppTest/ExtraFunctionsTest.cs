using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TxSuite.ViewModels;
using TxSuite.Views;
using T7App.ViewModels;
using T7App.Views;

namespace T7AppTest
{
    public partial class AppTest
    {
        /// <summary>
        /// Realtime → Extra functions: T7Suite's SaabOpenTech items in its order and captions, there without a file; with no adapter
        /// set up T7Suite's message and no report.
        /// </summary>
        [TestMethod]
        public void ExtraFunctionsRunSaabOpenTech()
        {
            s_session!.Dispatch(async () =>
            {
                var vm = new MainViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                var infos = new System.Collections.Generic.List<string>();
                vm.Info += infos.Add;
                var extra = window.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Header as string == "Extra functions");
                var items = extra.Items.OfType<MenuItem>().ToList();
                CollectionAssert.AreEqual(new[] { "Set ethanol content", "Set seatbelt ping on", "Set seatbelt ping off", "Set double unlocking with remote on",
                    "Set double unlocking with remote off", "Get trionic info", "SID display test", "Read trionic engine data", "Get SPA info and fault codes",
                    "Get alarm level", "Set alarm level no alarm", "Set alarm level glass break sensor", "Set alarm level glass break and tilt sensor" },
                    items.Select(m => m.Header as string).ToArray());
                CollectionAssert.AreEqual(new[] { "S 1", "S 0", "U 1", "U 0", "I", "T", "M", "P", "Z", "Z 0", "Z 1", "Z 2" }, items.Skip(1).Select(m => m.Tag as string).ToArray());
                Assert.IsTrue(extra.IsEnabled);
                Assert.IsTrue(items.Skip(1).All(m => m.IsEnabled), "no file needed");

                string adapter = vm.Settings.Adapter;
                vm.Settings.Adapter = "";
                try
                {
                    Assert.IsNull(await vm.T7.SaabOpenTechAsync("I"));
                    CollectionAssert.AreEqual(new[] { "Check settings, no CAN adapter has been selected!" }, infos);
                    Assert.IsFalse(vm.IsBusy);
                }
                finally
                {
                    vm.Settings.Adapter = adapter;
                    window.Close();
                }
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
