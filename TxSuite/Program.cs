using System;
using Avalonia;

namespace TxSuite;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        SuiteApp.SuiteStartup.UseHyphenMinus();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // also used by the Avalonia previewer
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace()
            // our own window class, as T7App's: a launcher named after the program would lend the taskbar its icon
            .With(new X11PlatformOptions { WmClass = "TxSuite" });
}
