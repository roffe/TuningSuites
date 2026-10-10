using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CommonSuite;
using MapControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.ViewModels;
using SuiteApp.Views;
using T7App.ViewModels;
using T7App.Views;

namespace T7AppTest
{
    public partial class AppTest
    {
        // the texts below are in the invariant culture; the machine's is put back afterwards
        private static void Invariant(Action check)
        {
            CultureInfo before = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                check();
            }
            finally
            {
                CultureInfo.CurrentCulture = before;
            }
        }

        /// <summary>frmFuelMapAccept showed the exact percentages with F2 (T5Suite too): 120 → 124 is 3.33 %, a leaner cell down to -100 %.</summary>
        [TestMethod]
        public void AutotuneAcceptShowsHundredths()
        {
            s_session!.Dispatch(() =>
            {
                double[] percent = [(124 - 120) * 100.0 / 120, (127 - 128) * 100.0 / 128, -100, 10, 0];
                var window = new AutotuneAcceptWindow("BFuelCal.Map", percent, [0, 1, 2, 3, 4], [500]);
                MapData map = window.FindControl<MapGrid>("Grid")!.Map!;
                Invariant(() => Assert.AreEqual("3.33 -0.78 -100.00 10.00 0.00",
                    string.Join(" ", Enumerable.Range(0, map.Count).Select(i => map.FormatCell(i, MapViewType.Easy)))));
                return true;
            }, default).GetAwaiter().GetResult();
        }

        /// <summary>T7Suite's compare results showed the symbol numbers in X6 like the lengths when addresses are in hex (the default).</summary>
        /// <summary>Compare results' F1 columns as .NET Framework printed them: a half away from zero (.NET 10's F1 gives 2.2).</summary>
        [TestMethod]
        public void CompareResultsRoundHalvesUp()
        {
            Assert.AreEqual("2.3", SuiteApp.Views.F1Converter.Text(2.25, CultureInfo.InvariantCulture));
            Assert.AreEqual("-2.3", SuiteApp.Views.F1Converter.Text(-2.25, CultureInfo.InvariantCulture));
            Assert.AreEqual("85.3", SuiteApp.Views.F1Converter.Text(85.25, CultureInfo.InvariantCulture));
        }

        [TestMethod]
        public void CompareResultsSymbolNumbersInHex()
        {
            string a = Path.Combine(s_dir, "numbers-a.bin"), b = Path.Combine(s_dir, "numbers-b.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), a, true);
            File.Copy(a, b, true);
            var binB = T7.T7Binary.Open(b, 0, false);
            SymbolHelper map = binB.Find("IgnNormCal.Map")!;
            byte[] changed = binB.ReadSymbol(map);
            changed[1] ^= 0x10;
            binB.WriteSymbol(binB.FileAddress(map), changed, false);
            bool hex = SymbolNumberConverter.Hex;
            s_session!.Dispatch(async () =>
            {
                SymbolNumberConverter.Hex = true;
                try
                {
                    var vm = new T7MainWindowViewModel();
                    var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                    window.Show();
                    Assert.IsTrue(await vm.OpenPlainFileAsync(a, true));
                    await vm.CompareToFileAsync(b);
                    var results = (CompareResultsViewModel)vm.SelectedViewer!;
                    CompareRow row = results.Rows.Cast<CompareRow>().Single(r => r.SymbolName == "IgnNormCal.Map");
                    window.CaptureRenderedFrame();
                    var grid = window.GetVisualDescendants().OfType<CompareResultsView>().Single().GetVisualDescendants().OfType<DataGrid>().First();
                    string Cell(string header) => ((TextBlock)grid.Columns.Single(c => (string?)c.Header == header).GetCellContent(row)!).Text!;
                    Assert.AreEqual(row.LengthBytes.ToString("X6"), Cell("Length (bytes)"));
                    Assert.AreEqual(row.SymbolNumber1.ToString("X6"), Cell("Symbolnumber #1"));
                    Assert.AreEqual(row.SymbolNumber2.ToString("X6"), Cell("Symbolnumber #2"));
                    window.Close();
                }
                finally
                {
                    SymbolNumberConverter.Hex = hex;
                }
                return true;
            }, default).GetAwaiter().GetResult();
        }

        /// <summary>
        /// The Free logging grid's Value and Peak had no display format in T7Suite (nor T5Suite's User defined tab): a double's
        /// G15, so Lambda.LambdaInt (× 0.0001 + 1) shows 0.9876 and 853 × 0.1 shows 85.3.
        /// </summary>
        [TestMethod]
        public void FreeLoggingShowsTheFullValue()
        {
            string file = Path.Combine(s_dir, "freelogging.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                CultureInfo before = CultureInfo.CurrentCulture;
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                try
                {
                    var vm = new T7MainWindowViewModel();
                    Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                    vm.Settings.Adapter = "";
                    await vm.ToggleRealtimePanelCommand.ExecuteAsync(null);
                    var rt = vm.Realtime!;
                    rt.Apply(new RealtimeSample(DateTime.Now, [("Lambda.LambdaInt", -124 * 0.0001 + 1), ("In.v_Vehicle", 853 * 0.1)], 25, null));
                    var view = new FreeLoggingView { DataContext = rt };
                    var window = new Window { Content = view, Width = 1200, Height = 2000 };
                    window.Show();
                    var grid = view.FindControl<DataGrid>("Grid")!;
                    string[] Texts(string name)
                    {
                        RealtimeRow row = rt.Rows.Single(r => r.Name == name);
                        grid.ScrollIntoView(row, null);
                        window.CaptureRenderedFrame();
                        return grid.GetVisualDescendants().OfType<DataGridRow>().Single(r => r.DataContext == row)
                            .GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
                    }
                    // value and peak
                    Assert.AreEqual(2, Texts("Lambda.LambdaInt").Count(t => t == "0.9876"));
                    Assert.AreEqual(2, Texts("In.v_Vehicle").Count(t => t == "85.3"));
                    window.Close();
                    await vm.ToggleRealtimePanelCommand.ExecuteAsync(null);
                }
                finally
                {
                    CultureInfo.CurrentCulture = before;
                }
                return true;
            }, default).GetAwaiter().GetResult();
        }

        /// <summary>
        /// ctrlAirmassResult: fuel flow in whole l/h (its int shown with F2), target AFR from λ × 14.7 shown with F2 rounding halves
        /// away from zero as .NET Framework did (λ 0.85 is 12.50).
        /// </summary>
        [TestMethod]
        public void AirmassFuelFlowAndAfrAsT7Suite()
        {
            string file = Path.Combine(s_dir, "airmass-numbers.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new T7MainWindowViewModel();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                vm.ShowAirmassResultCommand.Execute(null);
                var am = (AirmassResultViewModel)vm.SelectedViewer!;
                int wot = am.Result!.Pedal.Length - 1, c1640 = Array.IndexOf(am.Result.Rpm, 1640), c2020 = Array.IndexOf(am.Result.Rpm, 2020),
                    c5060 = Array.IndexOf(am.Result.Rpm, 5060);
                Invariant(() =>
                {
                    am.DisplayMode = 7;
                    Assert.AreEqual("13.00", am.Texts![wot, c1640]);
                    Assert.AreEqual("19.00", am.Texts[wot, c2020]);
                    am.DisplayMode = 4;
                    Assert.AreEqual("0.85", am.Texts![12, c5060]);
                    am.DisplayMode = 5;
                    Assert.AreEqual("12.50", am.Texts![12, c5060]);
                });
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
