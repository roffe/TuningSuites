using System;
using System.Globalization;
using System.IO;
using System.Linq;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TxSuite.ViewModels;
using TxSuite.Views;
using SuiteApp.ViewModels;
using T7App.ViewModels;

namespace T7AppTest
{
    public partial class AppTest
    {
        [TestMethod]
        public void MatrixNegativesAndCsvWideband()
        {
            string file = Path.Combine(s_dir, "logvalues.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            string log = Path.Combine(s_dir, "logvalues-20261009-CanTraceExt.t7l");
            var t0 = new DateTime(2026, 10, 9, 12, 0, 0);
            File.WriteAllLines(log,
            [
                RealtimeLog.Line(t0, [("ActualIn.n_Engine", 1000), ("MAF.m_AirInlet", 100), ("IgnProt.fi_Offset", -9.75), ("DisplProt.AD_Scanner", 512)], false),
                RealtimeLog.Line(t0.AddSeconds(1), [("ActualIn.n_Engine", 6000), ("MAF.m_AirInlet", 1000), ("IgnProt.fi_Offset", 20)], false),
            ]);
            s_session!.Dispatch(async () =>
            {
                var vm = new MainViewModel();
                Assert.IsTrue(await vm.OpenFileAsync(file, true));

                // the matrix shows a negative mean as T7Suite did: -9.75 (raw -9750 at factor 0.001), not 55.79
                var (lines, matrix) = vm.MatrixSelection(log);
                matrix.X = "ActualIn.n_Engine";
                matrix.Y = "MAF.m_AirInlet";
                matrix.Z = "IgnProt.fi_Offset";
                vm.ShowMatrix(lines, matrix);
                double[] cells = ((MapViewerViewModel)vm.SelectedViewer!).Map!.PhysicalValues();
                Assert.AreEqual(-9.75, cells.Min(), 1e-9);
                Assert.AreEqual(20, cells.Max(), 1e-9);

                // the CSV export writes the wideband's ADC count as AFR, as CSVGenerator did
                vm.ExportLogCsv(log, vm.LogSelection(log));
                string[] csv = File.ReadAllLines(Path.ChangeExtension(log, ".csv"));
                int column = Array.IndexOf(csv[0].Split(','), "DisplProt.AD_Scanner");
                double afr = double.Parse(csv[1].Split(',')[column], CultureInfo.InvariantCulture);
                Assert.AreEqual(WidebandAfr.AdcToAfr(512, vm.Settings), afr, 1e-9);
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
