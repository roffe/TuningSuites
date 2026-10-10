using System;
using System.Globalization;
using System.IO;
using CommonSuite;
using MapControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.ViewModels;
using T7App.ViewModels;

namespace T7AppTest
{
    public partial class AppTest
    {
        // frmMatrixResult: cells with two decimals (a half rounded up) and empty ones blank, headers and 3D axes with two decimals
        [TestMethod]
        public void MatrixShowsTwoDecimals()
        {
            string file = Path.Combine(s_dir, "matrixtext.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            string log = Path.Combine(s_dir, "matrixtext-20261010-CanTraceExt.t7l");
            var t0 = new DateTime(2026, 10, 10, 12, 0, 0);
            File.WriteAllLines(log,
            [
                RealtimeLog.Line(t0, [("ActualIn.n_Engine", 1000), ("ActualIn.p_AirInlet", 0.5), ("Out.M_Engine", 1143.625)], false),
                RealtimeLog.Line(t0.AddSeconds(1), [("ActualIn.n_Engine", 6000), ("ActualIn.p_AirInlet", 2), ("Out.M_Engine", 3042.77)], false),
            ]);
            s_session!.Dispatch(async () =>
            {
                CultureInfo before = CultureInfo.CurrentCulture;
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                try
                {
                    var vm = new T7MainWindowViewModel();
                    Assert.IsTrue(await vm.OpenFileAsync(file, true));
                    var (lines, matrix) = vm.MatrixSelection(log);
                    matrix.X = "ActualIn.n_Engine";
                    matrix.Y = "ActualIn.p_AirInlet";
                    matrix.Z = "Out.M_Engine";
                    vm.ShowMatrix(lines, matrix);
                    MapData map = ((MapViewerViewModel)vm.SelectedViewer!).Map!;
                    // the largest value needs factor 1, which left 1144 and 3043 in the cells
                    Assert.AreEqual("1143.63", map.FormatCell(0, MapViewType.Easy));
                    Assert.AreEqual("3042.77", map.FormatCell(map.Count - 1, MapViewType.Easy));
                    Assert.AreEqual("", map.FormatCell(1, MapViewType.Easy));
                    // the breakpoints as they are, the grid prints them with two decimals ("0.50", "2.00")
                    Assert.AreEqual(0.5, map.YEasy![0], 1e-9);
                    Assert.AreEqual(2, map.YEasy[^1], 1e-9);
                    Assert.AreEqual(1000 + 5000.0 / 15, map.XEasy![1], 1e-9);
                }
                finally
                {
                    CultureInfo.CurrentCulture = before;
                }
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
