using Avalonia.LogicalTree;
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7App.ViewModels;
using T7App.Views;

namespace T7AppTest
{
    /// <summary>
    /// The real app in a headless session (Skia, so the map controls really render), on a copy of a stock bin and with
    /// settings in a temp folder. T7APP_DUMP=&lt;dir&gt; saves the rendered windows as PNG.
    /// </summary>
    [TestClass]
    public class AppTest
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<T7App.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

        private static HeadlessUnitTestSession? s_session;
        private static string s_dir = "";

        [ClassInitialize]
        public static void Start(TestContext _)
        {
            s_dir = Directory.CreateTempSubdirectory("t7app").FullName;
            SettingsKey.BaseFolder = Path.Combine(s_dir, "settings");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), Path.Combine(s_dir, "5168646.bin"));
            s_session = HeadlessUnitTestSession.StartNew(typeof(AppTest));
        }

        [ClassCleanup]
        public static void Stop()
        {
            s_session?.Dispose();
            Directory.Delete(s_dir, true);
        }

        private static void Save(Avalonia.Controls.Window w, string name)
        {
            WriteableBitmap frame = w.CaptureRenderedFrame()!;
            string? dir = Environment.GetEnvironmentVariable("T7APP_DUMP");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            frame.Save(Path.Combine(dir, name + ".png"), new PngBitmapEncoderOptions());
        }

        [TestMethod]
        public void EditSaveReadAndClose()
        {
            string file = Path.Combine(s_dir, "edit.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                string? info = null;
                vm.Info += t => info = t;
                Assert.IsTrue(await vm.OpenFileAsync(file, true));

                // Enter in the symbol list opens the selected map
                vm.SelectedSymbol = vm.Binary!.Find("IgnNormCal.Map");
                window.CaptureRenderedFrame();
                var grid = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<DataGrid>().First(g => g.Name == "SymbolGrid");
                grid.Focus();
                window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
                Assert.HasCount(1, vm.Viewers);
                MapViewerViewModel viewer = (MapViewerViewModel)vm.Viewers[0];
                Assert.IsFalse(viewer.IsReadOnly);

                // edit, save, the file has it and its checksum still verifies
                viewer.Map.Set([(0, 123)]);
                Assert.IsTrue(viewer.Map.Mutated);
                viewer.SaveCommand.Execute(null);
                Assert.IsFalse(viewer.Map.Mutated);
                var reopened = T7.T7Binary.Open(file, 0, false);
                Assert.AreEqual(123, MapControls.MapData.Decode(reopened.ReadSymbol(reopened.Find("IgnNormCal.Map")), true)[0]);
                await vm.VerifyChecksumCommand.ExecuteAsync(null);
                Assert.AreEqual("Checksums verified and all matched!", info);

                // read from file drops an unsaved change
                viewer.Map.Set([(0, 5)]);
                viewer.ReadCommand.Execute(null);
                Assert.AreEqual(123, viewer.Map[0]);
                Assert.IsFalse(viewer.Map.Mutated);

                // closing with changes: Cancel keeps it, No discards
                viewer.Map.Set([(0, 7)]);
                vm.AskYesNoCancel = _ => Task.FromResult<bool?>(null);
                Assert.IsFalse(await vm.CloseViewerAsync(viewer));
                Assert.HasCount(1, vm.Viewers);
                vm.AskYesNoCancel = _ => Task.FromResult<bool?>(false);

                // Edit x-axis opens the axis symbol
                viewer.EditAxisCommand.Execute(true);
                Assert.HasCount(2, vm.Viewers);
                Assert.AreEqual(viewer.XAxisSymbol, ((MapViewerViewModel)vm.Viewers[1]).MapName);

                // firmware information: edit, OK, the bin has it
                FirmwareInfoViewModel fw = vm.FirmwareInfo()!;
                bool limiter = fw.TorqueLimiters;
                fw.TorqueLimiters = !limiter;
                fw.ChassisID = "YS3EF48E";
                fw.FastThrottleResponse = true;
                fw.ExtraFastThrottleResponse = true;
                Assert.IsFalse(fw.FastThrottleResponse, "fast and extra fast exclude each other");
                var fwWindow = new FirmwareInfoWindow { DataContext = fw };
                fwWindow.Show();
                Save(fwWindow, "firmware-edit");
                fwWindow.Close();
                vm.ApplyFirmware(fw, _ => true);
                var after = T7.FirmwareInfo.Read(T7.T7Binary.Open(file, 0, false));
                Assert.AreEqual(!limiter, after.TorqueLimiters.Enabled);
                Assert.AreEqual("YS3EF48E", after.ChassisID.Trim());
                Assert.AreEqual(TrionicCANLib.Checksum.ChecksumResult.Ok, T7.T7Binary.Open(file, 0, false).VerifyChecksum());

                Assert.IsTrue(await vm.CloseViewerAsync(viewer));
                Assert.HasCount(1, vm.Viewers);
                Assert.AreEqual(123, MapControls.MapData.Decode(T7.T7Binary.Open(file, 0, false).ReadSymbol(reopened.Find("IgnNormCal.Map")), true)[0]);
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        [TestMethod]
        public void ProjectsLogAndRollBack()
        {
            string file = Path.Combine(s_dir, "proj-source.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new MainWindowViewModel();
                vm.Settings.ProjectFolder = Path.Combine(s_dir, "Projects");
                vm.Settings.RequestProjectNotes = true;
                vm.AskText = _ => Task.FromResult<string?>("leaner at idle");
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));

                ProjectPropertiesViewModel props = vm.NewProjectProperties();
                Assert.AreEqual("5168646 EA1WF0LC.47D", props.ProjectName);
                await vm.CreateProjectAsync(props);
                Assert.IsTrue(vm.IsProjectOpen);
                Assert.AreEqual("T7SuitePro [Project: 5168646 EA1WF0LC.47D]", vm.Title);
                Assert.AreEqual(Path.Combine(vm.Settings.ProjectFolder, "5168646 EA1WF0LC.47D", "proj-source.bin"), vm.Binary!.FileName);
                Assert.IsFalse(vm.CanRollBack);

                // a map save inside the project: transaction with the asked note, logbook line
                vm.OpenSymbolByName("IgnNormCal.Map");
                MapViewerViewModel viewer = vm.Viewers.OfType<MapViewerViewModel>().Single(v => v.MapName == "IgnNormCal.Map");
                int original = viewer.Map[0];
                viewer.Map.Set([(0, original + 10)]);
                await viewer.SaveCommand.ExecuteAsync(null);
                Assert.IsTrue(vm.CanRollBack);
                Assert.AreEqual("leaner at idle", vm.Project!.TransactionLog.TransCollection[0].Note);
                var log = new TransactionLogViewModel(vm);
                Assert.HasCount(1, log.Entries);
                Assert.AreEqual("IgnNormCal.Map", log.Entries[0].SymbolName);

                // roll back from the menu: file and open viewer show the old value
                vm.RollBackCommand.Execute(null);
                Assert.AreEqual(original, viewer.Map[0]);
                Assert.IsTrue(vm.CanRollForward);
                Assert.AreEqual(TrionicCANLib.Checksum.ChecksumResult.Ok, vm.Binary.VerifyChecksum());
                Assert.IsTrue(new LogbookViewModel(vm.Project).Lines.Any(l => l.Type == "A transaction was rolled back"));

                foreach (var (w, name) in new (Window, string)[]
                {
                    (new TransactionLogWindow { DataContext = new TransactionLogViewModel(vm) }, "transactionlog"),
                    (new LogbookWindow { DataContext = new LogbookViewModel(vm.Project) }, "logbook"),
                    (new ProjectPropertiesWindow { DataContext = ProjectPropertiesViewModel.From(vm.Project.Properties) }, "projectproperties"),
                    (new ProjectSelectionWindow(T7.T7Project.List(vm.Settings.ProjectFolder)), "projectselection"),
                    (new RebuildWindow { DataContext = new RebuildViewModel() }, "rebuild"),
                })
                {
                    w.Show();
                    Save(w, name);
                    w.Close();
                }

                // rebuild into the project file
                Assert.IsNull(vm.Rebuild(DateTime.Now.AddMinutes(1), true));

                // reopen from the list, then a plain file closes the project
                Assert.HasCount(1, T7.T7Project.List(vm.Settings.ProjectFolder));
                Assert.IsTrue(await vm.OpenProjectAsync("5168646 EA1WF0LC.47D"));
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                Assert.IsFalse(vm.IsProjectOpen);
                Assert.AreEqual(0, vm.Settings.LastOpenedType);
                return true;
            }, default).GetAwaiter().GetResult();
        }

        [TestMethod]
        public void CompareAndTransfer()
        {
            string a = Path.Combine(s_dir, "cmp-a.bin"), b = Path.Combine(s_dir, "cmp-b.bin"), target = Path.Combine(s_dir, "cmp-target.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), a, true);
            File.Copy(a, b, true);
            File.Copy(a, target, true);
            var binB = T7.T7Binary.Open(b, 0, false);
            var map = binB.Find("IgnNormCal.Map");
            byte[] changed = binB.ReadSymbol(map);
            changed[1] ^= 0x10;
            binB.WriteSymbol(binB.FileAddress(map), changed, false);
            s_session!.Dispatch(async () =>
            {
                var vm = new MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(a, true));
                await vm.CompareToFileAsync(b);
                var results = (CompareResultsViewModel)vm.SelectedViewer!;
                Assert.AreEqual("Compare results: cmp-b.bin", results.Title);
                var row = results.Rows.Cast<T7.CompareRow>().Single(r => !r.MissingInCompareFile && !r.MissingInOriFile);
                Assert.AreEqual("IgnNormCal.Map", row.SymbolName);
                Assert.AreEqual(1, row.Differences); // one changed byte is one changed value (T7Suite showed 0)
                Save(window, "compare");

                results.Open(row);
                var mine = vm.Viewers.OfType<MapViewerViewModel>().Single(v => v.FileName == vm.Binary!.FileName);
                var theirs = vm.Viewers.OfType<MapViewerViewModel>().Single(v => v.Title == "Symbol: IgnNormCal.Map [cmp-b.bin]");
                Assert.IsFalse(mine.IsReadOnly);
                Assert.IsTrue(theirs.IsReadOnly);
                Assert.AreNotEqual(mine.Map[0], theirs.Map[0]);

                results.ShowDifferenceMap(row);
                var diff = vm.Viewers.OfType<MapViewerViewModel>().Single(v => v.Title == "Symbol difference: IgnNormCal.Map [cmp-b.bin]");
                Assert.AreEqual(0x10, diff.Map[0]);
                Assert.AreEqual(0, diff.Map[1]);

                string csv = Path.Combine(s_dir, "diffexport.csv");
                results.ExportCsv(csv);
                StringAssert.Contains(File.ReadAllText(csv), "IgnNormCal.Map");

                // transfer the changed map from b (opened) into target
                Assert.IsTrue(await vm.OpenPlainFileAsync(b, true));
                var report = vm.TransferMaps(target, ["IgnNormCal.Map"]);
                CollectionAssert.Contains(report, "Transferred symbol IgnNormCal.Map successfully");
                CollectionAssert.Contains(vm.LastTransferSelection().ToList(), "IgnNormCal.Map");
                CollectionAssert.AreEqual(changed, T7.T7Binary.Open(target, 0, false).ReadSymbol(map));
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        [TestMethod]
        public void MenusSettingsAndImports()
        {
            string file = Path.Combine(s_dir, "menus.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));

                var quick = window.FindControl<MenuItem>("QuickMapsMenu")!;
                Assert.AreEqual("Fuel", ((MenuItem)quick.Items[0]!).Header);
                var ve = (MenuItem)((MenuItem)quick.Items[0]!).Items[0]!;
                Assert.AreEqual("VE map", ve.Header);
                ve.Command!.Execute(ve.CommandParameter);
                Assert.AreEqual("BFuelCal.Map", ((MapViewerViewModel)vm.SelectedViewer!).MapName);

                vm.AddToMyMaps(vm.Binary!.Find("IgnNormCal.Map"));
                var my = window.FindControl<MenuItem>("MyMapsMenu")!;
                var directly = my.Items.OfType<MenuItem>().Single(i => (string?)i.Header == "Directly added");
                Assert.AreEqual("IgnNormCal.Map", ((MenuItem)directly.Items[0]!).Header);

                // settings: hex off applies to the symbol list at once
                var settings = new SettingsViewModel(vm.Settings) { ShowAddressesInHex = false, SynchronizeMapviewers = false };
                var settingsWindow = new SettingsWindow { DataContext = settings };
                settingsWindow.Show();
                Save(settingsWindow, "settings");
                settingsWindow.Close();
                settings.Apply(vm.Settings);
                vm.SettingsChanged();
                Assert.IsFalse(T7App.Views.SymbolNumberConverter.Hex);
                Assert.IsNull(((MapViewerViewModel)vm.SelectedViewer!).SyncGroup);
                var settings2 = new SettingsViewModel(vm.Settings) { ShowAddressesInHex = true, SynchronizeMapviewers = true };
                settings2.Apply(vm.Settings);
                vm.SettingsChanged();

                var myMapsWindow = new MyMapsWindow { DataContext = new MyMapsViewModel(T7.MapMenus.LoadMyMaps(vm.MyMapsFile)) };
                myMapsWindow.Show();
                Save(myMapsWindow, "mymaps");
                myMapsWindow.Close();

                // search map content: a results tab, its row opens the map
                var searchWindow = new SearchMapsWindow { DataContext = new SearchMapsViewModel() };
                searchWindow.Show();
                searchWindow.Close();
                vm.SearchMaps(new T7.MapSearchOptions(false, 0, true, "IgnNormCal.Map", true, false, false, 0));
                var found = (SearchResultsViewModel)vm.SelectedViewer!;
                Assert.AreEqual("Search results: menus.bin", found.Title);
                Save(window, "search");
                found.Open(found.Results.First(r => r.Varname == "IgnNormCal.Map"));
                Assert.AreEqual("IgnNormCal.Map", ((MapViewerViewModel)vm.SelectedViewer!).MapName);

                // a CSV descriptor import through the view model refreshes the list
                string csv = Path.Combine(s_dir, "names.csv");
                var target = vm.Binary!.Find("IgnNormCal.Map");
                File.WriteAllText(csv, $"{target.Symbol_number};My.Ignition;;;\n");
                vm.ImportSymbols(bin => T7.SymbolFiles.ImportCsv(bin, csv));
                Assert.AreEqual("My.Ignition", target.Userdescription);
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        [TestMethod]
        public void RealtimePanelWithoutHardware()
        {
            string file = Path.Combine(s_dir, "realtime.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                var infos = new System.Collections.Generic.List<string>();
                vm.Info += infos.Add;
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                vm.OpenSymbolByName("IgnNormCal.Map");
                var ign = (MapViewerViewModel)vm.SelectedViewer!;

                // no adapter: the panel opens with the dashboard rows but doesn't poll
                vm.Settings.Adapter = "";
                await vm.ToggleRealtimePanelCommand.ExecuteAsync(null);
                var rt = vm.Realtime!;
                Assert.AreSame(rt, vm.SelectedViewer);
                Assert.IsFalse(rt.IsRunning);
                Assert.IsTrue(rt.Rows.Any(r => r.Name == "ActualIn.n_Engine"));

                // a pass on screen: dashboard, decoded statuses, per-cylinder rows and the engine's cell in the ignition map
                int[] air = vm.Binary!.GetXaxisValues("IgnNormCal.Map"), rpm = vm.Binary.GetYaxisValues("IgnNormCal.Map");
                rt.Apply(new T7.RealtimeSample(System.DateTime.Now,
                [
                    ("ActualIn.n_Engine", rpm[4]), ("MAF.m_AirInlet", air[7]), ("In.p_AirInlet", 0.85), ("Out.M_Engine", 300),
                    ("IgnProt.fi_Offset", -2.5), ("Lambda.Status", 0), ("FCut.CutStatus", 0), ("ECMStat.ST_ActiveAirDem", 10),
                    ("Lambda.LambdaInt", 0.85), ("KnockCyl1", 3),
                ], 25, 1));
                Assert.AreEqual(0.85, rt.Boost);
                Assert.AreEqual(T7.Realtime.Power(rpm[4], 300), rt.Power);
                Assert.AreEqual("Closed loop activated", rt.LambdaStatus);
                Assert.AreEqual("PedalMap", rt.AirmassLimiter);
                Assert.AreEqual((0.85 * 14.7).ToString("F1"), rt.AfrText);
                Assert.IsTrue(rt.IsNormal);
                Assert.AreEqual(3, rt.Rows.Single(r => r.Name == "KnockCyl1").Value);
                Assert.AreEqual(new Avalonia.PixelPoint(7, 4), ign.LiveCell);

                // the wideband on the ECU's AD scanner: AFR on the display, into the feedback map and its open viewer
                vm.Settings.UseWidebandLambda = true;
                vm.Settings.WideBandSymbol = "DisplProt.AD_Scanner";
                vm.Settings.AutoCreateAFRMaps = true;
                vm.ShowAfrFeedbackMapCommand.Execute(null);
                var feedback = (MapViewerViewModel)vm.SelectedViewer!;
                Assert.AreEqual("Symbol: FeedbackAFR [realtime.bin]", feedback.Title);
                Assert.IsNull(feedback.EcuReadCommand);
                int[] fuelRpm = vm.Binary.GetYaxisValues("BFuelCal.Map"), fuelAir = vm.Binary.GetXaxisValues("BFuelCal.Map");
                rt.Apply(new T7.RealtimeSample(System.DateTime.Now,
                    [("ActualIn.n_Engine", fuelRpm[6]), ("MAF.m_AirInlet", fuelAir[3]), ("FCut.CutStatus", 0), ("DisplProt.AD_Scanner", 1023)], 25, null));
                Assert.AreEqual("22.3", rt.AfrText);
                int cell = 6 * T7.AfrFeedback.Columns + 3;
                Assert.AreEqual(223, feedback.Map[cell]);
                vm.ImportAfrFeedbackCommand.Execute(null);
                Assert.AreEqual(0, feedback.Map[cell]);
                Assert.AreEqual(TrionicCANLib.Checksum.ChecksumResult.Ok, T7.T7Binary.OpenRaw(file).VerifyChecksum());
                vm.Settings.UseWidebandLambda = false;
                vm.SelectedViewer = rt;

                // add to realtime list goes into the open panel and rtsymbols.txt
                vm.AddToRealtime(vm.Binary.Find("Out.X_AccPedal")!);
                vm.AddToRealtime(vm.Binary.Find("BFuelCal.Map")!);
                Assert.IsTrue(rt.Rows.Single(r => r.Name == "BFuelCal.Map").Symbol.UserDefined);
                Assert.IsTrue(File.ReadAllText(rt.LayoutFile).Contains("BFuelCal.Map|"));

                Save(window, "realtime-dashboard");
                rt.IsNight = true;
                Save(window, "realtime-night");

                // closing the tab stops it; the user rows come back with the next panel
                await vm.ToggleRealtimePanelCommand.ExecuteAsync(null);
                Assert.IsNull(vm.Realtime);
                await vm.ToggleRealtimePanelCommand.ExecuteAsync(null);
                Assert.IsTrue(vm.Realtime!.Rows.Any(r => r.Name == "BFuelCal.Map"));
                Assert.IsTrue(vm.Realtime.IsNight);
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        [TestMethod]
        public void LogViewerAndExports()
        {
            string log = Path.Combine(s_dir, "drive-20261009-CanTraceExt.t7l");
            var t0 = new System.DateTime(2026, 10, 9, 12, 0, 0);
            var lines = new System.Collections.Generic.List<string> { T7.T7Log.Line(t0.AddMinutes(-5), [("ActualIn.n_Engine", 800)], false) };
            for (int i = 0; i < 600; i++)
            {
                double rpm = 900 + 2500 * System.Math.Sin(i / 60.0) * System.Math.Sin(i / 60.0);
                lines.Add(T7.T7Log.Line(t0.AddMilliseconds(i * 100), [("ActualIn.n_Engine", rpm), ("In.p_AirInlet", rpm / 4000 - 0.3), ("Out.fi_Ignition", 30 - rpm / 200)], i == 300));
            }
            File.WriteAllLines(log, lines);
            s_session!.Dispatch(async () =>
            {
                var vm = new MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                System.Collections.Generic.IReadOnlyList<string>? offered = null;
                await vm.OpenLogAsync(log, sections =>
                {
                    offered = sections;
                    return System.Threading.Tasks.Task.FromResult<int?>(1);
                });
                Assert.HasCount(2, offered!);
                var viewer = (LogViewerViewModel)vm.SelectedViewer!;
                Assert.AreEqual("CANBus logfile: drive-20261009-CanTraceExt.t7l", viewer.Title);
                Assert.AreEqual("Rpm", viewer.Channels[0].Name);
                Assert.HasCount(600, viewer.Channels[0].Time);
                Save(window, "logviewer");

                var selection = vm.LogSelection(log);
                selection.Symbols.First(c => c.Name == "Out.fi_Ignition").Selected = false;
                vm.ExportLogCsv(log, selection);
                Assert.AreEqual("Time,ActualIn.n_Engine,IMPORTANTLINE,In.p_AirInlet", File.ReadLines(Path.ChangeExtension(log, ".csv")).First());

                // a matrix of ignition over rpm and boost from the same log
                string bin = Path.Combine(s_dir, "matrix.bin");
                File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), bin, true);
                Assert.IsTrue(await vm.OpenPlainFileAsync(bin, true));
                var (matrixLines, matrix) = vm.MatrixSelection(log);
                matrix.X = "ActualIn.n_Engine";
                matrix.Y = "In.p_AirInlet";
                matrix.Z = "Out.fi_Ignition";
                vm.ShowMatrix(matrixLines, matrix);
                var mv = (MapViewerViewModel)vm.SelectedViewer!;
                Assert.AreEqual("Matrix [ActualIn.n_Engine : In.p_AirInlet : Out.fi_Ignition] (Mean values)", mv.Title);
                Assert.IsTrue(mv.IsReadOnly);
                Assert.AreEqual("ActualIn.n_Engine", vm.Settings.LastXAxisFromMatrix);
                Save(window, "matrix");

                var filters = new LogFiltersWindow { DataContext = new LogFiltersViewModel(vm.LoadLogFilters(), ["ActualIn.n_Engine"]) };
                filters.Show();
                Save(filters, "logfilters");
                filters.Close();
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        [TestMethod]
        public void WorkspaceWindowsAndClosing()
        {
            string file = Path.Combine(s_dir, "workspace.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                vm.OpenSymbolByName("IgnNormCal.Map");
                vm.OpenSymbolByName("BFuelCal.Map");
                window.CaptureRenderedFrame();
                // T7Suite's ribbon order, with ECU where its Programmer page was
                var menu = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Menu>().First();
                CollectionAssert.AreEqual(new[] { "_File", "_Actions", "_Tuning", "M_y Maps", "_Realtime", "E_CU", "_Skin", "_Help" },
                    menu.Items.OfType<MenuItem>().Select(m => m.Header as string).ToArray());

                // dark theme: the coloured symbol names keep black text
                Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
                vm.SearchText = "MAFCal";
                Save(window, "symbols-dark");
                Avalonia.Application.Current.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
                vm.SearchText = "";
                var docs = window.FindControl<Dock.Avalonia.Controls.DockControl>("Workspace")!;
                var dock = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Dock.Avalonia.Controls.DocumentDockControl>().First().DataContext as Dock.Model.Controls.IDocumentDock;
                Assert.HasCount(2, dock!.VisibleDockables!);
                var ign = (MapViewerViewModel)vm.Viewers[0];

                // selecting in the view model brings the window to the front, and the other way round
                vm.SelectedViewer = ign;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.AreSame(ign, dock.ActiveDockable!.Context);
                // inner windows only: no floating out of the main window, nothing dropped onto the docks
                Assert.IsTrue(dock.VisibleDockables!.All(d => !d.CanFloat && !d.CanDrop));
                Assert.IsFalse(dock.CanDrop);
                docs.Factory!.SetActiveDockable(dock.VisibleDockables!.First(d => d.Context != ign));
                Assert.AreNotSame(ign, vm.SelectedViewer);

                // the whole title bar moves the window (the title text isn't Dock's drag-to-dock handle any more)
                var mdiWindows = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Dock.Avalonia.Controls.MdiDocumentWindow>().ToList();
                Assert.IsTrue(mdiWindows.SelectMany(w => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w)).OfType<Control>()
                    .Where(c => c.Name == "PART_DragHandle").All(c => !c.IsHitTestVisible));

                // a title bar dragged out of the main window floats the document; Dock back returns it
                var fuel = (MapViewerViewModel)vm.Viewers[1];
                var fuelWindow = mdiWindows.First(w => (w.DataContext as Dock.Model.Core.IDockable)?.Context == fuel);
                var header = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(fuelWindow).OfType<Control>().First(c => c.Name == "PART_Header");
                var titlePoint = header.TranslatePoint(new Point(30, header.Bounds.Height / 2), window)!.Value;
                window.MouseDown(titlePoint, MouseButton.Left, RawInputModifiers.None);
                window.MouseMove(new Point(-40, 20), RawInputModifiers.LeftMouseButton);
                window.MouseUp(new Point(-40, 20), MouseButton.Left, RawInputModifiers.None);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.IsTrue(fuel.IsFloating);
                Assert.DoesNotContain(fuel, vm.DockedViewers);
                Assert.Contains(fuel, vm.Viewers);
                var floating = window.OwnedWindows.OfType<FloatingDocumentWindow>().Single();
                Save(floating, "floating");
                vm.DockAllCommand.Execute(null);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.IsFalse(fuel.IsFloating);
                Assert.Contains(fuel, vm.DockedViewers);
                Assert.IsEmpty(window.OwnedWindows);

                // the window's close button asks about unsaved changes: Cancel keeps it, No closes it
                ign.Map.Set([(0, ign.Map[0] + 1)]);
                bool? answer = null;
                int asked = 0;
                vm.AskYesNoCancel = _ => { asked++; return System.Threading.Tasks.Task.FromResult(answer); };
                var ignDock = dock.VisibleDockables!.First(d => d.Context == ign);
                docs.Factory.CloseDockable(ignDock);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(1, asked);
                Assert.Contains(ign, vm.Viewers);
                answer = false;
                docs.Factory.CloseDockable(ignDock);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.DoesNotContain(ign, vm.Viewers);
                Assert.HasCount(1, dock.VisibleDockables!);
                Save(window, "workspace");

                // the viewer's buttons under the graphs, in order, and Close closes it
                window.CaptureRenderedFrame();
                var viewer = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<MapControls.MapViewer>().Single();
                string[] buttons = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(viewer).OfType<Button>()
                    .Where(b => b.IsVisible && b.Content is string t && (t.Contains("ECU") || t.Contains("file") || t == "Close")).Select(b => (string)b.Content!).ToArray();
                CollectionAssert.AreEqual(new[] { "Read from ECU", "Save to ECU", "Read from file", "Save to file", "Close" }, buttons);
                var close = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(viewer).OfType<Button>().Single(b => b.Content is "Close");
                Assert.IsTrue(close.Bounds.Bottom <= viewer.Bounds.Height);
                close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.IsEmpty(vm.Viewers);
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        [TestMethod]
        public void AirmassResultViewer()
        {
            string file = Path.Combine(s_dir, "airmass.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1600, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                vm.ShowAirmassResultCommand.Execute(null);
                var am = (AirmassResultViewModel)vm.SelectedViewer!;
                Assert.AreEqual("Airmass result viewer: airmass.bin", am.Title);
                int wot = am.Result!.Pedal.Length - 1, c2400 = System.Array.IndexOf(am.Result.Rpm, 2400);
                Assert.AreEqual("759", am.Texts![wot, c2400]);
                am.DisplayMode = 1;
                Assert.AreEqual("245", am.Texts![wot, c2400]);
                Assert.AreEqual(4, am.Series.Count);                           // power, torque, injector DC, lambda
                Assert.AreEqual(3, am.CompressorPoints!.Length);
                Save(window, "airmass-table");
                var tabs = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<AirmassResultView>().Single()
                    .GetLogicalDescendants().OfType<TabControl>().First();
                tabs.SelectedIndex = 1;
                Save(window, "airmass-dyno");
                tabs.SelectedIndex = 2;
                Save(window, "airmass-compressor");
                tabs.SelectedIndex = 0;

                // a legend entry opens its map
                am.OpenLimiterCommand.Execute(T7.AirmassLimitType.TorqueLimiterEngine);
                Assert.AreEqual("TorqueCal.M_EngMaxTab", ((MapViewerViewModel)vm.SelectedViewer!).MapName);
                vm.SelectedViewer = am;
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        [TestMethod]
        public void TuningPackagesImportAndEdit()
        {
            string file = Path.Combine(s_dir, "package.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                var ign = vm.Binary!.Find("IgnNormCal.Map")!;
                vm.OpenSymbolByName("IgnNormCal.Map");
                var viewer = (MapViewerViewModel)vm.SelectedViewer!;

                // the editor: a symbol in, its viewer saves into the row, saved as a package
                vm.EditTuningPackageCommand.Execute(null);
                var editor = (TuningPackageEditorViewModel)vm.SelectedViewer!;
                editor.Add([ign]);
                Assert.AreEqual(ign.Length, editor.Rows.Single().Length);
                editor.OpenRow(editor.Rows[0]);
                var pkgViewer = vm.Viewers.OfType<MapViewerViewModel>().Single(v => v.Title.StartsWith("Tuning package symbol: IgnNormCal.Map"));
                Assert.IsNull(pkgViewer.EcuReadCommand);
                pkgViewer.Map.Set([(0, pkgViewer.Map[0] + 5)]);
                await pkgViewer.SaveCommand.ExecuteAsync(null);
                string pkg = Path.Combine(s_dir, "edited.t7p");
                editor.Save(pkg);
                Assert.AreNotEqual(viewer.Map[0], pkgViewer.Map[0]);

                // importing it changes the bin and the open viewer
                var results = vm.ImportTuningPackage(pkg)!;
                Assert.IsTrue(results.Single().Success);
                Assert.AreEqual(pkgViewer.Map[0], viewer.Map[0]);
                var dialog = new ImportResultsWindow(results);
                dialog.Show();
                Save(dialog, "importresults");
                dialog.Close();
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        [TestMethod]
        public void SidInformation()
        {
            string file = Path.Combine(s_dir, "sid.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new MainWindowViewModel();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                var sid = new SidInfoViewModel(vm.Binary!, T7.SidInfo.Read(vm.Binary!)!);
                Assert.IsFalse(sid.Rows[0].CanEdit);
                var choice = vm.Binary!.FindAny("In.v_Vehicle")!;
                sid.Rows[2].T7Symbol = "In.v_Vehicle";
                Assert.AreEqual(choice.Flash_start_address.ToString("X6"), sid.Rows[2].AddressText);
                sid.Rows[2].Symbol = "Toolong";
                Assert.AreNotEqual("Toolong", sid.Rows[2].Symbol);
                var window = new SidInfoWindow { DataContext = sid };
                window.Show();
                Save(window, "sidinfo");
                window.Close();
                sid.Save(false);
                Assert.AreEqual("In.v_Vehicle", T7.SidInfo.Read(T7.T7Binary.Open(file, 0, false))![2].FoundT7Symbol);
                return true;
            }, default).GetAwaiter().GetResult();
        }

        [TestMethod]
        public void EcuWithoutHardware()
        {
            string file = Path.Combine(s_dir, "ecu.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                var infos = new System.Collections.Generic.List<string>();
                vm.Info += infos.Add;
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));

                // no adapter configured: T7Suite's message, not connected
                vm.Settings.Adapter = "";
                Assert.IsFalse(await vm.EnsureConnectedAsync());
                CollectionAssert.Contains(infos, "Check settings, no CAN adapter has been selected!");
                Assert.IsFalse(vm.IsConnected);
                Assert.AreEqual("Connect ECU", vm.ConnectCaption);

                // an SRAM snapshot: the symbol's bytes at its SRAM address
                var sh = vm.Binary!.Find("IgnNormCal.Map");
                var ram = new byte[0x10000];
                int start = (int)(sh.Start_address & 0xFFFF);
                for (int i = 0; i < sh.Length; i++) ram[(start + i) % ram.Length] = (byte)(i % 7);
                string ramFile = Path.Combine(s_dir, "snap.RAM");
                File.WriteAllBytes(ramFile, ram);
                vm.ImportSramSnapshot(ramFile);
                Assert.AreEqual("SRAM: snap", vm.SramFileText);
                vm.OpenFromSramFile(sh);
                var viewer = (MapViewerViewModel)vm.SelectedViewer!;
                Assert.AreEqual("SRAM Symbol: IgnNormCal.Map [snap.RAM]", viewer.Title);
                Assert.IsTrue(viewer.OnlineMode);
                Assert.IsFalse(viewer.CanSaveToFile);
                CollectionAssert.AreEqual(Enumerable.Range(0, sh.Length).Select(i => (byte)(i % 7)).ToArray(), viewer.Map.ToBytes());
                Save(window, "sram-viewer");

                // compare snapshots: one row, both snapshots' viewers
                ram[start]++;
                string ramFile2 = Path.Combine(s_dir, "snap2.RAM");
                File.WriteAllBytes(ramFile2, ram);
                await vm.CompareSramAsync(ramFile, ramFile2);
                var results = (CompareResultsViewModel)vm.SelectedViewer!;
                Assert.AreEqual("SRAM compare results: snap.RAM snap2.RAM", results.Title);
                var row = results.Rows.SourceCollection.Cast<T7.CompareRow>().Single(r => r.SymbolName == "IgnNormCal.Map");
                results.Open(row);
                Assert.AreEqual("SRAM Symbol: IgnNormCal.Map [snap2.RAM]", vm.SelectedViewer!.Title);
                results.ShowDifferenceMap(row);
                Assert.AreEqual("SRAM symbol difference: IgnNormCal.Map [snap.RAM vs snap2.RAM]", vm.SelectedViewer!.Title);

                StringAssert.StartsWith(Path.GetFileName(vm.SnapshotFileName()), "SRAM");
                Assert.AreEqual(Path.GetDirectoryName(file), Path.GetDirectoryName(vm.SnapshotFileName()));

                var faults = new FaultCodesWindow(vm, [new FaultCode("P0300", "Random misfire"), new FaultCode("P1A0F", "")]);
                faults.Show();
                Save(faults, "faultcodes");
                faults.Close();
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        [TestMethod]
        public void OpenBinaryAndMap()
        {
            s_session!.Dispatch(async () =>
            {
                var vm = new MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();

                Assert.IsFalse(await vm.OpenFileAsync(Path.Combine(Here(), "AppTest.cs"), false));
                Assert.IsTrue(await vm.OpenFileAsync(Path.Combine(s_dir, "5168646.bin"), true));
                Assert.AreEqual("Normal binary", vm.OpenClosedText);
                Assert.IsGreaterThan(3000, vm.Symbols!.Count);
                int recentIndex = vm.Recent.ToList().FindIndex(r => r.Name == "5168646.bin");
                Assert.IsGreaterThanOrEqualTo(0, recentIndex);

                // the menus open (a bad style once bound Recent itself to the open-recent command)
                var file = window.FindControl<MenuItem>("FileMenu")!;
                var recent = window.FindControl<MenuItem>("RecentMenu")!;
                file.Open();
                recent.Open();
                var entry = (MenuItem)recent.ContainerFromIndex(recentIndex)!;
                Assert.AreEqual("5168646.bin", entry.Header);
                Assert.AreSame(vm.OpenRecentCommand, entry.Command);
                Assert.AreSame(vm.Recent[recentIndex], entry.CommandParameter);
                recent.Close();
                file.Close();

                vm.SearchText = "IgnNormCal.Map";
                SymbolHelper map = vm.Symbols.Cast<SymbolHelper>().First(s => s.Varname == "IgnNormCal.Map");
                vm.SelectedSymbol = map;
                vm.OpenSymbolCommand.Execute(map);
                vm.OpenSymbolCommand.Execute(map); // the second open focuses the first viewer
                Assert.HasCount(1, vm.Viewers);
                MapViewerViewModel viewer = (MapViewerViewModel)vm.Viewers[0];
                Assert.AreEqual("Symbol: IgnNormCal.Map [5168646.bin]", viewer.Title);
                Assert.AreEqual(18, viewer.Map.Cols);
                Assert.IsNotNull(viewer.Map.OpenLoop);
                Save(window, "main");

                FirmwareInfoViewModel fw = vm.FirmwareInfo()!;
                Assert.AreEqual("5168646", fw.Info.PartNumber.Trim());
                var fwWindow = new FirmwareInfoWindow { DataContext = fw };
                fwWindow.Show();
                Save(fwWindow, "firmware");
                fwWindow.Close();
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
