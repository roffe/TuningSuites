using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SuiteApp;
using T8App.ViewModels;
using T8App.Views;

namespace T8App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            SuiteStartup.Start(desktop, new T8MainWindowViewModel(), new MainWindow());
        base.OnFrameworkInitializationCompleted();
    }
}
