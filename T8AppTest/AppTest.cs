using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.ViewModels;
using T8App.ViewModels;
using T8App.Views;
using T8SuitePro;

namespace T8AppTest
{
    /// <summary>
    /// The real app in a headless session (Skia, so the map controls really render), on a copy of a stock bin and with settings
    /// in a temp folder. T8APP_DUMP=&lt;dir&gt; saves the rendered windows as PNG.
    /// </summary>
    [TestClass]
    public partial class AppTest
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<T8App.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

        private const string StockBin = "55353231_FA56_C_FME2_37_FIEF_81c.BIN";

        private static HeadlessUnitTestSession? s_session;
        private static string s_dir = "";

        [ClassInitialize]
        public static void Start(TestContext _)
        {
            s_dir = Directory.CreateTempSubdirectory("t8app").FullName;
            SettingsKey.BaseFolder = Path.Combine(s_dir, "settings");
            s_session = HeadlessUnitTestSession.StartNew(typeof(AppTest));
        }

        [ClassCleanup]
        public static void Stop()
        {
            s_session?.Dispose();
            Directory.Delete(s_dir, true);
        }

        private static string CopyOfStockBin(string name)
        {
            string file = Path.Combine(s_dir, name);
            File.Copy(Path.Combine(Here(), "..", "T8Binaries", StockBin), file, true);
            return file;
        }

        private static void Save(Window w, string name)
        {
            WriteableBitmap frame = w.CaptureRenderedFrame()!;
            string? dir = Environment.GetEnvironmentVariable("T8APP_DUMP");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            frame.Save(Path.Combine(dir, name + ".png"), new PngBitmapEncoderOptions());
        }

        [TestMethod]
        public void OpenBinSymbolsMapAndFirmware()
        {
            string file = CopyOfStockBin("open.BIN");
            s_session!.Dispatch(async () =>
            {
                var vm = new T8MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                StringAssert.StartsWith(vm.Title, "T8SuitePro v");
                StringAssert.EndsWith(vm.Title, "[ open.BIN ]");
                Assert.AreEqual("Normal binary", vm.OpenClosedText);
                Assert.AreEqual("Checksum: OK", vm.ChecksumText);
                Assert.IsInstanceOfType<T8Binary>(vm.Binary);

                // T8Suite's list: only the symbols in the file at first, its own columns, numbered from 1
                Assert.AreEqual("Only symbols within binary", vm.SymbolFilter!.Name);
                var shown = vm.Symbols!.Cast<SymbolHelper>().ToList();
                Assert.IsTrue(shown.All(sh => sh.Length != 0 && sh.Flash_start_address < 0x100000));
                // the categories alphabetically, then the longest first
                Assert.AreEqual(shown.Select(sh => sh.Category).Min(System.StringComparer.Ordinal), shown[0].Category);
                Assert.AreEqual(shown.Where(sh => sh.Category == shown[0].Category).Max(sh => sh.Length), shown[0].Length);
                vm.SymbolFilter = vm.SymbolFilters[0];
                Assert.IsGreaterThan(shown.Count, vm.Symbols!.Cast<SymbolHelper>().Count());
                vm.SymbolFilter = vm.SymbolFilters[1];
                var grid = window.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "SymbolGrid");
                CollectionAssert.AreEqual(new[] { "Symbol name", "Length", "User description", "Number", "Type" },
                    grid.Columns.Where(c => c.IsVisible).OrderBy(c => c.DisplayIndex).Select(c => (string)c.Header!).ToArray());

                // Enter opens the selected map
                vm.SelectedSymbol = vm.Binary!.Find("IgnAbsCal.fi_NormalMAP");
                window.CaptureRenderedFrame();
                grid.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                var viewer = (MapViewerViewModel)vm.SelectedViewer!;
                Assert.AreEqual("Symbol: IgnAbsCal.fi_NormalMAP [open.BIN]", viewer.Title);
                Assert.AreEqual(18, viewer.Map.Cols);
                Assert.IsTrue(viewer.Map.SixteenBit);
                Assert.AreEqual(0.1, viewer.Map.Factor);
                Assert.AreEqual("mg/c", viewer.Map.XName);
                Assert.IsNull(viewer.EcuReadCommand);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(window, "main");

                // an edit saves into the bin with a valid checksum
                viewer.Map.Set([(0, viewer.Map[0] + 1)]);
                await viewer.SaveCommand.ExecuteAsync(null);
                Assert.IsFalse(viewer.Map.Mutated);
                Assert.AreEqual(TrionicCANLib.Checksum.ChecksumResult.Ok, vm.Binary.VerifyChecksum());

                FirmwareInfo info = vm.FirmwareInfo()!;
                Assert.AreEqual("FA56_C_FME2_37_FIEF_81c", info.SoftwareVersion);
                Assert.AreEqual("B207L MY2004/2005 MY03-06 Gasoline / front wheel drive", info.EngineTypeBySoftwareVersion);
                var firmware = new FirmwareInfoWindow { DataContext = info };
                firmware.Show();
                Save(firmware, "firmware");
                firmware.Close();
                var blocks = new FlashBlocksWindow(FirmwareInfo.FlashBlocks(file));
                blocks.Show();
                Save(blocks, "flashblocks");
                blocks.Close();
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
