using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommonSuite;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using MapControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.ViewModels;
using TxSuite.ViewModels;
using TxSuite.Views;

namespace T8AppTest
{
    public partial class AppTest
    {
        // the old pictures' bins were B207L reads (55559437 81f, 2004B207L_no erase): stock bins of the same engine with the same maps
        private const string Bin81f = "55353231_FA5I_C_FME2_65_FIEF_81f.bin";

        private static readonly (string Picture, string Bin, string Symbol, string Tab)[] MapPictures =
        [
            ("map-viewer", Bin81f, "TrqMastCal.Trq_NominalMap", "3D Graph"),
            ("injector-constant", Bin81f, "InjCorrCal.InjectorConst", "2D Graph"),
            ("battery-correction", Bin81f, "InjCorrCal.BattCorrTab", "2D Graph"),
            ("coolant-enrichment", Bin81f, "StartCal.EnrFacManTab", "2D Graph"),
            ("knock-enrichment", Bin81f, "KnkFuelCal.EnrichmentMap", "3D Graph"),
            ("ignition-normal", StockBin, "IgnAbsCal.fi_NormalMAP", "3D Graph"),
            ("ignition-high-octane", StockBin, "IgnAbsCal.fi_highOctanMAP", "3D Graph"),
            ("ignition-low-octane", StockBin, "IgnAbsCal.fi_lowOctanMAP", "3D Graph"),
            ("torque-request", StockBin, "PedalMapCal.Trq_RequestMap", "3D Graph"),
            ("torque-nominal", StockBin, "TrqMastCal.Trq_NominalMap", "3D Graph"),
            ("airmass-torque", StockBin, "TrqMastCal.m_AirTorqMap", "3D Graph"),
            ("rpm-limit", StockBin, "MaxEngSpdCal.n_EngLimTab", "2D Graph"),
        ];

        /// <summary>
        /// The pictures of the book "Analyzing Trionic 8" (TrionicDocumentation t8/images/txsuite) in the light skin: the symbol list and
        /// each map its old T8Suite picture showed, cut out of the main window. T8APP_BOOK_DUMP=&lt;dir&gt; saves them; without it they
        /// only render.
        /// </summary>
        [TestMethod, TestCategory("BookScreenshots")]
        public void BookScreenshots()
        {
            string dir = Directory.CreateDirectory(Path.Combine(s_dir, "book")).FullName;
            foreach (string bin in MapPictures.Select(m => m.Bin).Distinct())
                File.Copy(Path.Combine(Here(), "..", "T8Binaries", bin), Path.Combine(dir, bin), true);
            s_session!.Dispatch(async () =>
            {
                try
                {
                    await BookSymbolList(Path.Combine(dir, StockBin));
                    await BookMaps(dir);
                }
                finally
                {
                    Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
                }
                return true;
            }, default).GetAwaiter().GetResult();
        }

        // each map at the size its window opens at, on the graph tab its old picture showed
        private static async Task BookMaps(string dir)
        {
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 2400, Height = 1500 };
            window.Show();
            foreach (var (picture, bin, symbol, tab) in MapPictures)
            {
                string file = Path.Combine(dir, bin);
                if (vm.Binary?.FileName != file)
                {
                    Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                    Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                }
                string title = vm.OpenSymbolByName(symbol)!.Title;
                Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame();
                var viewer = window.GetVisualDescendants().OfType<MapViewer>().Single();
                viewer.GetVisualDescendants().OfType<TabItem>().Single(t => Equals(t.Header, tab)).IsSelected = true;
                var inner = viewer.GetVisualAncestors().OfType<MdiDocumentWindow>().First();
                // a narrow map's window as wide as its title, the stock bin's long name in full
                var caption = inner.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == title);
                caption.Measure(Size.Infinity);
                if (caption.DesiredSize.Width > caption.Bounds.Width)
                {
                    var mdi = (IMdiDocument)inner.DataContext!;
                    DockRect b = mdi.MdiBounds;
                    mdi.MdiBounds = new DockRect(b.X, b.Y, b.Width + Math.Ceiling(caption.DesiredSize.Width - caption.Bounds.Width), b.Height);
                }
                Dispatcher.UIThread.RunJobs();
                SaveBook(window, inner, picture);
                foreach (DocumentViewModel d in vm.Viewers.ToList()) await vm.CloseViewerAsync(d);
            }
            window.Close();
        }

        // the extracted symbol table: TrqMastCal unfolded under the three categories before it, Trq_NominalMap picked
        private static async Task BookSymbolList(string file)
        {
            string? width;
            using (var key = SettingsKey.Open(SettingsKey.Program))
            {
                width = key.GetValue("SymbolListWidth") as string;
                key.SetValue("SymbolListWidth", "860");
            }
            try
            {
                var vm = new MainViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1300, Height = 640 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.CaptureRenderedFrame();
                vm.SelectedSymbol = vm.Binary!.Find("TrqMastCal.Trq_NominalMap");
                Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame();
                var grid = window.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "SymbolGrid");
                var groups = vm.Symbols!.Groups!.OfType<DataGridCollectionViewGroup>().ToList();
                grid.ScrollIntoView(groups[groups.FindIndex(g => Equals(g.Key, "TrqMastCal")) - 3], null);
                Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame();
                SaveBook(window, grid.GetVisualAncestors().OfType<ToolChromeControl>().First(), "symbol-list");
                window.Close();
            }
            finally
            {
                using var key = SettingsKey.Open(SettingsKey.Program);
                if (width == null) key.DeleteValue("SymbolListWidth");
                else key.SetValue("SymbolListWidth", width);
            }
        }

        private static void SaveBook(Window window, Control part, string name)
        {
            WriteableBitmap frame = window.CaptureRenderedFrame()!;
            string? dir = Environment.GetEnvironmentVariable("T8APP_BOOK_DUMP");
            if (string.IsNullOrEmpty(dir)) return;
            var rect = new PixelRect(PixelPoint.FromPoint(part.TranslatePoint(default, window)!.Value, window.RenderScaling),
                PixelSize.FromSize(part.Bounds.Size, window.RenderScaling)).Intersect(new PixelRect(frame.PixelSize));
            using var crop = new WriteableBitmap(rect.Size, frame.Dpi, frame.Format, frame.AlphaFormat);
            using (var pixels = crop.Lock()) frame.CopyPixels(rect, pixels.Address, pixels.RowBytes * rect.Height, pixels.RowBytes);
            Directory.CreateDirectory(dir);
            crop.Save(Path.Combine(dir, name + ".png"), new PngBitmapEncoderOptions());
        }
    }
}
