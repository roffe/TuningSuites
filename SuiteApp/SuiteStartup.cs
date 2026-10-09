using Avalonia.Controls.ApplicationLifetimes;
using SuiteApp.Services;
using SuiteApp.ViewModels;
using SuiteApp.Views;
using TrionicCANLib.API;

namespace SuiteApp;

/// <summary>A suite's start (frmMain_Load / frmMain_Shown): its main window, the library's questions as dialogs, then the file.</summary>
public static class SuiteStartup
{
    public static void Start(IClassicDesktopStyleApplicationLifetime desktop, MainWindowViewModel vm, SuiteMainWindow window)
    {
        SymbolNumberConverter.Hex = vm.Settings.ShowAddressesInHex;
        window.DataContext = vm;
        desktop.MainWindow = window;

        // the cores and TrionicCANLib ask from worker threads, the dialog runs on the UI thread meanwhile
        UserPrompt.YesNo = (text, caption) => Dialogs.Wait(() => Dialogs.YesNo(window, text, caption));
        UserPrompt.Notify = (text, caption) => Dialogs.Wait(async () => { await Dialogs.Info(window, text, caption); return true; });

        string[] args = desktop.Args ?? [];
        window.Opened += async (_, _) =>
        {
            await vm.StartupAsync(args);
            await window.CheckForUpdatesAsync(true);
        };
    }
}
