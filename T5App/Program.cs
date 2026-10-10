using System;
using Avalonia;

namespace T5App;

static class Program
{
    [STAThread]
    static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // also used by the Avalonia previewer
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace()
            // our own window class, as T7App: a launcher named T5Suite.desktop would otherwise lend the taskbar its icon
            .With(new X11PlatformOptions { WmClass = "T5App" });
}
