using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
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
using Dock.Avalonia.Controls;
using MapControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.ViewModels;
using SuiteApp.Views;
using TxSuite.ViewModels;
using TxSuite.Views;
using T5App.ViewModels;
using T5App.Views;
using Trionic5Tools;

namespace T5AppTest
{
    public partial class AppTest
    {
        // the book's plain map viewers: the old picture's symbol on a stock bin of its engine type, the graph where the old picture had one
        private static readonly (string Bin, string Symbol, string Name, bool Graph)[] BookMaps =
        [
            ("4300828", "Insp_mat!", "insp-mat", true),
            ("4300828", "Startvev_fak!", "startvev-fak", false),
            ("4300828", "Idle_fuel_korr!", "idle-fuel-korr", true),
            ("4300828", "Fuel_knock_mat!", "fuel-knock-mat", true),
            ("4300828", "Knock_ref_matrix!", "knock-ref-matrix", true),
            ("4300828", "Knock_press_tab!", "knock-press-tab", false),
            ("4300828", "Ign_map_0!", "ign-map-0", true),
            ("4300828", "Ign_map_1!", "ign-map-1", false),
            ("4300828", "Ign_map_2!", "ign-map-2", true),
            ("4300828", "Reg_kon_mat!", "reg-kon-mat", true),
            ("4300828", "Tryck_mat!", "tryck-mat", true),
            ("4300828", "Tryck_vakt_tab!", "tryck-vakt-tab", false),
            ("4781886", "Adapt_korr!", "adapt-korr", true),
            ("4781886", "P_fors!", "p-fors", true),
            ("4781886", "I_fors!", "i-fors", true),
            ("4781886", "D_fors!", "d-fors", true),
            ("4781886", "Inj_konst!", "inj-konst", false),
            ("4781886", "Insp_mat!", "insp-mat-b234r", true),
            ("9132671", "Ign_map_4!", "ign-map-4", true),
            ("4781035", "Gear_st!", "gear-st", false),
            ("4302329", "Tryck_mat!", "tryck-mat-fpt", false),
            ("4302329", "Ign_map_0!", "ign-map-0-fpt", false),
            ("4302329", "Batt_korr_tab!", "batt-korr-tab", false),
            // all 8, as the text reads it (4300828 steps up from 4)
            ("4302329", "Apc_knock_tab!", "apc-knock-tab", false),
            ("4301941", "Insp_mat!", "insp-mat-4301941", false),
        ];

        // viewers side by side, the old picture's rows
        private static readonly (string Bin, string[] Symbols, string Name, bool Graph, int Columns)[] BookTiles =
        [
            ("4300828", ["Open_loop!", "Open_loop_knock!"], "open-loop-tiled", false, 2),
            ("4300828", ["Lacc_konst!", "Accel_konst!", "Lret_konst!", "Retard_konst!"], "acc-ret-konst-tiled", true, 2),
            ("4781886", ["Fload_throt_tab!", "Fload_tab!"], "fload-tiled", false, 2),
            ("4300828", ["D_fors!", "I_fors!", "P_fors!"], "pid-tiled", true, 3),
            ("4300828", ["Regl_tryck_fgm!", "Regl_tryck_sgm!"], "regl-tryck-tiled", false, 2),
        ];

        /// <summary>
        /// The pictures of the book "Analyzing Trionic 5" (TrionicDocumentation, t5/images/txsuite), in the light skin the book prints in:
        /// T5APP_BOOK_DUMP=&lt;dir&gt; writes them, without it they only render. A map is its viewer's window at the size it opens at,
        /// tiled viewers the workspace, dialogs on their own. What a picture needs is made in the temp folder: stock bins under their own
        /// names, a copy marked for a 3.0 bar MAP sensor, one converted by the MAP sensor wizard, and a log.
        /// </summary>
        [TestMethod, TestCategory("BookScreenshots")]
        public void BookScreenshots()
        {
            string dir = Directory.CreateDirectory(Path.Combine(s_dir, "book")).FullName;
            string Stock(string partnumber, string? name = null)
            {
                string file = Path.Combine(dir, (name ?? partnumber) + ".bin");
                if (!File.Exists(file)) File.Copy(Path.Combine(Here(), "..", "T5Binaries", partnumber + ".bin"), file);
                return file;
            }
            string marked = Stock("4302329", "4302329-3bar"), converted = Stock("4302329", "4302329-converted");
            using (var fs = new FileStream(marked, FileMode.Open))
            {
                // the 3.0 bar marker alone, as ToolConversionsTest sets it: the stock maps read for the other sensor
                fs.Position = fs.Length - 0x1FF;
                fs.WriteByte(0x01);
            }
            string log = BookLog(dir);

            s_session!.Dispatch(async () =>
            {
                var vm = new MainViewModel();
                var window = new MainWindow { DataContext = vm, Width = 2000, Height = 1400 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(Stock("4239273"), true));
                AppSettings settings = vm.T5.Settings;
                T5AppSettings t5 = vm.T5.T5Settings;
                bool graphs = settings.ShowGraphs, advanced = t5.EnableAdvancedMode;
                string adapter = settings.Adapter;
                string? image = Environment.GetEnvironmentVariable("APPIMAGE");
                string? width;
                using (var key = SettingsKey.Open(SettingsKey.Program)) width = key.GetValue("SymbolListWidth") as string;
                try
                {
                    // no stock bins next to the program (PartNumberList leaves two in the test's output folder)
                    Environment.SetEnvironmentVariable("APPIMAGE", Path.Combine(dir, "TxSuite.AppImage"));
                    Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

                    foreach (var group in BookMaps.GroupBy(m => m.Bin))
                    {
                        Assert.IsTrue(await vm.OpenPlainFileAsync(Stock(group.Key), true));
                        foreach (var (_, symbol, name, graph) in group)
                        {
                            settings.ShowGraphs = graph;
                            BookDocument(window, BookViewer(vm, window, symbol), name);
                            await CloseDocuments(vm);
                        }
                    }

                    foreach (var (bin, symbols, name, graph, columns) in BookTiles)
                    {
                        settings.ShowGraphs = graph;
                        Assert.IsTrue(await vm.OpenPlainFileAsync(Stock(bin), true));
                        foreach (string symbol in symbols) BookViewer(vm, window, symbol);
                        SideBySide(window, name, columns);
                        await CloseDocuments(vm);
                    }

                    await BookThreeBar(vm, window, marked, converted);
                    await BookAxisEdit(vm, window, Stock("4301941"));
                    await BookWizards(vm, window, Stock("4301974"), Stock("4781886"));
                    await BookEcu(vm, window, Stock("4239273"), log);
                    BookSymbolList(vm, window);
                }
                finally
                {
                    window.Close();
                    Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
                    Environment.SetEnvironmentVariable("APPIMAGE", image);
                    settings.ShowGraphs = graphs;
                    settings.Adapter = adapter;
                    t5.EnableAdvancedMode = advanced;
                    using var key = SettingsKey.Open(SettingsKey.Program);
                    if (width == null) key.DeleteValue("SymbolListWidth");
                    else key.SetValue("SymbolListWidth", width);
                }
                return true;
            }, default).GetAwaiter().GetResult();
        }

        // three minutes of driving as T5Suite's CanTraceExt log had them: boost, intake air, rpm and the boost request, two pulls
        private static string BookLog(string dir)
        {
            (double t, double rpm, double boost, double request)[] keys =
            [
                (0, 2250, -0.35, -0.2), (6, 2700, -0.3, -0.2), (12, 2650, -0.35, -0.2), (40, 2600, -0.4, -0.2), (44, 3100, 0.6, 0.9),
                (46, 2700, -0.4, -0.2), (100, 2550, -0.45, -0.2), (106, 1200, -0.75, -0.2), (109, 2700, -0.3, -0.2), (138, 2650, -0.35, -0.2),
                (143, 3400, 0.85, 1.1), (145, 2000, -0.6, -0.2), (148, 2600, -0.4, -0.2), (180, 2700, -0.35, -0.2),
            ];
            string log = Path.Combine(dir, "4239273-20261020-CanTraceExt.t5l");
            var t0 = new DateTime(2026, 10, 20, 10, 26, 20);
            var noise = new Random(5);
            File.WriteAllLines(log, Enumerable.Range(0, 900).Select(i =>
            {
                double t = i / 5.0;
                int k = 0;
                while (keys[k + 1].t < t) k++;
                var (a, b) = (keys[k], keys[k + 1]);
                double f = (t - a.t) / (b.t - a.t);
                return RealtimeLog.Line(t0.AddMilliseconds(i * 200),
                [
                    ("P_medel", Math.Round(a.boost + f * (b.boost - a.boost) + (noise.NextDouble() - 0.5) * 0.04, 2)),
                    ("Lufttemp", 39 + Math.Floor(t / 50) + (t > 144 && t < 160 ? 3 : 0)),
                    ("Rpm", Math.Round(a.rpm + f * (b.rpm - a.rpm) + noise.Next(-15, 16))),
                    ("Max_tryck", Math.Round(a.request + f * (b.request - a.request), 2)),
                ], false);
            }));
            return log;
        }

        private static MapViewerViewModel BookViewer(MainViewModel vm, Window window, string symbol)
        {
            vm.OpenSymbolByName(symbol);
            var viewer = vm.Viewers.OfType<MapViewerViewModel>().Last(v => v.MapName == symbol);
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            return viewer;
        }

        private static void BookDocument(Window window, MapViewerViewModel viewer, string name)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            Book(window, window.GetVisualDescendants().OfType<MapViewer>().First(m => m.Map == viewer.Map).GetVisualAncestors().OfType<MdiDocumentWindow>().First(), name);
        }

        private static DocumentDockControl Documents(Window window) => window.GetVisualDescendants().OfType<DocumentDockControl>().First();

        // the open viewers side by side at the size they opened at, columns to a row (as Skin → Tile windows vertically shows two or
        // three, without squeezing a wide map), the documents area around them
        private static void SideBySide(Window window, string name, int columns = 4)
        {
            var area = Documents(window);
            var docs = ((Dock.Model.Controls.IDocumentDock)area.DataContext!).VisibleDockables!.OfType<Dock.Model.Controls.IMdiDocument>().ToList();
            double x = 0, y = 0, row = 0, width = 0;
            for (int i = 0; i < docs.Count; i++)
            {
                if (i > 0 && i % columns == 0) (x, y, row) = (0, y + row, 0);
                var (w, h) = (docs[i].MdiBounds.Width, docs[i].MdiBounds.Height);
                docs[i].MdiBounds = new Dock.Model.Core.DockRect(x, y, w, h);
                (x, width, row) = (x + w, Math.Max(width, x + w), Math.Max(row, h));
            }
            var size = (window.Width, window.Height);
            window.Width = Math.Max(window.Width, window.Width + width - area.Bounds.Width);
            window.Height = Math.Max(window.Height, window.Height + y + row - area.Bounds.Height);
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            Book(window, new Rect(area.TranslatePoint(default, window)!.Value, new Size(width, y + row)), name);
            (window.Width, window.Height) = size;
            Dispatcher.UIThread.RunJobs();
        }

        // 08-tuning's 3 bar pages on a 9000 FPT bin: the marked copy's stock maps read for a 3.0 bar sensor, the MAP sensor wizard's
        // question and the maps it altered (09-can-bus' Tryck_mat! too)
        private static async Task BookThreeBar(MainViewModel vm, Window window, string marked, string converted)
        {
            vm.Settings.ShowGraphs = false;
            Assert.IsTrue(await vm.OpenPlainFileAsync(marked, true));
            Assert.AreEqual(MapSensorType.MapSensor30, vm.T5.CurrentMapSensor);
            foreach (var (symbol, name) in new[] { ("Tryck_mat!", "tryck-mat-3bar"), ("Ign_map_0!", "ign-map-0-3bar") })
            {
                BookDocument(window, BookViewer(vm, window, symbol), name);
                await CloseDocuments(vm);
            }

            Assert.IsTrue(await vm.OpenPlainFileAsync(converted, true));
            vm.T5.T5Settings.EnableAdvancedMode = true;
            vm.T5.SettingsChanged();
            window.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Header as string == "Convert to 3.0 bar MAP sensor").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var question = Owned(window);
            question.CaptureRenderedFrame();
            Book(question, null, "map-sensor-wizard");
            question.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Yes").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            (await Shown(window, "3.0 bar mapsensor report")).Close();
            Assert.AreEqual(MapSensorType.MapSensor30, vm.T5.CurrentMapSensor);
            BookDocument(window, BookViewer(vm, window, "Ign_map_0!"), "ign-map-0-3bar-converted");
            await CloseDocuments(vm);
            vm.Settings.ShowGraphs = true;
            BookDocument(window, BookViewer(vm, window, "Tryck_mat!"), "tryck-mat-3bar-3d");
            await CloseDocuments(vm);
        }

        // 08-tuning's supporting axis: Insp_mat!'s right-click menu on a cell of its first column, and Edit y-axis' viewer beside it
        private static async Task BookAxisEdit(MainViewModel vm, Window window, string file)
        {
            vm.Settings.ShowGraphs = false;
            Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
            var fuel = BookViewer(vm, window, "Insp_mat!");
            var grid = window.GetVisualDescendants().OfType<MapViewer>().Single().Grid;
            double row = grid.Bounds.Height / (fuel.Map.Rows + 1);
            Point at = grid.TranslatePoint(new Point(grid.Bounds.Width / (fuel.Map.Cols + 1) * 1.5, row * 5.5), window)!.Value;
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            window.MouseDown(at, MouseButton.Right);
            window.MouseUp(at, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            var edit = window.GetVisualDescendants().OfType<MenuItem>().First(m => m.Header as string == "Edit y-axis");
            window.MouseMove(edit.TranslatePoint(new Point(edit.Bounds.Width / 2, edit.Bounds.Height / 2), window)!.Value);
            BookDocument(window, fuel, "insp-mat-menu");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();

            fuel.EditAxisCommand.Execute(false);
            BookViewer(vm, window, "Fuel_map_yaxis!");
            SideBySide(window, "fuel-map-yaxis-tiled");
            await CloseDocuments(vm);
            vm.Settings.ShowGraphs = true;
        }

        // 08-tuning's wizards: Tune me up on a 9000 Aero bin at stage 1 and at its free settings, the injector wizard on a B234R bin
        private static async Task BookWizards(MainViewModel vm, Window window, string aero, string b234r)
        {
            Assert.IsTrue(await vm.OpenPlainFileAsync(aero, true));
            var tune = vm.T5.TuneMeUp()!;
            Assert.AreEqual(1, tune.Stage);
            BookAlone(new TuneMeUpWindow { DataContext = tune }, "tune-me-up-stage1");
            tune.Stage = 4;
            BookAlone(new TuneMeUpWindow { DataContext = tune }, "tune-me-up-stage4");

            Assert.IsTrue(await vm.OpenPlainFileAsync(b234r, true));
            var injectors = vm.T5.InjectorWizard()!;
            injectors.Type = InjectorType.Siemens630Dekas;
            BookAlone(new InjectorWizardWindow { DataContext = injectors }, "injector-wizard");
        }

        // 05-symbol-tables: the list wide enough for the descriptions, the boost request map selected, the fuel and ignition basics unfolded
        private static void BookSymbolList(MainViewModel vm, Window window)
        {
            (window.Width, window.Height) = (1600, 1250);
            var parts = NameScope.GetNameScope(window.GetLogicalDescendants().OfType<SuiteWorkspace>().First())!;
            parts.Find<Dock.Model.Avalonia.Controls.ToolDock>("SymbolPane")!.Proportion = 1000 / parts.Find<DockControl>("Workspace")!.Bounds.Width;
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            var bin = (T5Binary)vm.Binary!;
            var grid = window.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "SymbolGrid");
            foreach (string category in new[] { "Fuel", "Ignition" })
            {
                var group = vm.Symbols!.Groups!.OfType<DataGridCollectionViewGroup>().Single(g => Equals(g.Key, category));
                grid.ExpandRowGroup(group, false);
                grid.ExpandRowGroup(group.Items.OfType<DataGridCollectionViewGroup>().Single(g => g.Key is GroupKey { Name: "Basic" }), false);
            }
            vm.SelectedSymbol = bin.Find("Tryck_mat!");
            grid.ScrollIntoView(vm.Symbols!.Groups![0], null);
            window.MouseMove(new Point(window.Width - 1, window.Height - 1));
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            // up to the descriptions, without the empty User description column
            var list = window.GetVisualDescendants().OfType<SymbolListView>().First();
            var description = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().First(h => Equals(h.Content, "Description"));
            Point top = list.TranslatePoint(default, window)!.Value;
            Book(window, new Rect(top, new Size(description.TranslatePoint(new Point(description.Bounds.Width, 0), list)!.Value.X, list.Bounds.Height)), "symbol-list");
        }

        // 09-can-bus: the ECU menu, a flash under way, the realtime panel's user defined symbols and a log
        private static async Task BookEcu(MainViewModel vm, Window window, string file, string log)
        {
            (window.Width, window.Height) = (1600, 900);
            Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
            var ecu = window.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Name == "EcuMenu");
            ecu.Open();
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            var dropDown = ecu.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().First().Child!;
            Rect Bounds(Control c) => new(c.TranslatePoint(default, window)!.Value, c.Bounds.Size);
            Book(window, Bounds(ecu).Union(Bounds(dropDown)), "ecu-menu");
            ecu.Close();

            window.Width = 800;
            await vm.T5.RunFlasherAsync(() =>
            {
                // as the flasher's progress reports it
                vm.ProgressText = "48 %";
                Dispatcher.UIThread.RunJobs();
                Book(window, window.GetVisualDescendants().OfType<StatusBar>().Single(), "flash-progress");
                return Task.CompletedTask;
            });
            window.Width = 1600;

            vm.Settings.Adapter = "";
            vm.T5.T5Settings.EnableAdvancedMode = true;
            vm.T5.SettingsChanged();
            vm.ConfigureRealtimePanelCommand.Execute(null);
            var rt = (T5RealtimeViewModel)vm.Realtime!;
            rt.SelectedTab = (int)T5RealtimeTab.Userdefined;
            rt.Apply(new RealtimeSample(DateTime.Now,
            [
                ("Rpm", 2840), ("P_medel", 0.42), ("Lufttemp", 31), ("Kyl_temp", 89), ("Medeltrot", 46), ("Regl_tryck", 0.55), ("Max_tryck", 0.6),
                ("AD_sond", 0.98), ("Insptid_ms10", 6.4), ("Ign_angle", 21.5), ("Pgm_status", 0x02000000),
            ], 20, null));
            BookMaximized(window, "realtime-panel");
            await CloseDocuments(vm);

            window.Height = 620;
            await vm.OpenLogAsync(log, _ => Task.FromResult<int?>(0));
            BookMaximized(window, "log-viewer");
            await CloseDocuments(vm);
        }

        private static void BookMaximized(Window window, string name)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            foreach (var mdi in Documents(window).DataContext is Dock.Model.Controls.IDocumentDock dock ? dock.VisibleDockables?.OfType<Dock.Model.Controls.IMdiDocument>() ?? [] : [])
                mdi.MdiState = Dock.Model.Core.MdiWindowState.Maximized;
            window.MouseMove(new Point(window.Width - 1, window.Height - 1));
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            Book(window, Documents(window), name);
        }

        private static void BookAlone(Window w, string name)
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            w.CaptureRenderedFrame();
            Book(w, null, name);
            w.Close();
        }

        private static async Task CloseDocuments(MainViewModel vm)
        {
            foreach (DocumentViewModel d in vm.Viewers.ToList()) await vm.CloseViewerAsync(d);
            Dispatcher.UIThread.RunJobs();
        }

        // a dialog a menu handler shows after its work (an async void handler can't be awaited)
        private static async Task<Window> Shown(Window owner, string title)
        {
            for (int i = 0; i < 200; i++)
            {
                Dispatcher.UIThread.RunJobs();
                if (owner.OwnedWindows.LastOrDefault(w => w.Title == title) is { } shown) return shown;
                await Task.Delay(50);
            }
            throw new AssertFailedException(title + " didn't show: " + string.Join(", ", owner.OwnedWindows.Select(w => w.Title)));
        }

        private static void Book(Window w, Control? part, string name) =>
            Book(w, part == null ? new Rect(w.ClientSize) : new Rect(part.TranslatePoint(default, w)!.Value, part.Bounds.Size), name);

        // the frame cropped to area (window coordinates, the headless scaling of 1)
        private static void Book(Window w, Rect area, string name)
        {
            WriteableBitmap frame = w.CaptureRenderedFrame()!;
            string? dir = Environment.GetEnvironmentVariable("T5APP_BOOK_DUMP");
            if (string.IsNullOrEmpty(dir)) return;
            var crop = PixelRect.FromRect(area, 1).Intersect(new PixelRect(frame.PixelSize));
            int stride = crop.Width * 4;
            var pixels = GCHandle.Alloc(new byte[stride * crop.Height], GCHandleType.Pinned);
            try
            {
                frame.CopyPixels(crop, pixels.AddrOfPinnedObject(), stride * crop.Height, stride);
                using var bitmap = new Bitmap(frame.Format!.Value, frame.AlphaFormat!.Value, pixels.AddrOfPinnedObject(), crop.Size, frame.Dpi, stride);
                Directory.CreateDirectory(dir);
                bitmap.Save(Path.Combine(dir, name + ".png"), new PngBitmapEncoderOptions());
            }
            finally
            {
                pixels.Free();
            }
        }
    }
}
