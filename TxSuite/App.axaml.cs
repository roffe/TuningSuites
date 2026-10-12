using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
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
/// opens in that suite's view, which takes the place of the one before in the one window (MainWindowViewModel.Handoff).
/// </summary>
public partial class App : Application
{
    /// <summary>TxSuite's own settings: the suite used last.</summary>
    public const string SettingsName = "TxSuite";
    private const string SuiteKey = "Suite";

    private sealed record Suite(string Name, Func<string, bool> Opens, Type ViewModel, Func<MainWindowViewModel> NewViewModel, Func<SuiteMainView> NewView,
        Func<string, PartInfo?> Lookup, Func<PartNumberTab> PartNumbers);

    private static Suite Of<TVm, TView>(string name, Func<string, bool> opens, Func<string, PartInfo?> lookup, Func<PartNumberTab> partNumbers)
        where TVm : MainWindowViewModel, new() where TView : SuiteMainView, new() =>
        new(name, opens, typeof(TVm), () => new TVm(), () => new TView(), lookup, partNumbers);

    // by size first: T8Suite's own check tells the user about any file that isn't a T8 binary
    private static readonly Suite[] Suites =
    [
        Of<T5MainWindowViewModel, T5App.Views.MainView>("T5", Trionic5Tools.T5Binary.IsValidFile, T5MainWindowViewModel.PartLookup, T5App.Views.T5ToolWindows.PartNumberTab),
        Of<T7MainWindowViewModel, T7App.Views.MainView>("T7", T7.T7Binary.IsValidFile, T7.BinaryTools.LookupPartNumber, T7App.Views.MainView.PartNumberTab),
        Of<T8MainWindowViewModel, T8App.Views.MainView>("T8", p => File.Exists(p) && new FileInfo(p).Length == TrionicCANLib.Firmware.FileT8.Length
            && T8SuitePro.T8Binary.IsValidFile(p), T8MainWindowViewModel.PartLookup, T8App.Views.MainView.PartNumberTab),
    ];

    /// <summary>TxSuite to the suites: the title's name and version, its releases for the update check, its About.</summary>
    public static readonly CombinedProgram Program = new(
        SettingsName,
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "",
        typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0, 0),
        "Txsuite_v",
        // the three suites' thanks in one list
        "Dilemma, Actitis H., Steve Hayes, Hook, mackan, MrAze, Sandy_rus, T5_Germany, Seb, Tomili, sourcode, J.K Nilsson, G-ice, " +
        "General Failure, Danibjor, Johnc, tomas0student, Janus0070, Mattias Claesson, Roffe and...");

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // the suite of the file it's started with, else the one used last, else T7
            string? last;
            using (var settings = SettingsKey.Open(SettingsName)) last = settings.GetValue(SuiteKey) as string;
            Suite suite = (desktop.Args is [var arg, ..] ? SuiteFor(arg) : null) ?? Suites.FirstOrDefault(s => s.Name == last) ?? Suites[1];
            Combine(desktop);
            var vm = suite.NewViewModel();
            Logging.ApplyCanLogging(vm.Settings.EnableCanLog);
            SuiteStartup.Start(desktop, vm, NewWindow(suite.NewView()));
            Remember(suite);
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>TxSuite's window, the one the suites' views take turns in.</summary>
    public static SuiteWindow NewWindow(SuiteMainView view) =>
        // ponytail: T7Suite's icon until TxSuite has its own
        new(view) { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://T7Suite/T7Suite.ico"))) };

    /// <summary>
    /// The suites as one: another suite's file or project opens in that suite's view in the same window, and Settings shows every
    /// suite's settings (TxSettingsWindow).
    /// </summary>
    public static void Combine(IClassicDesktopStyleApplicationLifetime desktop)
    {
        MainWindowViewModel.Combined = Program;
        SeedRecent();
        MainWindowViewModel.Handoff = (from, path, project) => SwitchAsync(desktop, from, path, project);
        // Lookup partnumber finds every ECU's (no partnumber is in two lists); its list has a tab per ECU, the open binary's in front, T5's
        // with none open
        SuiteMainView.LookupAll = pn => Suites.Select(s => s.Lookup(pn)).FirstOrDefault(info => info != null);
        SuiteMainView.BrowseAll = (owner, vm) =>
            PartNumberListWindow.Show(owner, vm.Binary == null ? 0 : Array.FindIndex(Suites, s => s.ViewModel == vm.GetType()), [.. Suites.Select(s => s.PartNumbers())]);
        SuiteMainView.SettingsDialog = async owner =>
        {
            var settings = new TxSettingsViewModel((MainWindowViewModel)owner.DataContext!);
            if (!await new TxSettingsWindow { DataContext = settings }.ShowDialog<bool>((Window)TopLevel.GetTopLevel(owner)!)) return false;
            settings.Apply();
            return true;
        };
    }

    // the first time: Recent starts as the suites' own lists together
    private static void SeedRecent()
    {
        using var shared = SettingsKey.Open(SettingsName, "MRUList");
        if (shared.GetValueNames().Any()) return;
        int n = 0;
        foreach (string suite in new[] { Trionic5Tools.T5AppSettings.Suite, "T7SuitePro", "T8SuitePro" })
        {
            using var own = SettingsKey.Open(suite, "MRUList");
            foreach (string name in own.GetValueNames().OrderBy(v => int.TryParse(v, out int i) ? i : int.MaxValue))
                if (own.GetValue(name) is string path) shared.SetValue((n++).ToString(), path);
        }
    }

    private static Suite? SuiteFor(string path) => Suites.FirstOrDefault(s => s.Opens(path));

    private static void Remember(Suite suite)
    {
        using var settings = SettingsKey.Open(SettingsName);
        settings.SetValue(SuiteKey, suite.Name);
    }

    // the other suite's view in place of this one: unsaved maps asked about first, nothing while flashing
    private static async Task<bool> SwitchAsync(IClassicDesktopStyleApplicationLifetime desktop, MainWindowViewModel from, string path, string? project)
    {
        if (await Task.Run(() => SuiteFor(path)) is not { } suite || suite.ViewModel == from.GetType()
            || desktop.MainWindow is not SuiteWindow window || window.DataContext != from) return false;
        if (from.CloseBlocker is { } blocker)
        {
            from.ShowInfo(blocker);
            return true;
        }
        if (!await from.CloseMutatedViewersAsync()) return true;

        var vm = suite.NewViewModel();
        Logging.ApplyCanLogging(vm.Settings.EnableCanLog);
        window.Show(suite.NewView(), vm);
        SuiteStartup.Attach(desktop, vm, window);
        Remember(suite);
        if (project != null) await vm.OpenProjectAsync(project);
        else await vm.OpenPlainFileAsync(path, true);
        return true;
    }
}
