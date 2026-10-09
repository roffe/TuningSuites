using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MapControlsTest
{
    public class TestApp : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<TestApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    /// <summary>
    /// One headless Avalonia session (with Skia, so custom draws really render) for all tests.
    /// MAPCONTROLS_DUMP=&lt;dir&gt; saves every rendered frame there as PNG to look at.
    /// </summary>
    [TestClass]
    public static class Headless
    {
        private static HeadlessUnitTestSession? s_session;

        [AssemblyInitialize]
        public static void Start(TestContext _) => s_session = HeadlessUnitTestSession.StartNew(typeof(TestApp));

        [AssemblyCleanup]
        public static void Stop() => s_session?.Dispose();

        public static T Run<T>(Func<T> f) => s_session!.Dispatch(f, default).GetAwaiter().GetResult();

        public static void Run(Action a) => s_session!.Dispatch(a, default).GetAwaiter().GetResult();

        /// <summary>Shows the control in a window of the given size and returns the rendered frame.</summary>
        public static WriteableBitmap Render(Control control, double width, double height, string name, Action<Window>? after = null)
        {
            var window = new Window { Width = width, Height = height, Content = control };
            window.Show();
            after?.Invoke(window);
            WriteableBitmap frame = Capture(window, name);
            window.Close();
            return frame;
        }

        /// <summary>Renders a window that is already showing and saves it like <see cref="Render"/>.</summary>
        public static WriteableBitmap Capture(Window window, string name)
        {
            WriteableBitmap frame = window.CaptureRenderedFrame()!;
            string? dir = Environment.GetEnvironmentVariable("MAPCONTROLS_DUMP");
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                frame.Save(Path.Combine(dir, name + ".png"), new PngBitmapEncoderOptions());
            }
            return frame;
        }

        public static uint Pixel(WriteableBitmap bmp, int x, int y)
        {
            using var fb = bmp.Lock();
            unsafe { return ((uint*)fb.Address)[y * fb.RowBytes / 4 + x]; }
        }
    }
}
