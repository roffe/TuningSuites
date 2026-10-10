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

        [TestMethod]
        public void KnockSnapshotsAndSettings()
        {
            string file = CopyOfStockBin("knock.BIN");
            string folder = Path.Combine(Path.GetDirectoryName(file)!, "Snapshots");
            Directory.CreateDirectory(folder);
            var a = new byte[576];
            var b = new byte[576];
            a[1] = 5;      // cell 0: 5 knocks, then 2
            b[1] = 2;
            a[575] = 1;
            File.WriteAllText(Path.Combine(folder, "Knockmap01012026100000.KNK"), System.Convert.ToHexString(a));
            File.WriteAllText(Path.Combine(folder, "Knockmap01012026110000.KNK"), System.Convert.ToHexString(b));
            File.WriteAllText(Path.Combine(folder, "broken.KNK"), "00");
            s_session!.Dispatch(async () =>
            {
                var vm = new T5MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));

                var snapshots = vm.KnockSnapshots();
                Assert.AreEqual(2, snapshots.Count);
                Assert.AreEqual(6, snapshots.Single(k => k.File.EndsWith("100000.KNK")).Knocks);
                vm.ShowKnockMap(snapshots.Single(k => k.File.EndsWith("100000.KNK")).File, snapshots.Single(k => k.File.EndsWith("110000.KNK")).File);
                var diff = (MapViewerViewModel)vm.SelectedViewer!;
                Assert.AreEqual(288, diff.Map.Count);
                Assert.AreEqual(3, diff.Map[0]);
                Assert.AreEqual(1, diff.Map[287]);
                StringAssert.StartsWith(diff.Title, "Knock counter difference");

                var settings = new SettingsViewModel(vm.Settings, vm.T5Settings);
                var settingsWindow = new SettingsWindow { DataContext = settings };
                settingsWindow.Show();
                Save(settingsWindow, "settings");
                settingsWindow.Close();
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        [TestMethod]
        public void PartNumberList()
        {
            s_session!.Dispatch(async () =>
            {
                // Binaries next to the program (published only): a 16 MHz and a 20 MHz stock bin for the test
                string library = Path.Combine(System.AppContext.BaseDirectory, "Binaries");
                Directory.CreateDirectory(library);
                foreach (string pn in new[] { "4239273", "4781035" })
                    File.Copy(Path.Combine(Here(), "..", "T5Binaries", pn + ".BIN"), Path.Combine(library, pn + ".BIN"), true);
                var rows = T5ToolWindows.PartNumbers();
                Assert.IsTrue(rows.Count > 100, "rows " + rows.Count);
                Assert.AreEqual("16 MHz", rows.First(r => r.Partnumber == "4239273").Library);
                Assert.AreEqual("20 MHz", rows.First(r => r.Partnumber == "4781035").Library);
                var window = new MainWindow { DataContext = new T5MainWindowViewModel(), Width = 1500, Height = 950 };
                window.Show();
                var pick = T5ToolWindows.PartNumberList(window);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var list = window.OwnedWindows.Single();
                Save(list, "partnumbers");
                list.Close();
                Assert.IsNull(await pick);
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
}
}
