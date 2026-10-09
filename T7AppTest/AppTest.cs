using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
                Assert.HasCount(1, vm.Recent);

                // the menus open (a bad style once bound Recent itself to the open-recent command)
                var file = window.FindControl<MenuItem>("FileMenu")!;
                var recent = window.FindControl<MenuItem>("RecentMenu")!;
                file.Open();
                recent.Open();
                var entry = (MenuItem)recent.ContainerFromIndex(0)!;
                Assert.AreEqual("5168646.bin", entry.Header);
                Assert.AreSame(vm.OpenRecentCommand, entry.Command);
                Assert.AreSame(vm.Recent[0], entry.CommandParameter);
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
