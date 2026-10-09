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
                // T8Suite's rule: the ECU buttons only for symbols in SRAM; a closed bin's calibration lives in the flash alone
                Assert.IsNull(viewer.EcuReadCommand);
                SymbolHelper ramOnly = vm.Binary.Symbols.Cast<SymbolHelper>().First(sh => sh.Flash_start_address >= 0x100000 && sh.Length > 1);
                Assert.IsNotNull(MapViewerViewModel.Create(vm, vm.Binary, ramOnly, new byte[ramOnly.Length], sram: true)!.EcuReadCommand);
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
                var firmware = new FirmwareInfoWindow { DataContext = new FirmwareInfoViewModel(info) };
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
    
        [TestMethod]
        public void OfflineTuning()
        {
            string file = CopyOfStockBin("tune.BIN"), other = CopyOfStockBin("other.BIN");
            s_session!.Dispatch(async () =>
            {
                var vm = new T8MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();

                // compare with a copy whose ignition map differs
                T8Binary copy = T8Binary.Open(other, false);
                SymbolHelper ign = copy.Find("IgnAbsCal.fi_NormalMAP");
                byte[] data = copy.ReadSymbol(ign);
                data[1] ^= 1;
                copy.WriteSymbol(copy.FileAddress(ign), data);
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                await vm.CompareToFileAsync(other);
                var compare = (CompareResultsViewModel)vm.SelectedViewer!;
                Assert.AreEqual("Compare results: other.BIN", compare.Title);
                Assert.AreEqual("IgnAbsCal.fi_NormalMAP", compare.Rows.Cast<CompareRow>().Single().SymbolName);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(window, "compare");

                vm.SearchMaps(new MapSearchOptions(false, 0, true, "fi_NormalMAP", true, false, false, 0));
                Assert.AreEqual("Search results:  string fi_NormalMAP", vm.SelectedViewer!.Title);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(window, "search");

                // the Tuning menu with DynamicTuningMenu's captions for an old calibration, opening its maps
                var quick = window.FindControl<MenuItem>("QuickMapsMenu")!;
                // after the Tuning Wizard
                var airmass = (MenuItem)quick.Items[1]!;
                Assert.AreEqual("Airmass controller", airmass.Header);
                var manual = (MenuItem)airmass.Items[0]!;
                Assert.AreEqual("Max airmass map (manual)", manual.Header);
                manual.Command!.Execute(manual.CommandParameter);
                Assert.AreEqual("Symbol: BstKnkCal.MaxAirmass [tune.BIN]", vm.SelectedViewer!.Title);

                // My Maps: T8Suite's defaults for a new file, Add to MyMaps under "Directly added"
                Assert.AreEqual("BFuelCal.LambdaOneFacMap", vm.MyMapsToEdit()[0].Symbol);
                vm.AddToMyMaps(vm.Binary!.Find("IgnAbsCal.fi_NormalMAP"));
                var my = window.FindControl<MenuItem>("MyMapsMenu")!;
                var directly = my.Items.OfType<MenuItem>().Single(i => (string?)i.Header == "Directly added");
                // escaped: a menu header's single underscore marks the access key
                Assert.AreEqual("IgnAbsCal.fi__NormalMAP", ((MenuItem)directly.Items[0]!).Header);

                var settings = new SettingsViewModel(vm.Settings) { MapDetectionActive = true };
                var settingsWindow = new SettingsWindow { DataContext = settings };
                settingsWindow.Show();
                Save(settingsWindow, "settings");
                settingsWindow.Close();
                settings.Apply(vm.Settings);
                Assert.IsTrue(vm.Settings.MapDetectionActive);

                var lookup = new PartLookupViewModel(vm.LookupPartNumber, vm.PartDetails, vm.Caption) { PartNumber = "55353231_FA56_C_FME2_37_FIEF_81c" };
                lookup.Lookup();
                Assert.AreEqual("Saab93", lookup.Info!.CarModel);
                Assert.AreEqual("B207L", lookup.Info.EngineType);
                var unknown = new PartLookupViewModel(vm.LookupPartNumber, vm.PartDetails, vm.Caption) { PartNumber = "123" };
                unknown.Lookup();
                Assert.AreEqual("The entered partnumber was not recognized by T8Suite", unknown.Message);
                var lookupWindow = new SuiteApp.Views.PartLookupWindow { DataContext = lookup };
                lookupWindow.Show();
                Save(lookupWindow, "partlookup");
                lookupWindow.Close();

                // VIN and immobilizer code from the firmware dialog, then the checksum as on open
                var firmware = new FirmwareInfoViewModel(vm.FirmwareInfo()!);
                firmware.StartVinAndImmoEdit();
                firmware.ChassisId = "YS3FB45F431012345";
                var firmwareWindow = new FirmwareInfoWindow { DataContext = firmware };
                firmwareWindow.Show();
                Save(firmwareWindow, "firmware-edit");
                firmwareWindow.Close();
                await vm.ApplyFirmwareAsync(firmware.ToEdit());
                Assert.AreEqual("YS3FB45F431012345", vm.FirmwareInfo()!.ChassisId);
                Assert.AreEqual("Checksum: OK", vm.ChecksumText);

                var vin = new VinDecoderViewModel(vm.FirmwareInfo()!.ChassisId);
                Assert.AreEqual("Valid", vin.Decoded.Checksum);
                var vinWindow = new SuiteApp.Views.VinDecoderWindow { DataContext = vin };
                vinWindow.Show();
                Save(vinWindow, "vindecoder");
                vinWindow.Close();
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    
        [TestMethod]
        public void EcuWithoutAnAdapter()
        {
            string file = CopyOfStockBin("ecu.BIN");
            s_session!.Dispatch(async () =>
            {
                var vm = new T8MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));

                // no adapter in the settings: nothing goes on the wire
                vm.Settings.Adapter = "";
                Assert.IsFalse(await vm.EnsureConnectedAsync());
                Assert.IsFalse(vm.IsConnected);
                Assert.AreEqual("Connect ECU", vm.ConnectCaption);
                Assert.IsNull(await vm.ReadEcuInfoAsync());
                Assert.IsNull(await vm.ReadFaultCodesAsync());
                Assert.IsNull(vm.CloseBlocker);

                // Import SRAM file / Read from SRAM file: a 32 KB dump read at the symbol's SRAM address
                SymbolHelper live = vm.Binary!.Symbols.Cast<SymbolHelper>().First(sh => sh.Start_address >= 0x100000 && sh.Length >= 16);
                byte[] data = Enumerable.Range(1, live.Length).Select(i => (byte)i).ToArray();
                var ram = new byte[0x8000];
                data.CopyTo(ram, (int)(live.Start_address % ram.Length));
                string ramFile = Path.Combine(s_dir, "snap.RAM");
                File.WriteAllBytes(ramFile, ram);
                vm.ImportSramSnapshot(ramFile);
                Assert.AreEqual("SRAM: snap", vm.SramFileText);
                Assert.IsTrue(vm.HasSramFile);
                vm.OpenFromSramFile(live);
                var sram = (MapViewerViewModel)vm.SelectedViewer!;
                Assert.AreEqual($"SRAM Symbol: {live.SmartVarname} [snap.RAM]", sram.Title);
                CollectionAssert.AreEqual(data, sram.Map.ToBytes());

                var faults = new SuiteApp.Views.FaultCodesWindow(vm, [new FaultCode("P0335", "Crankshaft Position Sensor Circuit, Crank Time Based Circuit"), new FaultCode("U0100", "")]);
                faults.Show();
                Save(faults, "faultcodes");
                faults.Close();

                var info = new EcuInfoWindow([
                    new("ECU related data", "ECU description", "Trionic 8 P6.8", "GMPT 0100"), new("ECU related data", "Build date", "2003-07-09"),
                    new("Calibration data", "Software version", "FA56_C_FME2_37_FIEF_81c"), new("Calibration data", "Software IDs", "12345678", "87654321"),
                    new("Calibration data", "Speedlimit", "250 km/h"),
                ]);
                info.Show();
                Save(info, "ecuinfo");
                info.Close();
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    
        [TestMethod]
        public void RealtimePanelAndLogs()
        {
            string file = CopyOfStockBin("rt.BIN");
            string log = Path.Combine(s_dir, "rt-20261010-CanTraceExt.t8l");
            var t0 = new DateTime(2026, 10, 10, 12, 0, 0);
            File.WriteAllLines(log, Enumerable.Range(0, 50).Select(i =>
                RealtimeLog.Line(t0.AddMilliseconds(i * 100), [("ActualIn.n_Engine", 900 + i * 40), ("Out.M_EngTrqAct", i * 4)], false)));
            s_session!.Dispatch(async () =>
            {
                var vm = new T8MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));

                // the panel opens; without an adapter it doesn't poll
                vm.Settings.Adapter = "";
                await vm.ToggleRealtimePanelCommand.ExecuteAsync(null);
                RealtimeViewModel rt = vm.Realtime!;
                Assert.IsFalse(rt.IsRunning);
                Assert.AreEqual("ActualIn.U_Battery", rt.Rows[0].Name);
                Assert.IsFalse(rt.HasPerformanceMode);
                Assert.IsFalse(rt.CanAutotune);

                // a pass on screen: T8's names on the dashboard, T8Suite's status texts, the per-cylinder rows
                rt.Apply(new RealtimeSample(DateTime.Now,
                [
                    ("ActualIn.U_Battery", 13.8), ("ActualIn.n_Engine", 3000), ("Out.M_EngTrqAct", 250), ("IgnMastProt.fi_Offset", -1.5),
                    ("AirMassMast.m_Request", 900), ("Out.X_AccPos", 55), ("In.p_AirInlet", 0.9), ("Lambda.Status", 1), ("FCut.CutStatus", 0),
                    ("ECMStat.ST_ActiveAirDem", 50), ("Lambda.LambdaInt", 0.98), ("KnkCntCyl2", 7),
                ], 20, null));
                Assert.AreEqual(250, rt.Torque);
                Assert.AreEqual(-1.5, rt.IgnitionOffset);
                Assert.AreEqual(55, rt.Tps);
                Assert.AreEqual(CommonSuite.Realtime.Power(3000, 250), rt.Power);
                Assert.AreEqual("Closed loop not activated", rt.LambdaStatus);
                Assert.AreEqual("Knock airmass limit", rt.AirmassLimiter);
                Assert.IsTrue(rt.Rows.Single(r => r.Name == "KnkCntCyl2").Symbol.Derived);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(window, "realtime");

                // the day's .t8l in the viewer
                await vm.OpenLogAsync(log, _ => System.Threading.Tasks.Task.FromResult<int?>(0));
                var viewer = (LogViewerViewModel)vm.SelectedViewer!;
                Assert.AreEqual("CANBus logfile: rt-20261010-CanTraceExt.t8l", viewer.Title);
                CollectionAssert.AreEqual(new[] { "Rpm", "Out.M_EngTrqAct" }, viewer.Channels.Where(c => c.Symbol != "IMPORTANTLINE").Select(c => c.Name).ToArray());
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(window, "logviewer");
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
