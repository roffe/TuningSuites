using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SuiteApp;
using T5App.ViewModels;
using T5App.Views;

namespace T5App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            SuiteStartup.Start(desktop, new T5MainWindowViewModel(), new MainWindow());
        base.OnFrameworkInitializationCompleted();
    }
}
