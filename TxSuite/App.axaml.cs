using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CommonSuite;
using SuiteApp;
using SuiteApp.Services;
using SuiteApp.ViewModels;
using SuiteApp.Views;
using T5App.ViewModels;
using T7App.ViewModels;
using T8App.ViewModels;

namespace TxSuite;

/// <summary>
/// T5Suite, T7Suite and T8Suite as one program: the suite of the open binary is the one in front. A file or project of another ECU
/// opens in that suite's main window, which takes the place of the one before (MainWindowViewModel.Handoff).
/// </summary>
public partial class App : Application
{
    /// <summary>TxSuite's own settings: the suite used last.</summary>
    public const string SettingsName = "TxSuite";
    private const string SuiteKey = "Suite";

    private sealed record Suite(string Name, Func<string, bool> Opens, Type ViewModel, Func<MainWindowViewModel> NewViewModel, Func<SuiteMainWindow> NewWindow);

    private static Suite Of<TVm, TWindow>(string name, Func<string, bool> opens) where TVm : MainWindowViewModel, new() where TWindow : SuiteMainWindow, new() =>
        new(name, opens, typeof(TVm), () => new TVm(), () => new TWindow());

    // by size first: T8Suite's own check tells the user about any file that isn't a T8 binary
    private static readonly Suite[] Suites =
    [
        Of<T5MainWindowViewModel, T5App.Views.MainWindow>("T5", Trionic5Tools.T5Binary.IsValidFile),
        Of<T7MainWindowViewModel, T7App.Views.MainWindow>("T7", T7.T7Binary.IsValidFile),
        Of<T8MainWindowViewModel, T8App.Views.MainWindow>("T8", p => File.Exists(p) && new FileInfo(p).Length == TrionicCANLib.Firmware.FileT8.Length
            && T8SuitePro.T8Binary.IsValidFile(p)),
    ];

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // the suite of the file it's started with, else the one used last, else T7
            string? last;
            using (var settings = SettingsKey.Open(SettingsName)) last = settings.GetValue(SuiteKey) as string;
            Suite suite = (desktop.Args is [var arg, ..] ? SuiteFor(arg) : null) ?? Suites.FirstOrDefault(s => s.Name == last) ?? Suites[1];
            var vm = suite.NewViewModel();
            Logging.ApplyCanLogging(vm.Settings.EnableCanLog);
            SuiteStartup.Start(desktop, vm, suite.NewWindow());
            Remember(suite);
            Combine(desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The suites as one: another suite's file or project opens in that suite's window, the main window from then on, and Settings
    /// shows every suite's settings (TxSettingsWindow).
    /// </summary>
    public static void Combine(IClassicDesktopStyleApplicationLifetime desktop)
    {
        MainWindowViewModel.Handoff = (from, path, project) => SwitchAsync(desktop, from, path, project);
        SuiteMainWindow.SettingsDialog = async owner =>
        {
            var settings = new TxSettingsViewModel((MainWindowViewModel)owner.DataContext!);
            if (!await new TxSettingsWindow { DataContext = settings }.ShowDialog<bool>(owner)) return false;
            settings.Apply();
            return true;
        };
    }

    private static Suite? SuiteFor(string path) => Suites.FirstOrDefault(s => s.Opens(path));

    private static void Remember(Suite suite)
    {
        using var settings = SettingsKey.Open(SettingsName);
        settings.SetValue(SuiteKey, suite.Name);
    }

    // the other suite's window in place of this one, where it was and as big: unsaved maps asked about first, nothing while flashing
    private static async Task<bool> SwitchAsync(IClassicDesktopStyleApplicationLifetime desktop, MainWindowViewModel from, string path, string? project)
    {
        if (await Task.Run(() => SuiteFor(path)) is not { } suite || suite.ViewModel == from.GetType()
            || desktop.MainWindow is not SuiteMainWindow old || old.DataContext != from) return false;
        if (from.CloseBlocker is { } blocker)
        {
            from.ShowInfo(blocker);
            return true;
        }
        if (!await from.CloseMutatedViewersAsync()) return true;

        var vm = suite.NewViewModel();
        var window = suite.NewWindow();
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = old.Position;
        window.Width = old.Width;
        window.Height = old.Height;
        window.WindowState = old.WindowState;
        Logging.ApplyCanLogging(vm.Settings.EnableCanLog);
        SuiteStartup.Attach(desktop, vm, window);
        window.Show();
        old.Close();
        Remember(suite);
        if (project != null) await vm.OpenProjectAsync(project);
        else await vm.OpenPlainFileAsync(path, true);
        return true;
    }
}
