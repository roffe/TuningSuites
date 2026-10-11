using System.IO;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp;
using SuiteApp.ViewModels;

namespace TxSuiteTest
{
    /// <summary>TxSuite headless: a binary of another ECU opens in that suite's window, which becomes the main one.</summary>
    [TestClass]
    public partial class SwitchTest
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<TxSuite.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

        private static HeadlessUnitTestSession? s_session;
        private static string s_dir = "";

        [ClassInitialize]
        public static void Start(TestContext _)
        {
            s_dir = Directory.CreateTempSubdirectory("txsuite").FullName;
            SettingsKey.BaseFolder = Path.Combine(s_dir, "settings");
            NLog.LogManager.SuspendLogging();
            s_session = HeadlessUnitTestSession.StartNew(typeof(SwitchTest));
        }

        [ClassCleanup]
        public static void Stop()
        {
            s_session?.Dispose();
            Directory.Delete(s_dir, true);
        }

        private static string Stock(string folder, string name)
        {
            string file = Path.Combine(s_dir, name);
            File.Copy(Path.Combine(Here(), "..", folder, name), file, true);
            return file;
        }

        [TestMethod]
        public void OpeningAnotherEcusBinarySwitchesSuite()
        {
            string t5 = Stock("T5Binaries", "4239273.bin");
            string t7 = Stock("T7Binaries", "5168646.bin");
            string t8 = Stock("T8Binaries", "55353231_FA56_C_FME2_37_FIEF_81c.bin");
            s_session!.Dispatch(async () =>
            {
                try
                {
                    var desktop = new ClassicDesktopStyleApplicationLifetime();
                    var vm = new T7App.ViewModels.T7MainWindowViewModel();
                    var window = new T7App.Views.MainWindow { Width = 1200, Height = 800 };
                    SuiteStartup.Attach(desktop, vm, window);
                    window.Show();
                    TxSuite.App.Combine(desktop);

                    Assert.IsTrue(await vm.OpenPlainFileAsync(t7, true));
                    Assert.AreSame(window, desktop.MainWindow);

                    // T8: its own window in place of T7's, with the file open
                    Assert.IsFalse(await vm.OpenPlainFileAsync(t8, true));
                    Assert.IsInstanceOfType<T8App.Views.MainWindow>(desktop.MainWindow);
                    var t8vm = (MainWindowViewModel)desktop.MainWindow.DataContext!;
                    Assert.AreEqual(t8, t8vm.Binary?.FileName);
                    Assert.IsFalse(window.IsVisible);

                    // and on to T5, then back to T7
                    Assert.IsFalse(await t8vm.OpenPlainFileAsync(t5, true));
                    var t5vm = (MainWindowViewModel)desktop.MainWindow!.DataContext!;
                    Assert.IsInstanceOfType<T5App.ViewModels.T5MainWindowViewModel>(t5vm);
                    Assert.AreEqual(t5, t5vm.Binary?.FileName);
                    Assert.IsFalse(await t5vm.OpenPlainFileAsync(t7, true));
                    Assert.IsInstanceOfType<T7App.ViewModels.T7MainWindowViewModel>(desktop.MainWindow!.DataContext);

                    // its own kind stays in the window
                    var t7vm = (MainWindowViewModel)desktop.MainWindow.DataContext!;
                    var t7window = desktop.MainWindow;
                    Assert.IsTrue(await t7vm.OpenPlainFileAsync(t7, true));
                    Assert.AreSame(t7window, desktop.MainWindow);
                    t7window.Close();
                }
                finally
                {
                    MainWindowViewModel.Handoff = null;
                    SuiteApp.Views.SuiteMainWindow.SettingsDialog = null;
                }
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
