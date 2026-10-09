using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
                var grid = window.FindControl<DataGrid>("SymbolGrid")!;
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
