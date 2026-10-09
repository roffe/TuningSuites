using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using T7App.Services;
using T7App.ViewModels;
using T7App.Views;
using TrionicCANLib.API;

namespace T7App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();
            SymbolNumberConverter.Hex = vm.Settings.ShowAddressesInHex;
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;

            // T7Core and TrionicCANLib ask from worker threads, the dialog runs on the UI thread meanwhile
            UserPrompt.YesNo = (text, caption) => Dialogs.Wait(() => Dialogs.YesNo(window, text, caption));
            UserPrompt.Notify = (text, caption) => Dialogs.Wait(async () => { await Dialogs.Info(window, text, caption); return true; });

            string[] args = desktop.Args ?? [];
            window.Opened += async (_, _) => await vm.StartupAsync(args);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
