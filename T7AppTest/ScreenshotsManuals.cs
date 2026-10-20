using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.Services;
using SuiteApp.ViewModels;
using SuiteApp.Views;
using T7App.ViewModels;
using T7App.Views;
using TxSuite.ViewModels;
using TxSuite.Views;

namespace T7AppTest
{
    public partial class AppTest
    {
        /// <summary>
        /// The user manuals' T7 pictures (the shared chapters, rendered on T7, and the Trionic 7 ones) into docs/screenshots/t7 as
        /// Screenshots does, each in both skins. What a shot shows is made here in the temp folder: a B235R stock bin to compare
        /// with, a copy with one map changed, a log, two projects with saved maps, fault codes and autotune proposals.
        /// </summary>
        [TestMethod, TestCategory("Screenshots")]
        public void ManualScreenshots()
        {
            string dir = Directory.CreateDirectory(Path.Combine(s_dir, "manual")).FullName;
            string stock = Path.Combine(Here(), "..", "T7Binaries");
            string file = Path.Combine(dir, "5168646.bin"), other = Path.Combine(dir, "5380555.bin"), tuned = Path.Combine(dir, "5168646-tuned.bin");
            File.Copy(Path.Combine(stock, "5168646.bin"), file, true);
            File.Copy(Path.Combine(stock, "5380555.bin"), other, true);
            File.Copy(file, tuned, true);
            // a degree more in a corner of the ignition map: a few lines of the binary compare
            var copy = T7.T7Binary.Open(tuned, 0, false);
            var ign = copy.Find("IgnNormCal.Map");
            byte[] advance = copy.ReadSymbol(ign);
            for (int row = 12; row < 15; row++)
                for (int col = 10; col < 14; col++)
                {
                    int i = (row * 18 + col) * 2, v = (short)(advance[i] << 8 | advance[i + 1]) + 10;
                    (advance[i], advance[i + 1]) = ((byte)(v >> 8), (byte)v);
                }
            copy.WriteSymbol(copy.FileAddress(ign), advance, false);
            // a minute of driving: idle, a pull through second and third, lift off, cruise, a pull in fourth (rpm, boost, ignition)
            (double t, double rpm, double boost, double ign)[] keys =
            [
                (0, 850, -0.65, 12), (4, 870, -0.65, 12), (6, 2300, 0.1, 22), (6.5, 1600, -0.2, 26), (8, 2600, 0.6, 18), (10, 4000, 1.05, 16),
                (14, 6000, 0.95, 20), (14.5, 4200, 0.4, 22), (16, 4600, 1.0, 17), (22, 6000, 0.85, 20), (24, 3800, -0.7, 28), (27, 2400, -0.4, 32),
                (45, 2450, -0.35, 33), (47, 2900, 0.5, 21), (50, 3800, 1.05, 16), (55, 5200, 0.95, 19), (57, 3200, -0.7, 28), (60, 2000, -0.5, 30),
            ];
            string log = Path.Combine(dir, "5168646-20261012-CanTraceExt.t7l");
            var t0 = new DateTime(2026, 10, 12, 16, 30, 0);
            var noise = new Random(7);
            File.WriteAllLines(log, Enumerable.Range(0, 600).Select(i =>
            {
                // between two of the points, with a real engine's jitter
                double t = i / 10.0;
                int k = 0;
                while (keys[k + 1].t < t) k++;
                var (a, b) = (keys[k], keys[k + 1]);
                double f = (t - a.t) / (b.t - a.t);
                return RealtimeLog.Line(t0.AddMilliseconds(i * 100),
                [
                    ("ActualIn.n_Engine", Math.Round(a.rpm + f * (b.rpm - a.rpm) + noise.Next(-20, 21))),
                    ("In.p_AirInlet", Math.Round(a.boost + f * (b.boost - a.boost) + (noise.NextDouble() - 0.5) * 0.03, 3)),
                    ("Out.fi_Ignition", Math.Round(a.ign + f * (b.ign - a.ign) + (noise.NextDouble() - 0.5) * 0.8, 1)),
                ], false);
            }));

            s_session!.Dispatch(async () =>
            {
                var t7 = new MainViewModel().T7.Settings;
                string projectFolder = t7.ProjectFolder, adapter = t7.Adapter;
                bool notes = t7.RequestProjectNotes;
                string? width;
                using (var key = SettingsKey.Open(SettingsKey.Program)) width = key.GetValue("SymbolListWidth") as string;
                try
                {
                    foreach (var (theme, suffix) in new[] { (ThemeVariant.Light, ""), (ThemeVariant.Dark, "-dark") })
                    {
                        var vm = new MainViewModel();
                        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
                        window.Show();
                        Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                        // the status bar without the loading's progress text: its right part kept that text's width at times (the panel
                        // wasn't measured again when the text hid), the read-only text off the right edge
                        Dispatcher.UIThread.RunJobs();
                        foreach (var panel in window.GetVisualDescendants().OfType<StatusBar>().Single().GetVisualDescendants().OfType<StackPanel>())
                            panel.InvalidateMeasure();
                        // after the window applied the skin from the settings
                        Application.Current!.RequestedThemeVariant = theme;
                        MainWindowShots(vm, window, suffix);
                        DialogShots(vm, window, suffix, tuned);
                        await DocumentShots(vm, window, suffix, other, log);
                        await ProjectShots(vm, window, suffix, file, other, Path.Combine(dir, "Projects" + suffix), projectFolder);
                        // Settings as they were for the next skin's settings-common
                        vm.Settings.ProjectFolder = projectFolder;
                        vm.Settings.RequestProjectNotes = notes;
                        window.Close();
                    }
                }
                finally
                {
                    Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
                    t7.ShowRibbon = false;
                    t7.ProjectFolder = projectFolder;
                    t7.Adapter = adapter;
                    t7.RequestProjectNotes = notes;
                    // the symbol list's width the windows kept: Screenshots' window opens as it would on its own
                    using var key = SettingsKey.Open(SettingsKey.Program);
                    if (width == null) key.DeleteValue("SymbolListWidth");
                    else key.SetValue("SymbolListWidth", width);
                }
                return true;
            }, default).GetAwaiter().GetResult();
        }

        // a path in the test's temp folder as one in the home folder, not whoever made the screenshots'
        private static string Home(string path) => Path.Combine("~", Path.GetFileName(path));

        // a dialog on its own
        private static void Alone(Window w, string name)
        {
            if (!w.IsVisible) w.Show();
            Dispatcher.UIThread.RunJobs();
            Save(w, name);
            w.Close();
        }

        // the dialog a menu handler showed over the main window (Dialogs' and the partnumber list's windows are made in code)
        private static Window Shown(Window owner)
        {
            Dispatcher.UIThread.RunJobs();
            return owner.OwnedWindows.Last();
        }

        private static void RightClick(Window window, Point at)
        {
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Right);
            window.MouseUp(at, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
        }

        private static void CloseMenu(Window window)
        {
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
        }

        private static async Task CloseDocuments(MainViewModel vm)
        {
            foreach (DocumentViewModel d in vm.Viewers.ToList()) await vm.CloseViewerAsync(d);
            Dispatcher.UIThread.RunJobs();
        }

        // the main window with the file open, no documents: the ribbon, the symbol list searched and its header menu
        private static void MainWindowShots(MainViewModel vm, Window window, string suffix)
        {
            vm.Settings.ShowRibbon = true;
            vm.SettingsChanged();
            Dispatcher.UIThread.RunJobs();
            Save(window, "ribbon" + suffix);
            vm.Settings.ShowRibbon = false;
            vm.SettingsChanged();

            vm.SearchText = "BFuelCal";
            // the search unfolds the categories after a layout
            window.CaptureRenderedFrame();
            Dispatcher.UIThread.RunJobs();
            Save(window, "symbol-list-search" + suffix);
            vm.SearchText = "";
            window.CaptureRenderedFrame();

            // a right click on the end of the Symbol name header: the menu opens there, the caption stays in sight
            var grid = window.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "SymbolGrid");
            var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().First(h => Equals(h.Content, "Symbol name"));
            RightClick(window, header.TranslatePoint(new Point(header.Bounds.Width - 12, header.Bounds.Height / 2), window)!.Value);
            Save(window, "symbol-list-header-menu" + suffix);
            CloseMenu(window);
        }

        // the dialogs, each on its own, as their menu items open them on the open T7 bin
        private static void DialogShots(MainViewModel vm, Window window, string suffix, string tuned)
        {
            var filters = new LogFiltersViewModel(new LogFilterCollection(), vm.LogFilterSymbols);
            filters.Rows.Add(new LogFilterRow { Symbol = "ActualIn.n_Engine", Type = 0, Value = 2000, Active = true });
            Alone(new LogFiltersWindow { DataContext = filters }, "log-filters" + suffix);

            // TxSuite's lookup: every ECU's partnumbers, the "..." list
            var lookup = new PartLookupViewModel(vm.LookupAnyPartNumber, vm.Caption)
            {
                Own = vm.LookupPartNumber, Browse = () => Task.FromResult<string?>(null), PartNumber = "5168646",
            };
            lookup.Lookup();
            Assert.IsNotNull(lookup.Info);
            Alone(new PartLookupWindow { DataContext = lookup }, "lookup-partnumber" + suffix);

            // the list starts folded after its first layout; the T7 tab's first car model unfolded
            _ = PartNumberListWindow.Show(window, 1, MainView.PartNumberTabs());
            var list = Shown(window);
            list.CaptureRenderedFrame();
            var partGrid = list.GetVisualDescendants().OfType<DataGrid>().First(g => g.IsEffectivelyVisible);
            var model = ((Avalonia.Collections.DataGridCollectionView)partGrid.ItemsSource!).Groups!.OfType<Avalonia.Collections.DataGridCollectionViewGroup>().First();
            partGrid.ExpandRowGroup(model, false);
            // its engine types listed, the last one's partnumbers shown (T7Suite's manual had it so)
            partGrid.ExpandRowGroup(model.Items.OfType<Avalonia.Collections.DataGridCollectionViewGroup>().Last(), false);
            Alone(list, "partnumber-list" + suffix);

            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Alone(ShowSettings(vm, 0, s => s.Common.ProjectFolder = s.Common.ProjectFolder.Replace(profile, "~")), "settings-common" + suffix);
            var logging = ShowSettings(vm, 1);
            // the log folder in the home folder's settings, not the test's
            foreach (TextBox box in logging.GetVisualDescendants().OfType<TextBox>().Where(b => b.Text == Logging.Folder))
                box.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MattiasC", SettingsKey.Program).Replace(profile, "~");
            Alone(logging, "settings-logging" + suffix);

            Alone(new VinDecoderWindow { DataContext = new VinDecoderViewModel(vm.BinaryVin) }, "vin-decoder" + suffix);

            // SuiteMainView.BinaryCompare's report
            var lines = SuiteCompare.BinaryDiff(vm.Binary!.FileName, tuned);
            Assert.IsGreaterThanOrEqualTo(3, lines.Count);
            var text = new StringBuilder($"{Path.GetFileName(vm.Binary.FileName)} / {Path.GetFileName(tuned)}: {lines.Count} lines differ\n\n");
            foreach (var (mine, theirs) in lines) text.Append(mine).Append('\n').Append(theirs).Append("\n\n");
            _ = Dialogs.Text(window, "Binary compare", text.ToString());
            Alone(Shown(window), "binary-compare" + suffix);

            // the fuel maps and their axes, scrolled into sight
            var transfer = new TransferSelectionViewModel(SuiteCompare.TransferCandidates(vm.Binary),
                new HashSet<string> { "BFuelCal.AirXSP", "BFuelCal.Map", "BFuelCal.RpmYSP", "BFuelCal.StartMap" });
            var transferWindow = new TransferSelectionWindow { DataContext = transfer };
            transferWindow.Show();
            transferWindow.CaptureRenderedFrame();
            transferWindow.GetVisualDescendants().OfType<ListBox>().Single().ScrollIntoView(transfer.Items[transfer.Items.FindIndex(i => i.Name == "BFuelCal.Map") + 7]);
            Alone(transferWindow, "transfer-maps" + suffix);

            Alone(new SearchMapsWindow { DataContext = new SearchMapsViewModel { SearchForNumericValues = true, NumericValue = 125 } }, "search-maps" + suffix);
            Alone(new MyMapsWindow { DataContext = new MyMapsViewModel(vm.T7.MyMapsDefaults) }, "my-maps" + suffix);

            var catalog = DtcCatalog.Load();
            Alone(new FaultCodesWindow(vm, [.. new[] { "P0171", "P0300", "P0420" }.Select(c => new FaultCode(c, catalog[c].Description))]), "fault-codes" + suffix);

            Alone(new AutotuneSettingsWindow { DataContext = new AutotuneSettingsViewModel(vm.T7.Settings) }, "autotune-settings" + suffix);
            var bin = (T7.T7Binary)vm.Binary;
            Alone(new SidInfoWindow { DataContext = new SidInfoViewModel(bin, T7.SidInfo.Read(bin)!) }, "sid-information" + suffix);

            // a handful of proposals in the part load cells, as MainView's AcceptAutotune shows them
            double[] x = bin.GetXaxisValues("BFuelCal.Map").Select(v => (double)v).ToArray(), y = bin.GetYaxisValues("BFuelCal.Map").Select(v => (double)v).ToArray();
            var percent = new double[x.Length * y.Length];
            foreach (var (row, col, change) in new[] { (5, 4, 2.35), (5, 5, 3.1), (6, 4, 1.2), (6, 5, 4.75), (6, 6, 2.6), (7, 5, -1.5), (7, 6, -0.8), (8, 7, 1.85) })
                percent[row * x.Length + col] = change;
            Alone(new AutotuneAcceptWindow("BFuelCal.Map", percent, x, y), "autotune-accept" + suffix);
        }

        // the documents, maximized in the workspace
        private static async Task DocumentShots(MainViewModel vm, Window window, string suffix, string other, string log)
        {
            // the table's right-click menu over a block of cells, the pointer in its bottom right cell
            vm.OpenSymbolByName("IgnNormCal.Map");
            MaximizeDocuments(window);
            var grid = window.GetVisualDescendants().OfType<MapControls.MapViewer>().Single().Grid;
            Point at = grid.TranslatePoint(new Point(grid.Bounds.Width * 0.45, grid.Bounds.Height * 0.35), window)!.Value;
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            var map = grid.Map!;
            var (r0, c0) = map.Cell(grid.SelectedCells.Single());
            grid.Select(from r in Enumerable.Range(r0 - 2, 3) from c in Enumerable.Range(c0 - 3, 4) select map.Index(r, c));
            RightClick(window, at);
            Save(window, "mapviewer-menu" + suffix);
            CloseMenu(window);
            await CloseDocuments(vm);

            vm.BrowseAxesCommand.Execute(null);
            Maximized(window, "axis-browser" + suffix);
            await CloseDocuments(vm);

            await vm.CompareToFileAsync(other);
            Maximized(window, "compare-results" + suffix);
            await CloseDocuments(vm);

            // the caret on the ignition map, the status bar naming it; from further down, so its line scrolls to the top
            vm.ViewHexCommand.Execute(null);
            MaximizeDocuments(window);
            var caret = window.GetVisualDescendants().OfType<AvaloniaHex.HexEditor>().Single().Caret;
            ulong start = (ulong)vm.Binary!.Find("IgnNormCal.Map").Flash_start_address;
            caret.Location = new AvaloniaHex.Document.BitLocation(start + 0x800);
            window.CaptureRenderedFrame();
            caret.Location = new AvaloniaHex.Document.BitLocation(start);
            Maximized(window, "hex-view" + suffix);
            await CloseDocuments(vm);

            vm.T7.ShowAirmassResultCommand.Execute(null);
            MaximizeDocuments(window);
            window.GetVisualDescendants().OfType<AirmassResultView>().Single().GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 2;
            Maximized(window, "compressor-map" + suffix);
            await CloseDocuments(vm);

            vm.T7.ShowAfrTargetMapCommand.Execute(null);
            Maximized(window, "afr-target" + suffix);
            await CloseDocuments(vm);

            // no adapter: the panel opens without polling; one pass of values as the engine gives them
            vm.Settings.Adapter = "";
            await vm.ToggleRealtimePanelCommand.ExecuteAsync(null);
            var rt = vm.Realtime!;
            rt.IsNight = false;
            rt.Apply(new RealtimeSample(DateTime.Now,
            [
                ("ActualIn.n_Engine", 3450), ("In.v_Vehicle", 96), ("Out.X_AccPedal", 72), ("ActualIn.T_Engine", 89), ("ActualIn.T_AirInlet", 31),
                ("ECMStat.ST_ActiveAirDem", 10), ("Lambda.Status", 0), ("FCut.CutStatus", 0), ("IgnProt.fi_Offset", -1.5), ("m_Request", 860),
                ("Out.M_Engine", 295), ("In.p_AirInlet", 0.92), ("Out.PWM_BoostCntrl", 46), ("Out.fi_Ignition", 17.5), ("MAF.m_AirInlet", 842),
                ("BFuelProt.CurrentFuelCon", 18.4), ("Lambda.LambdaInt", 0.86),
            ], 25, 1));
            Maximized(window, "realtime-dashboard" + suffix);
            window.GetVisualDescendants().OfType<RealtimeView>().Single().FindControl<TabControl>("Tabs")!.SelectedIndex = 1;
            Maximized(window, "realtime-free-logging" + suffix);
            await vm.ToggleRealtimePanelCommand.ExecuteAsync(null);
            await CloseDocuments(vm);

            await vm.OpenLogAsync(log, _ => Task.FromResult<int?>(0));
            Maximized(window, "log-viewer" + suffix);
            await CloseDocuments(vm);
        }

        // two projects in a project folder of the test's, the open one with a few map saves and a note each; projectFolder: Settings' own, the
        // logbook shows it
        private static async Task ProjectShots(MainViewModel vm, Window window, string suffix, string file, string other, string folder, string projectFolder)
        {
            vm.Settings.ProjectFolder = folder;
            vm.Settings.RequestProjectNotes = true;
            Assert.IsTrue(await vm.OpenPlainFileAsync(other, true));
            await vm.CreateProjectAsync(vm.NewProjectProperties());
            Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));

            var shown = vm.NewProjectProperties();
            shown.BinaryFile = Home(shown.BinaryFile);
            Alone(new ProjectPropertiesWindow { DataContext = shown }, "project-create" + suffix);
            await vm.CreateProjectAsync(vm.NewProjectProperties());
            Assert.IsTrue(vm.IsProjectOpen);

            foreach (var (name, cells, step, note) in new[]
            {
                ("IgnNormCal.Map", new[] { 220, 221, 238, 239 }, 10, "1 degree more at full load"),
                ("BFuelCal.Map", new[] { 150, 151, 152 }, 4, "richer at high airmass"),
                ("IgnNormCal.Map", new[] { 222, 240 }, -5, "half a degree back, knock at 4500"),
            })
            {
                vm.AskText = _ => Task.FromResult<string?>(note);
                vm.OpenSymbolByName(name);
                var viewer = vm.Viewers.OfType<MapViewerViewModel>().Single(v => v.MapName == name);
                viewer.Map.Set([.. cells.Select(i => (i, viewer.Map[i] + step))]);
                await viewer.SaveCommand.ExecuteAsync(null);
            }
            await CloseDocuments(vm);
            Alone(new TransactionLogWindow { DataContext = new TransactionLogViewModel(vm) }, "transaction-log" + suffix);
            var logbook = new LogbookViewModel(vm.Project!);
            string home = projectFolder.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~");
            for (int i = 0; i < logbook.Lines.Length; i++) logbook.Lines[i] = logbook.Lines[i] with { Description = logbook.Lines[i].Description.Replace(folder, home) };
            Alone(new LogbookWindow { DataContext = logbook }, "project-logbook" + suffix);
            var projects = SuiteProject.List(folder);
            Assert.HasCount(2, projects);
            Alone(new ProjectSelectionWindow(projects), "project-open" + suffix);
        }
    }
}
