using System;
using Avalonia;

namespace T7App;

static class Program
{
    [STAThread]
    static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // also used by the Avalonia previewer
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
