using System.Globalization;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using NLog;
using SuiteApp.Services;
using SuiteApp.ViewModels;
using SuiteApp.Views;
using TrionicCANLib.API;

namespace SuiteApp;

/// <summary>A suite's start (frmMain_Load / frmMain_Shown): its main window, the library's questions as dialogs, then the file.</summary>
public static class SuiteStartup
{
    /// <summary>
    /// Negative numbers with a plain '-' in every locale, as the old suites showed them on Windows: ICU (Linux, macOS) gives
    /// Swedish, Norwegian and Finnish a typographic minus (U+2212). Called first in Main, so every thread formats with it.
    /// </summary>
    public static void UseHyphenMinus()
    {
        if (CultureInfo.CurrentCulture.NumberFormat.NegativeSign == "-") return;
        var culture = (CultureInfo)CultureInfo.CurrentCulture.Clone();
        culture.NumberFormat.NegativeSign = "-";
        CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = culture;
    }

    public static void Start(IClassicDesktopStyleApplicationLifetime desktop, MainWindowViewModel vm, SuiteMainWindow window)
    {
        SymbolNumberConverter.Hex = vm.Settings.ShowAddressesInHex;
        window.DataContext = vm;
        desktop.MainWindow = window;

        // the cores and TrionicCANLib ask from worker threads, the dialog runs on the UI thread meanwhile
        UserPrompt.YesNo = (text, caption) => Dialogs.Wait(() => Dialogs.YesNo(window, text, caption));
        UserPrompt.Notify = (text, caption) => Dialogs.Wait(async () => { await Dialogs.Info(window, text, caption); return true; });

        // WinForms showed an unexpected exception and carried on, Avalonia ends the app and every unsaved map with it
        Logger logger = LogManager.GetLogger(vm.GetType().FullName!);
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            logger.Error(e.Exception);
            vm.ShowInfo(e.Exception.Message);
            e.Handled = true;
        };

        string[] args = desktop.Args ?? [];
        window.Opened += async (_, _) =>
        {
            await vm.StartupAsync(args);
            await window.CheckForUpdatesAsync(true);
        };
    }
}
