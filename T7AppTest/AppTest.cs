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
                MapViewerViewModel viewer = vm.Viewers[0];
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
                Assert.AreEqual(viewer.XAxisSymbol, vm.Viewers[1].MapName);

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
                MapViewerViewModel viewer = vm.Viewers.Single(v => v.MapName == "IgnNormCal.Map");
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
                MapViewerViewModel viewer = vm.Viewers[0];
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
