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
                var about = window.NewAboutWindow("2.5.0");
                about.Show();
                about.CaptureRenderedFrame();
                // About in the same words for every suite; Roffe among the thanks
                var aboutTexts = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(about).OfType<TextBlock>().Select(t => t.Text).ToList();
                CollectionAssert.Contains(aboutTexts, "T5Suite was created with the help of lots of people on ecuproject.com and trionictuning.com");
                CollectionAssert.Contains(aboutTexts, "No e-mail support currently, check out www.trionictuning.com and www.ecuproject.com");
                CollectionAssert.Contains(aboutTexts, "Just4pLeisure ;-)");
                Assert.IsTrue(aboutTexts.Any(t => t?.Contains("Roffe") == true));
                about.Close();
                // the menus the three suites share: the same menus, captions and order (T5Suite's own items in their place)
                var menu = window.GetVisualDescendants().OfType<Menu>().First();
                CollectionAssert.AreEqual(new[] { "_File", "_Actions", "_Tuning", "M_y Maps", "_Realtime", "E_CU", "_Skin", "_Help" },
                    menu.Items.OfType<MenuItem>().Select(m => m.Header as string).ToArray());
                // Actions starts the same in every suite, VIN decoder right under Firmware information
                CollectionAssert.AreEqual(new[] { "_Verify checksum", "_Firmware information", "VIN decoder", "Browse axis information" },
                    ((MenuItem)menu.Items[1]!).Items.OfType<MenuItem>().Take(4).Select(m => m.Header as string).ToArray());
                var fileMenu = menu.Items.OfType<MenuItem>().First().Items.OfType<MenuItem>().Select(m => m.Header as string).ToList();
                CollectionAssert.IsSubsetOf(new[] { "_Open file...", "Save all", "Settings", "E_xit" }, fileMenu);
                CollectionAssert.DoesNotContain(fileMenu, "Options and settings");
                StringAssert.EndsWith(vm.Title, "[ open.BIN ]");
                Assert.AreEqual("Checksum: OK", vm.ChecksumText);
                var bin = (T5Binary)vm.Binary!;
                Assert.IsTrue(bin.IsTrionic55);

                // "Only symbols within binary" by default: SRAM-only symbols hidden until a snapshot or the ECU is there
                // sorted by category ascending and grouped by it, in every suite; inside a category the suite's order (subcategory, then description)
                var byCategory = vm.Symbols!.Cast<CommonSuite.SymbolHelper>().ToList();
                Assert.IsTrue(vm.IsSortedBy("Category", false) && vm.IsGroupedBy("Category"));
                Assert.IsTrue(Enumerable.Range(1, byCategory.Count - 1).All(i => string.Compare(byCategory[i - 1].Category, byCategory[i].Category, System.StringComparison.CurrentCulture) <= 0));
                Assert.IsTrue(Enumerable.Range(1, byCategory.Count - 1).All(i => byCategory[i - 1].Category != byCategory[i].Category
                    || byCategory[i - 1].XdfSubcategory <= byCategory[i].XdfSubcategory));
                var visible = vm.Symbols!.Cast<CommonSuite.SymbolHelper>().ToList();
                Assert.IsTrue(visible.Count > 0 && visible.All(sh => sh.Flash_start_address > 0));
                vm.ImportSramSnapshot(file);
                Assert.AreEqual("All symbols", vm.SymbolFilter!.Name);
                vm.SymbolFilter = vm.SymbolFilters[1];

                // the symbol list's columns as in every suite (no type or bit mask on T5); numbered in symbol table order as ecusymbol does
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame();
                var grid = window.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "SymbolGrid");
                CollectionAssert.AreEqual(new[] { "Symbol name", "Address", "Length", "Description", "User description" },
                    grid.Columns.Where(c => c.IsVisible).OrderBy(c => c.DisplayIndex).Select(c => (string)c.Header!).ToArray());
                CollectionAssert.AreEqual(new[] { "Symbol name", "Number", "Address", "SRAM address", "Length", "Description", "User description", "Category" },
                    grid.Columns.OrderBy(c => c.DisplayIndex).Select(c => (string)c.Header!).ToArray());
                CollectionAssert.AreEqual(Enumerable.Range(0, bin.Symbols.Count).ToArray(),
                    bin.Symbols.Cast<CommonSuite.SymbolHelper>().Select(sh => sh.Symbol_number).ToArray());

                // a user description is a note: saved to <bin>.xml, read back on open, never the symbol's name
                bin.Find("Insp_mat!")!.Userdescription = "my main fuel notes";
                vm.SaveUserDescriptions();
                Assert.AreEqual("Insp_mat!", bin.Find("Insp_mat!")!.SmartVarname);
                Assert.IsNull(bin.FindAny("my main fuel notes"));
                Assert.AreEqual("my main fuel notes", T5Binary.Open(file).Find("Insp_mat!")!.Userdescription);
                // by number: the second of two same-named symbols keeps its own note; a note that starts like T7's placeholder stays a note
                var twins = bin.Symbols.Cast<CommonSuite.SymbolHelper>().Where(sh => sh.Varname == "AMOS_text").ToList();
                Assert.HasCount(2, twins);
                twins[1].Userdescription = "the second one";
                bin.Find("Ign_map_0!")!.Userdescription = "Symbolnumber 5 is not this one";
                vm.SaveUserDescriptions();
                var reopened = T5Binary.Open(file);
                var reopenedTwins = reopened.Symbols.Cast<CommonSuite.SymbolHelper>().Where(sh => sh.Varname == "AMOS_text").ToList();
                Assert.AreEqual("", reopenedTwins[0].Userdescription);
                Assert.AreEqual("the second one", reopenedTwins[1].Userdescription);
                Assert.AreEqual("Symbolnumber 5 is not this one", reopened.Find("Ign_map_0!")!.Userdescription);
                // a read-only folder: a message, no crash (ponytail: checked where folder modes exist)
                if (!OperatingSystem.IsWindows())
                {
                    string? info = null;
                    vm.Info += t => info = t;
                    string folder = Path.GetDirectoryName(file)!;
                    File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                    try
                    {
                        vm.SaveUserDescriptions();
                    }
                    finally
                    {
                        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    }
                    StringAssert.StartsWith(info, "The user descriptions could not be saved");
                }

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
