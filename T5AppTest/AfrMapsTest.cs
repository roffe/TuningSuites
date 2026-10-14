using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MapControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TxSuite.ViewModels;
using TxSuite.Views;
using SuiteApp.ViewModels;
using T5App.ViewModels;
using T5App.Views;

namespace T5AppTest
{
    public partial class AppTest
    {
        private static string Easy(double v, string format = "F2") => v.ToString(format, CultureInfo.CurrentCulture);

        [TestMethod]
        public void AfrErrorMapsShowRichCellsNegativeAndEmptyCellsBlank()
        {
            string file = CopyOfStockBin("afrerror.BIN");
            s_session!.Dispatch(async () =>
            {
                var vm = new MainViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                var afr = vm.T5.AfrMaps!;
                // target 13.0 everywhere; one pass at 11.5 (1.5 rich) and one in the idle map at 12.0
                afr.SetTargetAFRMapInBytes([.. Enumerable.Range(0, 256).SelectMany(_ => new byte[] { 0, 130 })]);
                afr.SetIdleTargetAFRMapInBytes([.. Enumerable.Range(0, 96).SelectMany(_ => new byte[] { 0, 130 })]);
                afr.LogWidebandAFR(11.5, 3000, 0.8, false);
                afr.LogWidebandAFR(12.0, 900, 0.1, true);
                int cell = Array.FindIndex(afr.GetFeedbackAFRMap(), f => f != 0), idleCell = Array.FindIndex(afr.GetIdleFeedbackAFRMap(), f => f != 0);
                Assert.IsTrue(cell > 0 && idleCell > 0);

                // T5Suite: -1.50 (its viewer read the stored low byte 201..255 as negative), not 24.10
                vm.T5.ShowAfrCommand.Execute("FeedbackvsTargetAFR");
                MapData error = ((MapViewerViewModel)vm.SelectedViewer!).Map;
                Assert.AreEqual(-15, error[cell]);
                Assert.AreEqual(Easy(-1.5), error.FormatCell(cell, MapViewType.Easy));
                Assert.AreEqual(-15, error.MinValue());
                // a cell without samples is blank in Easy view only
                Assert.AreEqual("", error.FormatCell(0, MapViewType.Easy));
                Assert.AreEqual("0", error.FormatCell(0, MapViewType.Decimal));

                vm.T5.ShowAfrCommand.Execute("IdleFeedbackvsTargetAFR");
                MapData idleError = ((MapViewerViewModel)vm.SelectedViewer!).Map;
                Assert.AreEqual(Easy(-1.0), idleError.FormatCell(idleCell, MapViewType.Easy));
                Assert.AreEqual("", idleError.FormatCell(0, MapViewType.Easy));

                vm.T5.ShowAfrCommand.Execute("FeedbackAFR");
                MapData feedback = ((MapViewerViewModel)vm.SelectedViewer!).Map;
                Assert.AreEqual(Easy(11.5), feedback.FormatCell(cell, MapViewType.Easy));
                Assert.AreEqual("", feedback.FormatCell(0, MapViewType.Easy));
                vm.T5.ShowAfrCommand.Execute("TargetAFR");
                Assert.IsFalse(((MapViewerViewModel)vm.SelectedViewer!).Map.BlankZero);

                // the realtime panel's Autotune tab: T5Suite filled the feedback grid with F1 text (λ in F2)
                vm.Settings.Adapter = "";
                vm.ConfigureRealtimePanelCommand.Execute(null);
                var rt = (T5RealtimeViewModel)vm.Realtime!;
                FieldInfo tuning = typeof(T5RealtimeViewModel).GetField("m_tuning", BindingFlags.NonPublic | BindingFlags.Instance)!;
                afr.SetCurrentFuelMap(new byte[256]);
                tuning.SetValue(rt, afr);
                rt.LambdaMode = true;
                Assert.AreEqual(Easy(11.5 / 14.7), rt.FeedbackGrid!.FormatCell(cell, MapViewType.Easy));
                rt.LambdaMode = false;
                Assert.AreEqual(Easy(11.5, "F1"), rt.FeedbackGrid!.FormatCell(cell, MapViewType.Easy));
                tuning.SetValue(rt, null);
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
