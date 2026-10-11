using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Input;
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
                    var window = TxSuite.App.NewWindow(new T7App.Views.MainView());
                    window.Width = 1200;
                    window.Height = 800;
                    SuiteStartup.Attach(desktop, vm, window);
                    window.Show();
                    TxSuite.App.Combine(desktop);

                    Assert.IsTrue(await vm.OpenPlainFileAsync(t7, true));

                    // T8: its view and view model in the same window, with the file open
                    var t7view = window.View;
                    Assert.IsFalse(await vm.OpenPlainFileAsync(t8, true));
                    Assert.AreSame(window, desktop.MainWindow);
                    Assert.IsTrue(window.IsVisible);
                    Assert.IsInstanceOfType<T8App.Views.MainView>(window.View);
                    var t8vm = (MainWindowViewModel)window.DataContext!;
                    Assert.AreSame(t8vm, window.View.DataContext);
                    Assert.AreEqual(t8, t8vm.Binary?.FileName);
                    // the T7 view let go of the window: its key bindings and view model stay its own
                    Assert.AreSame(vm, t7view.DataContext);
                    Assert.IsFalse(window.KeyBindings.Intersect(t7view.KeyBindings).Any());
                    // T8's keys work with nothing focused: Shift+F1 opens its realtime panel
                    window.Focus();
                    window.KeyPress(Key.F1, RawInputModifiers.Shift, PhysicalKey.F1, null);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Assert.IsNotNull(t8vm.Realtime);
                    await t8vm.ToggleRealtimePanelCommand.ExecuteAsync(null);

                    // TxSuite's title, About and skin whichever suite is in front
                    StringAssert.StartsWith(window.Title, "TxSuite v");
                    StringAssert.EndsWith(window.Title, "[ " + Path.GetFileName(t8) + " ]");
                    Assert.AreEqual("About TxSuite", window.View.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Name == "AboutMenu").Header);
                    Assert.AreEqual("TxSuite v1.2.3", window.View.NewAboutWindow("1.2.3").GetLogicalDescendants().OfType<TextBlock>().First().Text);
                    window.View.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Tag as string == "Dark").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    using (var tx = SettingsKey.Open("TxSuite")) Assert.AreEqual("Dark", tx.GetValue("Skin"));
                    using (var own = SettingsKey.Open("T8SuitePro")) Assert.IsNull(own.GetValue("Skin"));

                    // and on to T5, then back to T7
                    Assert.IsFalse(await t8vm.OpenPlainFileAsync(t5, true));
                    var t5vm = (MainWindowViewModel)window.DataContext!;
                    Assert.IsInstanceOfType<T5App.ViewModels.T5MainWindowViewModel>(t5vm);
                    Assert.AreEqual(t5, t5vm.Binary?.FileName);
                    // one Recent for all suites
                    CollectionAssert.IsSubsetOf(new[] { t7, t8, t5 }, t5vm.Recent.Select(r => r.Path).ToList());
                    Assert.IsFalse(await t5vm.OpenPlainFileAsync(t7, true));
                    Assert.IsInstanceOfType<T7App.ViewModels.T7MainWindowViewModel>(window.DataContext);
                    Assert.AreEqual(Avalonia.Styling.ThemeVariant.Dark, Avalonia.Application.Current!.RequestedThemeVariant);
                    Assert.AreSame(window, desktop.MainWindow);

                    // its own kind stays
                    var t7vm = (MainWindowViewModel)window.DataContext!;
                    var t7again = window.View;
                    Assert.IsTrue(await t7vm.OpenPlainFileAsync(t7, true));
                    Assert.AreSame(t7again, window.View);
                    window.Close();
                }
                finally
                {
                    MainWindowViewModel.Handoff = null;
                    MainWindowViewModel.Combined = null;
                    Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
                    SuiteApp.Views.SuiteMainView.SettingsDialog = null;
                }
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
