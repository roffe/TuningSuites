using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommonSuite;
using MapControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.ViewModels;
using T7App.ViewModels;
using T7App.Views;
using TxSuite.ViewModels;
using TxSuite.Views;

namespace T7AppTest
{
    public partial class AppTest
    {
        /// <summary>What shows under the table: no graph, the 3D Graph tab or the 2D Graph tab.</summary>
        private enum BookGraph { None, Surface, Line }

        /// <summary>
        /// One of the book's map pictures: the map's window alone, or with Edit the window after the edit (Before: the stock map to
        /// its left).
        /// </summary>
        private sealed record BookMap(string Bin, string Symbol, BookGraph Graph, string Name, Action<MapViewer>? Edit = null, bool Before = false);

        // the 400 Nm BioPower file's limiters and torque axis, from the old pictures (lowest rpm / gear first)
        private static readonly Dictionary<string, int[]> s_tuned = new()
        {
            ["TorqueCal.M_EngMaxE85Tab"] = [180, 295, 380, 380, 380, 380, 380, 370, 350, 345, 340, 330, 320, 302, 275, 263],
            ["TorqueCal.M_ManGearLim"] = [230, 350, 410, 410, 410, 410],
            ["TorqueCal.M_OverBoostTab"] = [200, 247, 315, 370, 400, 400, 400, 400, 400, 400, 400, 400, 370, 340, 319, 294],
            ["TorqueCal.M_EngXSP"] = [-39, -19, 0, 25, 50, 80, 110, 140, 170, 200, 230, 260, 290, 320, 350, 400],
            ["TorqueCal.M_EngMaxAutTab"] = [200, 247, 315, 360, 360, 360, 360, 360, 360, 360, 360, 360, 357, 343, 319, 294],
        };

        // ... and rescaled with the axis
        private static readonly Dictionary<string, int[]> s_rescaled = new()
        {
            ["TorqueCal.M_ReverseTab"] = [200, 200, 200],
            ["TorqueCal.M_ManGearLim"] = [230, 280, 330, 330, 330, 330],
            ["TorqueCal.M_EngMaxAutTab"] = [295, 298, 310, 322, 326, 328, 330, 330, 330, 330, 328, 326, 322, 320, 310, 291],
            ["TorqueCal.M_EngMaxE85Tab"] = [180, 291, 326, 330, 330, 330, 330, 324, 320, 317, 316, 313, 310, 305, 276, 263],
            ["TorqueCal.M_EngMaxTab"] = [200, 247, 305, 322, 330, 330, 330, 330, 330, 330, 326, 322, 321, 317, 309, 291],
            ["TorqueCal.M_5GearLimTab"] = [330, 330, 330, 330, 330, 330, 330, 330, 330, 330, 330, 330, 330, 330, 330, 330],
            ["TorqueCal.M_OverBoostTab"] = [200, 247, 300, 322, 330, 330, 330, 330, 330, 330, 326, 322, 321, 317, 309, 291],
        };

        // "Analyzing Trionic 7"'s map pictures. Chapters 5 and 6 were taken on a MY00 9-3 Viggen (B235R), 5388293 has its maps; chapter 7's
        // example is a 9-3 B205R (5380480), its BioPower limiters a tuned BioPower file (55562425 with the old pictures' values)
        private static readonly BookMap[] s_bookMaps =
        [
            new("5388293.bin", "TorqueCal.M_NominalMap", BookGraph.Surface, "torquecal-m-nominalmap"),
            new("5388293.bin", "InjCorrCal.InjectorConst", BookGraph.None, "injcorrcal-injectorconst"),
            new("5388293.bin", "InjCorrCal.BattCorrTab", BookGraph.Line, "injcorrcal-battcorrtab"),
            new("5388293.bin", "StartCal.EnrFacTab", BookGraph.Line, "startcal-enrfactab"),
            new("5388293.bin", "KnkFuelCal.EnrichmentMap", BookGraph.Surface, "knkfuelcal-enrichmentmap"),
            new("5388293.bin", "IgnIdleCal.Tab", BookGraph.None, "ignidlecal-tab"),
            new("5388293.bin", "IgnNormCal.Map", BookGraph.Surface, "ignnormcal-map"),
            new("5388293.bin", "PedalMapCal.m_RequestMap", BookGraph.Surface, "pedalmapcal-m-requestmap"),

            new("5380480.bin", "AirCtrlCal.m_MaxAirTab", BookGraph.None, "airctrlcal-m-maxairtab"),
            new("5380480.bin", "BoostCal.I_LimTab", BookGraph.None, "boostcal-i-limtab"),
            new("5380480.bin", "BoostCal.P_LimTab", BookGraph.None, "boostcal-p-limtab"),
            new("5380480.bin", "BoostCal.RegMap", BookGraph.Surface, "boostcal-regmap"),
            new("5380480.bin", "BstKnkCal.MaxAirmass", BookGraph.Surface, "bstknkcal-maxairmass"),
            new("5380480.bin", "FCutCal.m_AirInletLimit", BookGraph.None, "fcutcal-m-airinletlimit"),
            new("5380480.bin", "IgnNormCal.Map", BookGraph.Surface, "ignnormcal-map-b205r"),
            new("5380480.bin", "PedalMapCal.m_RequestMap", BookGraph.Surface, "pedalmapcal-m-requestmap-b205r"),
            new("5380480.bin", "TorqueCal.M_ManGearLim", BookGraph.None, "torquecal-m-mangearlim"),
            new("5380480.bin", "TorqueCal.m_AirTorqMap", BookGraph.Surface, "torquecal-m-airtorqmap"),
            new("5380480.bin", "TorqueCal.M_EngMaxTab", BookGraph.None, "torquecal-m-engmaxtab"),
            new("5380480.bin", "TorqueCal.M_EngMaxAutTab", BookGraph.None, "torquecal-m-engmaxauttab"),
            new("5380480.bin", "TorqueCal.M_5GearLimTab", BookGraph.None, "torquecal-m-5gearlimtab"),
            new("5380480.bin", "TorqueCal.m_PedYSP", BookGraph.None, "torquecal-m-pedysp"),
            // the right part of the table up at high rpm: the wastegate held shut longer for more airmass
            new("5380480.bin", "BoostCal.RegMap", BookGraph.Surface, "boostcal-regmap-before-after",
                v => Calculate(v, Block(v.Map!, 0, 10, 4, 8), "Multiply", 1.3), true),
            new("5380480.bin", "BstKnkCal.MaxAirmass", BookGraph.Surface, "bstknkcal-maxairmass-before-after",
                v => Calculate(v, All(v.Map!), "Multiply", 1.25), true),
            new("5380480.bin", "FCutCal.m_AirInletLimit", BookGraph.None, "fcutcal-m-airinletlimit-before-after",
                v => Calculate(v, All(v.Map!), "Fill", 1400), true),
            new("5380480.bin", "MaxSpdCal.n_EngLimAir", BookGraph.None, "maxspdcal-n-englimair-before-after",
                v => Calculate(v, Block(v.Map!, 0, 2), "Fill", 6350), true),
            new("5380480.bin", "MaxVehicCal.v_MaxSpeed", BookGraph.None, "maxvehiccal-v-maxspeed-before-after",
                v => Calculate(v, All(v.Map!), "Fill", 280), true),
            new("5380480.bin", "PedalMapCal.m_RequestMap", BookGraph.Surface, "pedalmapcal-m-requestmap-before-after", v =>
            {
                Calculate(v, Block(v.Map!, 0, 1), "Fill", 1350);
                Calculate(v, Block(v.Map!, 1, 2), "Fill", 1100);
            }, true),
            new("5380480.bin", "TorqueCal.m_PedYSP", BookGraph.None, "torquecal-m-pedysp-before-after", PedYsp, true),
            new("5380480.bin", "TorqueCal.M_EngMaxAutTab", BookGraph.None, "torquecal-m-engmaxauttab-before-after", v =>
            {
                Calculate(v, Block(v.Map!, 0, 13), "Fill", 360);
                Calculate(v, Block(v.Map!, 13, 16), "Fill", 300);
            }, true),
            new("5380480.bin", "AirCtrlCal.m_MaxAirTab", BookGraph.None, "airctrlcal-m-maxairtab-fill", v => Calculate(v, All(v.Map!), "Fill", 1350)),

            new("55562425-tuned.bin", "TorqueCal.M_EngMaxE85Tab", BookGraph.None, "torquecal-m-engmaxe85tab"),
            new("55562425-tuned.bin", "TorqueCal.M_ManGearLim", BookGraph.None, "torquecal-m-mangearlim-tuned"),
            new("55562425-tuned.bin", "TorqueCal.M_5GearLimTab", BookGraph.None, "torquecal-m-5gearlimtab-fill", v => Calculate(v, All(v.Map!), "Fill", 410)),
            new("55562425-tuned.bin", "TorqueCal.M_OverBoostTab", BookGraph.None, "torquecal-m-overboosttab"),
            // the axis rescaled to the gearbox's 330 Nm limit, the automatic's limiter with it
            new("55562425-tuned.bin", "TorqueCal.M_EngXSP", BookGraph.None, "torquecal-m-engxsp-before-after",
                v => Type(v, [-39, -19, 0, 25, 50, 80, 110, 140, 170, 200, 230, 260, 290, 310, 320, 330]), true),
            new("55562425-tuned.bin", "TorqueCal.M_EngMaxAutTab", BookGraph.None, "torquecal-m-engmaxauttab-rescaled",
                v => Type(v, s_rescaled["TorqueCal.M_EngMaxAutTab"]), true),
        ];

        /// <summary>
        /// The TxSuite pictures of "Analyzing Trionic 7" (TrionicDocumentation's t7/images/txsuite), light skin only: each map's window
        /// on its own, edits beside the stock map, tiled windows as the workspace shows them, the dialogs alone. T7APP_BOOK_DUMP=&lt;dir&gt;
        /// writes them; without it they are only rendered.
        /// </summary>
        [TestMethod, TestCategory("BookScreenshots")]
        public void BookScreenshots()
        {
            string dir = Directory.CreateDirectory(Path.Combine(s_dir, "book")).FullName;
            string stock = Path.Combine(Here(), "..", "T7Binaries");
            foreach (string bin in new[] { "5388293.bin", "5380480.bin" }) File.Copy(Path.Combine(stock, bin), Path.Combine(dir, bin), true);
            string tuned = Path.Combine(dir, "55562425-tuned.bin");
            File.Copy(Path.Combine(stock, "55562425.bin"), tuned, true);
            var copy = T7.T7Binary.Open(tuned, 0, false);
            foreach (var (name, values) in s_tuned)
            {
                var sh = copy.Find(name);
                var map = new MapData(name, copy.ReadSymbol(sh), copy.TableWidth(name), copy.IsSixteenBitTable(name), copy.SignAbove(name));
                map.Set(values.Select((v, i) => (i, v)));
                copy.WriteSymbol(copy.FileAddress(sh), map.ToBytes(), false);
            }
            copy.UpdateChecksum();

            s_session!.Dispatch(async () =>
            {
                var t7 = new MainViewModel().T7.Settings;
                bool graphs = t7.ShowGraphs, descriptions = t7.ShowSymbolDescriptionLine;
                string? width;
                using (var key = SettingsKey.Open(SettingsKey.Program)) width = key.GetValue("SymbolListWidth") as string;
                try
                {
                    var vm = new MainViewModel();
                    var window = new MainWindow { DataContext = vm, Width = 1800, Height = 640 };
                    window.Show();
                    // the edits are left unsaved: No to the question on closing
                    vm.AskYesNoCancel = _ => Task.FromResult<bool?>(false);
                    Assert.IsTrue(await vm.OpenPlainFileAsync(Path.Combine(dir, "5388293.bin"), true));
                    Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                    SymbolListShot(vm, window);
                    // room for every map window to open at its full size
                    window.Width = 2400;
                    window.Height = 1600;

                    foreach (string bin in s_bookMaps.Select(m => m.Bin).Distinct())
                    {
                        if (vm.Binary!.FileName != Path.Combine(dir, bin)) Assert.IsTrue(await vm.OpenPlainFileAsync(Path.Combine(dir, bin), true));
                        foreach (BookMap shot in s_bookMaps.Where(m => m.Bin == bin))
                        {
                            vm.Settings.ShowGraphs = shot.Graph != BookGraph.None;
                            var viewer = OpenMapWindow(vm, window, shot.Symbol, shot.Graph);
                            if (shot.Edit is { } edit)
                            {
                                WriteableBitmap? before = shot.Before ? MapWindowPicture(window, viewer) : null;
                                edit(MapViewerOf(window, viewer));
                                WriteableBitmap after = MapWindowPicture(window, viewer);
                                SaveBook(before == null ? after : SideBySide(before, after), shot.Name);
                            }
                            else SaveBook(MapWindowPicture(window, viewer), shot.Name);
                            await CloseDocuments(vm);
                        }
                        vm.Settings.ShowGraphs = graphs;

                        if (bin == "5388293.bin")
                        {
                            await TiledShot(vm, window, ["BFuelCal.StartMap", "BFuelCal.Map"], BookGraph.Surface, "bfuelcal-startmap-and-map");
                            var t7bin = (T7.T7Binary)vm.Binary;
                            Dialog(new SidInfoWindow { DataContext = new SidInfoViewModel(t7bin, T7.SidInfo.Read(t7bin)!) }, "sid-information");
                            var fw = vm.T7.FirmwareInfo()!;
                            fw.OpenSIDInfo = true;
                            // the bin has no programming date and the dialog offers now: a fixed one keeps the picture the same on every run
                            fw.ProgrammingDate = "2026-10-20 12:00:00";
                            Dialog(new FirmwareInfoWindow { DataContext = fw }, "firmware-information");
                        }
                        else if (bin == "5380480.bin") await AxisShots(vm, window);
                        else
                        {
                            vm.Settings.ShowGraphs = false;
                            // seven windows in a row are too narrow for a page: two rows of narrow ones
                            string[] limiters = [.. s_rescaled.Keys];
                            void Rescale(MapViewer v)
                            {
                                Type(v, s_rescaled[v.Map!.Name]);
                                v.Grid.Select([]);
                            }
                            await TiledShot(vm, window, limiters[..4], BookGraph.None, "torque-limiters-rescaled-1", 370, Rescale);
                            await TiledShot(vm, window, limiters[4..], BookGraph.None, "torque-limiters-rescaled-2", 370, Rescale);
                            vm.Settings.ShowGraphs = graphs;
                        }
                    }
                    window.Close();
                }
                finally
                {
                    Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
                    t7.ShowGraphs = graphs;
                    t7.ShowSymbolDescriptionLine = descriptions;
                    using var key = SettingsKey.Open(SettingsKey.Program);
                    if (width == null) key.DeleteValue("SymbolListWidth");
                    else key.SetValue("SymbolListWidth", width);
                }
                return true;
            }, default).GetAwaiter().GetResult();

            static void Dialog(Window w, string name)
            {
                w.Show();
                Dispatcher.UIThread.RunJobs();
                SaveBook(w.CaptureRenderedFrame()!, name);
                w.Close();
            }
        }

        // the symbol list with T7Suite's description line under each name, TorqueCal unfolded by selecting its nominal torque map
        private static void SymbolListShot(MainViewModel vm, Window window)
        {
            vm.Settings.ShowSymbolDescriptionLine = true;
            vm.SettingsChanged();
            WorkspacePart<Dock.Model.Avalonia.Controls.ToolDock>(window, "SymbolPane")!.Proportion = 0.9;
            window.CaptureRenderedFrame();
            vm.SelectedSymbol = vm.Binary!.Find("TorqueCal.M_NominalMap");
            Settle(window);
            var grid = window.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "SymbolGrid");
            // the selected symbol scrolls to the top; a few categories above it in sight
            grid.RaiseEvent(new PointerWheelEventArgs(grid, new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true), grid, default, 0,
                PointerPointProperties.None, KeyModifiers.None, new Vector(0, 4.5)));
            Settle(window);
            SaveBook(Crop(window, window.GetVisualDescendants().OfType<Dock.Avalonia.Controls.ToolDockControl>().First()), "symbol-list-descriptions");
            vm.Settings.ShowSymbolDescriptionLine = false;
            vm.SettingsChanged();
        }

        // m_PedYSP's two top points up to the requested 1350 mg/c
        private static void PedYsp(MapViewer v)
        {
            Calculate(v, Block(v.Map!, 0, 1), "Fill", 1350);
            Calculate(v, Block(v.Map!, 1, 2), "Fill", 1000);
            v.Grid.Select(Block(v.Map!, 0, 2));
        }

        // the pedal map over its altered y axis; the airmass map's last torque point to 350 Nm through Edit x-axis, its column up with it
        private static async Task AxisShots(MainViewModel vm, Window window)
        {
            var axis = OpenMapWindow(vm, window, "TorqueCal.m_PedYSP", BookGraph.None);
            PedYsp(MapViewerOf(window, axis));
            await axis.SaveCommand.ExecuteAsync(null);
            await CloseDocuments(vm);
            vm.Settings.ShowGraphs = true;
            SaveBook(MapWindowPicture(window, OpenMapWindow(vm, window, "TorqueCal.X_AccPedalMap", BookGraph.Surface)), "torquecal-x-accpedalmap");
            await CloseDocuments(vm);

            var airTorque = OpenMapWindow(vm, window, "TorqueCal.m_AirTorqMap", BookGraph.Surface);
            airTorque.EditAxisCommand.Execute(true);
            var torqueAxis = vm.Viewers.OfType<MapViewerViewModel>().Single(v => v.MapName == airTorque.XAxisSymbol);
            Settle(window);
            Calculate(MapViewerOf(window, torqueAxis), Block(torqueAxis.Map, 0, 1), "Fill", 350);
            await torqueAxis.SaveCommand.ExecuteAsync(null);
            await CloseDocuments(vm);
            airTorque = OpenMapWindow(vm, window, "TorqueCal.m_AirTorqMap", BookGraph.Surface);
            var map = airTorque.Map;
            int last = map.Cols - 1;
            map.Set(new[] { 1294, 1294, 1294, 1350, 1350, 1350, 1350, 1350, 1350, 1350, 1350, 1350, 1350, 1350, 1332, 1332 }.Select((v, row) => (row * map.Cols + last, v)));
            var shown = MapViewerOf(window, airTorque);
            shown.Grid.Select(Enumerable.Range(0, map.Rows).Select(row => row * map.Cols + last));
            SaveBook(MapWindowPicture(window, airTorque), "torquecal-m-airtorqmap-edited");
            await CloseDocuments(vm);
        }

        // maps side by side as Skin → Tile windows vertically lays them out, in a workspace as wide as their windows open (or width each)
        private static async Task TiledShot(MainViewModel vm, Window window, string[] symbols, BookGraph graph, string name, double width = 0,
            Action<MapViewer>? edit = null)
        {
            var viewers = symbols.Select(s => OpenMapWindow(vm, window, s, graph)).ToList();
            var dock = DocumentDock(window);
            var sizes = viewers.Select(v => ((Dock.Model.Controls.IMdiDocument)dock.VisibleDockables!.First(d => d.Context == v)).MdiBounds).ToList();
            var area = window.GetVisualDescendants().OfType<Dock.Avalonia.Controls.DocumentDockControl>().First();
            var (windowWidth, windowHeight) = (window.Width, window.Height);
            window.Width += sizes.Sum(s => width > 0 ? width : s.Width) - area.Bounds.Width;
            window.Height += sizes.Max(s => s.Height) - area.Bounds.Height;
            Settle(window);
            window.GetLogicalDescendants().OfType<MenuItem>().First(m => Equals(m.Header, "Tile windows vertically")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Settle(window);
            if (edit != null)
                foreach (var v in viewers) edit(MapViewerOf(window, v));
            Settle(window);
            SaveBook(Crop(window, area), name);
            await CloseDocuments(vm);
            window.Width = windowWidth;
            window.Height = windowHeight;
            Settle(window);
        }

        private static MapViewerViewModel OpenMapWindow(MainViewModel vm, Window window, string symbol, BookGraph graph)
        {
            var viewer = vm.OpenSymbolByName(symbol)!;
            Assert.IsNotNull(viewer, symbol);
            Settle(window);
            // wide enough for its title, which names the map and the file
            var inner = InnerWindow(window, viewer);
            var title = inner.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text?.StartsWith("Symbol: ") == true);
            var natural = new TextBlock { Text = title.Text, FontFamily = title.FontFamily, FontSize = title.FontSize, FontWeight = title.FontWeight };
            natural.Measure(Size.Infinity);
            var mdi = (Dock.Model.Controls.IMdiDocument)DocumentDock(window).VisibleDockables!.First(d => d.Context == viewer);
            var bounds = mdi.MdiBounds;
            double width = Math.Ceiling(inner.Bounds.Width - title.Bounds.Width + natural.DesiredSize.Width + 8);
            if (width > bounds.Width) mdi.MdiBounds = new Dock.Model.Core.DockRect(bounds.X, bounds.Y, width, bounds.Height);
            Settle(window);
            if (graph != BookGraph.None) MapViewerOf(window, viewer).GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = graph == BookGraph.Surface ? 0 : 1;
            Settle(window);
            return viewer;
        }

        private static Dock.Model.Controls.IDocumentDock DocumentDock(Window window) =>
            (Dock.Model.Controls.IDocumentDock)window.GetVisualDescendants().OfType<Dock.Avalonia.Controls.DocumentDockControl>().First().DataContext!;

        private static MapViewer MapViewerOf(Window window, MapViewerViewModel viewer) =>
            window.GetVisualDescendants().OfType<MapViewer>().First(m => m.Map == viewer.Map);

        // the map's inner window at the size it opened at (Auto size new mapwindows), title bar and all
        private static WriteableBitmap MapWindowPicture(Window window, MapViewerViewModel viewer)
        {
            Settle(window);
            return Crop(window, InnerWindow(window, viewer));
        }

        private static Dock.Avalonia.Controls.MdiDocumentWindow InnerWindow(Window window, MapViewerViewModel viewer) =>
            window.GetVisualDescendants().OfType<Dock.Avalonia.Controls.MdiDocumentWindow>().First(w => (w.DataContext as Dock.Model.Core.IDockable)?.Context == viewer);

        private static void Settle(Window window)
        {
            for (int i = 0; i < 3; i++)
            {
                Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame();
            }
        }

        // the cells of display rows [top, bottom) (from the top of the table), columns [left, right)
        private static int[] Block(MapData map, int top, int bottom, int left = 0, int right = int.MaxValue) =>
            [.. from r in Enumerable.Range(top, bottom - top) from c in Enumerable.Range(left, Math.Min(right, map.Cols) - left) select map.Index(r, c)];

        private static int[] All(MapData map) => [.. Enumerable.Range(0, map.Count)];

        // the viewer's toolbar on the selected cells, as a user picks the operation, types the value and presses Execute
        private static void Calculate(MapViewer viewer, int[] cells, string operation, double value)
        {
            viewer.Grid.Select(cells);
            var execute = viewer.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Execute"));
            var tools = ((Panel)execute.Parent!).Children;
            tools.OfType<ComboBox>().Single().SelectedItem = operation;
            tools.OfType<TextBox>().Single().Text = value.ToString(CultureInfo.CurrentCulture);
            execute.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Select(viewer, cells);
        }

        // the changed cells stay selected, but a whole table selected hides its colours (a flat one has none, grey)
        private static void Select(MapViewer viewer, int[] cells) =>
            viewer.Grid.Select(cells.Length == viewer.Map!.Count && viewer.Map.MinValue() != viewer.Map.MaxValue() ? [] : cells);

        // values as typed into the table, lowest row first
        private static void Type(MapViewer viewer, int[] values)
        {
            var map = viewer.Map!;
            int[] changed = [.. Enumerable.Range(0, values.Length).Where(i => map[i] != values[i])];
            map.Set(changed.Select(i => (i, values[i])));
            Select(viewer, changed);
        }

        private static WriteableBitmap Crop(Window window, Control part)
        {
            WriteableBitmap frame = window.CaptureRenderedFrame()!;
            PixelRect r = PixelRect.FromRect(new Rect(part.TranslatePoint(default, window)!.Value, part.Bounds.Size), 1)
                .Intersect(new PixelRect(frame.PixelSize));
            var crop = new WriteableBitmap(r.Size, frame.Dpi, frame.Format, frame.AlphaFormat);
            using var from = frame.Lock();
            using var to = crop.Lock();
            byte[] row = new byte[r.Width * 4];
            for (int y = 0; y < r.Height; y++)
            {
                Marshal.Copy(from.Address + (r.Y + y) * from.RowBytes + r.X * 4, row, 0, row.Length);
                Marshal.Copy(row, 0, to.Address + y * to.RowBytes, row.Length);
            }
            return crop;
        }

        // before and after, a white gap between
        private static WriteableBitmap SideBySide(WriteableBitmap left, WriteableBitmap right)
        {
            const int gap = 16;
            var all = new WriteableBitmap(new PixelSize(left.PixelSize.Width + gap + right.PixelSize.Width, Math.Max(left.PixelSize.Height, right.PixelSize.Height)),
                left.Dpi, left.Format, left.AlphaFormat);
            using var to = all.Lock();
            byte[] white = Enumerable.Repeat((byte)0xFF, to.RowBytes).ToArray();
            for (int y = 0; y < to.Size.Height; y++) Marshal.Copy(white, 0, to.Address + y * to.RowBytes, white.Length);
            foreach (var (part, x) in new[] { (left, 0), (right, left.PixelSize.Width + gap) })
            {
                using var from = part.Lock();
                byte[] row = new byte[from.Size.Width * 4];
                for (int y = 0; y < from.Size.Height; y++)
                {
                    Marshal.Copy(from.Address + y * from.RowBytes, row, 0, row.Length);
                    Marshal.Copy(row, 0, to.Address + y * to.RowBytes + x * 4, row.Length);
                }
            }
            return all;
        }

        private static void SaveBook(WriteableBitmap picture, string name)
        {
            string? dir = Environment.GetEnvironmentVariable("T7APP_BOOK_DUMP");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            picture.Save(Path.Combine(dir, name + ".png"), new PngBitmapEncoderOptions());
        }
    }
}
