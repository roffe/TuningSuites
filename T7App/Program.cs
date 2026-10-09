using System;
using Avalonia;

namespace T7App;

static class Program
{
    [STAThread]
    static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // also used by the Avalonia previewer
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace()
            // the window class defaults to the assembly name, and a launcher for the old T7Suite (or anything else) named
            // T7Suite.desktop then lends the taskbar its icon; this class is ours, so the window's own icon shows
            .With(new X11PlatformOptions { WmClass = "T7App" });
}
