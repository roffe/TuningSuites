using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SuiteApp;
using SuiteApp.Services;
using TxSuite.ViewModels;
using TxSuite.Views;

namespace TxSuite;

/// <summary>T5Suite, T7Suite and T8Suite as one program: one main window, which opens a T5, T7 or T8 binary in its ECU's module.</summary>
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Settings → Logging before anything logs: the logs at their levels, the old ones gone
            Logging.Apply();
            Logging.DeleteOld();
            SuiteStartup.Start(desktop, new MainViewModel(), new MainWindow());
        }
        base.OnFrameworkInitializationCompleted();
    }
}
