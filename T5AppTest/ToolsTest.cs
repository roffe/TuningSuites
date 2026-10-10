using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.ViewModels;
using T5App.ViewModels;
using T5App.Views;
using Trionic5Tools;

namespace T5AppTest
{
    public partial class AppTest
    {
        [TestMethod]
        public void DisassemblyVectorsIdc()
        {
            string file = CopyOfStockBin("tools.BIN");
            s_session!.Dispatch(async () =>
            {
                var vm = new T5MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                var bin = (T5Binary)vm.Binary!;

                // the vectors by the CPU's names, the reset vector into the flash
                var vectors = bin.InterruptVectors();
                Assert.AreEqual("Reset initial program counter", vectors[1].Name);
                Assert.IsTrue(vectors[1].Address is >= 0x40000 and < 0x80000);
                Assert.AreEqual("User defined vector 0", vectors[64].Name);

                // the listing has functions at flash addresses; a line maps to its file bytes and back
                await vm.ShowDisassemblyAsync(false, _ => System.Threading.Tasks.Task.FromResult(true));
                var asm = (DisassemblyViewModel)vm.SelectedViewer!;
                string[] lines = asm.Document.Text.Split('\n');
                int line = System.Array.FindIndex(lines, l => l.StartsWith("0x"));
                Assert.IsTrue(line >= 0, "no instructions in the listing");
                var bytes = asm.LineBytes(line + 1)!.Value;
                Assert.IsTrue(DisassemblyViewModel.TryParseAddress(lines[line], out uint address));
                Assert.AreEqual((ulong)(address - 0x40000), bytes.Start);
                Assert.IsNotNull(asm.FindAddress(bytes.Start));

                Assert.IsTrue(File.Exists(bin.ExportIdc()));
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    
        [TestMethod]
        public void DynoCompressorInjectionTiming()
        {
            string file = CopyOfStockBin("dyno.BIN");
            s_session!.Dispatch(async () =>
            {
                var vm = new T5MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));

                // a stock 2.0 T5.5: 16 rpm points, peak torque and power in a sane range, the duty cycle below 100 %
                var dyno = vm.Dyno();
                Assert.AreEqual(16, dyno.Count);
                Assert.IsTrue(dyno.Max(d => d.Torque) is > 150 and < 500, "torque " + dyno.Max(d => d.Torque));
                Assert.IsTrue(dyno.Max(d => d.Power) is > 80 and < 300, "power " + dyno.Max(d => d.Power));
                Assert.IsTrue(dyno.All(d => d.DutyCycle is >= 0 and < 100), "dc " + string.Join(",", dyno.Select(d => d.DutyCycle)));
                var dynoWindow = T5ToolWindows.Dyno(window, vm);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(dynoWindow, "dyno");
                dynoWindow.Close();

                var (compressor, cid) = vm.CompressorDefaults();
                Assert.AreEqual(122, cid);
                var points = vm.CompressorPoints(cid, 20, Enumerable.Repeat(90.0, 16).ToList());
                Assert.AreEqual(16, points.Count);
                Assert.IsTrue(points.All(p => p.lbmin > 0 && p.pr > 0.9));
                var compressorWindow = T5ToolWindows.Compressor(window, vm);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(compressorWindow, "compressor");
                compressorWindow.Close();

                var timing = vm.InjectionTiming("Insp_mat!", 46, 13, vm.InjectorConstant, false)!;
                Assert.AreEqual(256, timing.Count);
                Assert.IsTrue(Enumerable.Range(0, timing.Count).Select(timing.Physical).All(ms => ms is > 0 and < 40));
                var timingWindow = T5ToolWindows.InjectionTiming(window, vm);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(timingWindow, "injectiontiming");
                timingWindow.Close();
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
}
}
