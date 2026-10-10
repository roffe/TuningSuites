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
using T5App.ViewModels;
using T5App.Views;
using Trionic5Tools;

namespace T5AppTest
{
    /// <summary>
    /// The real app in a headless session (Skia, so the map controls really render), on a copy of a stock bin and with settings
    /// in a temp folder. T5APP_DUMP=&lt;dir&gt; saves the rendered windows as PNG.
    /// </summary>
    [TestClass]
    public partial class AppTest
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<T5App.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

        // a T5.5 (256 KB) stock bin
        private const string StockBin = "4239273.BIN";

        private static HeadlessUnitTestSession? s_session;
        private static string s_dir = "";

        [ClassInitialize]
        public static void Start(TestContext _)
        {
            s_dir = Directory.CreateTempSubdirectory("t5app").FullName;
            SettingsKey.BaseFolder = Path.Combine(s_dir, "settings");
            // T5App's NLog.config comes along; its log files would go to the real AppData
            NLog.LogManager.SuspendLogging();
            s_session = HeadlessUnitTestSession.StartNew(typeof(AppTest));
        }

        [ClassCleanup]
        public static void Stop()
        {
            s_session?.Dispose();
            Directory.Delete(s_dir, true);
        }

        private static string CopyOfStockBin(string name, string stock = StockBin)
        {
            string file = Path.Combine(s_dir, name);
            File.Copy(Path.Combine(Here(), "..", "T5Binaries", stock), file, true);
            return file;
        }

        private static void Save(Window w, string name)
        {
            WriteableBitmap frame = w.CaptureRenderedFrame()!;
            string? dir = Environment.GetEnvironmentVariable("T5APP_DUMP");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            frame.Save(Path.Combine(dir, name + ".png"), new PngBitmapEncoderOptions());
        }

        [TestMethod]
        public void OpenBinSymbolsAndMap()
        {
            string file = CopyOfStockBin("open.BIN");
            s_session!.Dispatch(async () =>
            {
                var vm = new T5MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                StringAssert.StartsWith(vm.Title, "T5Suite v");
                StringAssert.EndsWith(vm.Title, "[ open.BIN ]");
                Assert.AreEqual("Checksum: OK", vm.ChecksumText);
                var bin = (T5Binary)vm.Binary!;
                Assert.IsTrue(bin.IsTrionic55);

                // T5Suite's grid: the description first, then the symbol
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame();
                var grid = window.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "SymbolGrid");
                CollectionAssert.AreEqual(new[] { "Description", "Symbol" },
                    grid.Columns.Where(c => c.IsVisible).OrderBy(c => c.DisplayIndex).Select(c => (string)c.Header!).ToArray());

                // the main fuel map: 16 x 16 bytes, its factor and axes; its row coloured as a Fuel symbol
                vm.SelectedSymbol = bin.Find("Insp_mat!");
                grid.ScrollIntoView(vm.SelectedSymbol, null);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(window, "symbols");
                window.CaptureRenderedFrame();
                grid.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                var viewer = (MapViewerViewModel)vm.SelectedViewer!;
                Assert.AreEqual("Symbol: Insp_mat! [open.BIN]", viewer.Title);
                Assert.AreEqual(16, viewer.Map.Cols);
                Assert.IsFalse(viewer.Map.SixteenBit);
                Assert.AreEqual(0.00390625, viewer.Map.Factor);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(window, "main");

                // an edit saves into the bin with a valid checksum
                viewer.Map.Set([(0, viewer.Map[0] + 1)]);
                await viewer.SaveCommand.ExecuteAsync(null);
                Assert.IsFalse(viewer.Map.Mutated);
                Assert.AreEqual(TrionicCANLib.Checksum.ChecksumResult.Ok, bin.VerifyChecksum());
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
