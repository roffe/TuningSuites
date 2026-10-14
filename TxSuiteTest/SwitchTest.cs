using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
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
using TxSuite.ViewModels;
using TxSuite.Views;

namespace TxSuiteTest
{
    /// <summary>TxSuite headless: one window and one view model for T5, T7 and T8 binaries, the open file's ECU's menus.</summary>
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

        private static (MainViewModel, MainWindow) NewWindow(IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel();
            var window = new MainWindow { Width = 1200, Height = 800 };
            SuiteStartup.Attach(desktop, vm, window);
            window.Show();
            return (vm, window);
        }

        // a main menu item by its caption (the first, where an ECU's own one shares it)
        private static MenuItem Item(Window window, string header) =>
            window.GetLogicalDescendants().OfType<MenuItem>().First(m => m.Header as string == header);

        [TestMethod]
        public void OpeningAnotherEcusBinarySwitchesModule()
        {
            string t5 = Stock("T5Binaries", "4239273.bin");
            string t7 = Stock("T7Binaries", "5168646.bin");
            string t8 = Stock("T8Binaries", "55353231_FA56_C_FME2_37_FIEF_81c.bin");
            s_session!.Dispatch(async () =>
            {
                try
                {
                    var (vm, window) = NewWindow(new ClassicDesktopStyleApplicationLifetime());
                    var view = window.View;
                    string? info = null;
                    vm.Info += t => info = t;

                    // no file: what every ECU has, and nothing connects (the file tells which ECU it is)
                    Assert.IsTrue(Item(window, "Lookup partnumber").IsVisible);
                    foreach (string own in new[] { "SID information", "PID editor", "Examine binary", "Export to S19", "User manual" })
                        Assert.IsFalse(Item(window, own).IsVisible, own);
                    await vm.ConnectDisconnectCommand.ExecuteAsync(null);
                    Assert.AreEqual(MainWindowViewModel.FileNeeded, info);
                    Assert.IsFalse(vm.IsConnected);
                    info = null;
                    await vm.ReadEcuAsync(Path.Combine(s_dir, "read.bin"));
                    Assert.AreEqual(MainWindowViewModel.FileNeeded, info);
                    // the messages' dialogs closed: they block the window's keys
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    foreach (Window dialog in window.OwnedWindows.ToList()) dialog.Close();

                    // T7: its own items, not T5's or T8's
                    Assert.IsTrue(await vm.OpenPlainFileAsync(t7, true));
                    Assert.AreSame(vm.T7, vm.Module);
                    Assert.IsTrue(vm.IsT7 && !vm.IsT5 && !vm.IsT8);
                    Assert.IsTrue(Item(window, "SID information").IsVisible);
                    Assert.IsTrue(Item(window, "Export to S19").IsVisible);
                    Assert.IsFalse(Item(window, "PID editor").IsVisible);
                    Assert.IsFalse(Item(window, "Examine binary").IsVisible);
                    var t7map = vm.OpenSymbolByName("IgnNormCal.Map")!;
                    Assert.AreEqual(1, vm.Viewers.Count);

                    // T8 in the same window and view model: the T7 map closed, T8's items in place of T7's
                    Assert.IsTrue(await vm.OpenPlainFileAsync(t8, true));
                    Assert.AreSame(vm.T8, vm.Module);
                    Assert.AreSame(view, window.View);
                    Assert.AreSame(vm, window.DataContext);
                    Assert.AreEqual(t8, vm.Binary?.FileName);
                    Assert.AreEqual(0, vm.Viewers.Count);
                    Assert.IsTrue(Item(window, "PID editor").IsVisible);
                    Assert.IsTrue(Item(window, "Export to S19").IsVisible);
                    Assert.IsFalse(Item(window, "SID information").IsVisible);
                    // the symbol list's columns are T8's: Bitmask too
                    Assert.IsTrue(vm.SymbolColumns.Any(c => c.Path == nameof(SymbolHelper.BitMask)));
                    // the keys work with nothing focused: Shift+F1 opens T8's realtime panel
                    window.Focus();
                    window.KeyPress(Key.F1, RawInputModifiers.Shift, PhysicalKey.F1, null);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Assert.IsNotNull(vm.Realtime);
                    Assert.IsNull(vm.T5Realtime);
                    await vm.ToggleRealtimePanelCommand.ExecuteAsync(null);

                    // TxSuite's title, About and skin
                    StringAssert.StartsWith(window.Title, "TxSuite v");
                    StringAssert.EndsWith(window.Title, "[ " + Path.GetFileName(t8) + " ]");
                    Assert.AreEqual("About TxSuite", Item(window, "About TxSuite").Header);
                    Assert.AreEqual("TxSuite v1.2.3", view.NewAboutWindow("1.2.3").GetLogicalDescendants().OfType<TextBlock>().First().Text);
                    Item(window, "Dark").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    using (var tx = SettingsKey.Open("TxSuite")) Assert.AreEqual("Dark", tx.GetValue("Skin"));

                    // T5: its columns (no type, no bit mask) and its realtime panel's autotune key
                    Assert.IsTrue(await vm.OpenPlainFileAsync(t5, true));
                    Assert.AreSame(vm.T5, vm.Module);
                    Assert.IsTrue(Item(window, "Examine binary").IsVisible);
                    Assert.IsFalse(vm.SymbolColumns.Any(c => c.Path is nameof(SymbolHelper.BitMask) or nameof(SymbolHelper.Symbol_type)));
                    await vm.ToggleRealtimePanelCommand.ExecuteAsync(null);
                    Assert.IsNotNull(vm.T5Realtime);
                    await vm.ToggleRealtimePanelCommand.ExecuteAsync(null);
                    // one Recent for every ECU
                    CollectionAssert.IsSubsetOf(new[] { t7, t8, t5 }, vm.Recent.Select(r => r.Path).ToList());

                    // an unsaved T5 map: Cancel keeps the T5 file and the map, No lets it go
                    var map = vm.OpenSymbolByName("Ign_map_0!")!;
                    map.Map.Set([(0, map.Map[0] + 1)]);
                    vm.AskYesNoCancel = _ => Task.FromResult<bool?>(null);
                    Assert.IsFalse(await vm.OpenPlainFileAsync(t7, true));
                    Assert.AreSame(vm.T5, vm.Module);
                    Assert.AreEqual(t5, vm.Binary?.FileName);
                    Assert.IsTrue(vm.Viewers.Contains(map));
                    vm.AskYesNoCancel = _ => Task.FromResult<bool?>(false);
                    Assert.IsTrue(await vm.OpenPlainFileAsync(t7, true));
                    Assert.AreSame(vm.T7, vm.Module);
                    Assert.AreEqual(0, vm.Viewers.Count);
                    Assert.AreEqual(Avalonia.Styling.ThemeVariant.Dark, Avalonia.Application.Current!.RequestedThemeVariant);

                    // a file no ECU takes closes the open one
                    string text = Path.Combine(s_dir, "notes.bin");
                    File.WriteAllText(text, "not a binary");
                    info = null;
                    Assert.IsFalse(await vm.OpenPlainFileAsync(text, true));
                    Assert.AreEqual(MainWindowViewModel.NotABinary, info);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    foreach (Window dialog in window.OwnedWindows.ToList()) dialog.Close();
                    Assert.IsNull(vm.Binary);
                    window.Close();
                }
                finally
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
                }
                return true;
            }, default).GetAwaiter().GetResult();
        }

        /// <summary>The ECU of the file opened last is the one a new start begins with (its My Maps, its S19 size).</summary>
        [TestMethod]
        public void StartsWithTheEcuOpenedLast()
        {
            string t8 = Stock("T8Binaries", "55353231_FA56_C_FME2_37_FIEF_81c.bin");
            s_session!.Dispatch(async () =>
            {
                var (vm, window) = NewWindow(new ClassicDesktopStyleApplicationLifetime());
                Assert.IsTrue(await vm.OpenPlainFileAsync(t8, true));
                window.Close();
                var again = new MainViewModel();
                Assert.AreSame(again.T8, again.Module);
                Assert.IsFalse(again.IsT8, "no file open yet");
                again.Shutdown();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
