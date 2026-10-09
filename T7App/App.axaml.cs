using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SuiteApp;
using SuiteApp.Services;
using T7App.ViewModels;
using T7App.Views;

namespace T7App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new T7MainWindowViewModel();
            Logging.ApplyCanLogging(vm.Settings.EnableCanLog);
            SuiteStartup.Start(desktop, vm, new MainWindow());
        }
        base.OnFrameworkInitializationCompleted();
    }
}
