using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7App.ViewModels;
using SuiteApp.ViewModels;

namespace T7AppTest
{
    public partial class AppTest
    {
        private static byte[] Pixels(Avalonia.Controls.Window w, out int stride)
        {
            WriteableBitmap frame = w.CaptureRenderedFrame()!;
            using var fb = frame.Lock();
            stride = fb.RowBytes;
            byte[] data = new byte[fb.RowBytes * fb.Size.Height];
            Marshal.Copy(fb.Address, data, 0, data.Length);
            return data;
        }

        private static int Diff(byte[] a, byte[] b, int stride, PixelRect r)
        {
            int n = 0;
            for (int y = r.Y; y < r.Bottom; y++)
                for (int x = r.X; x < r.Right; x++)
                {
                    int i = y * stride + x * 4;
                    if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2]) n++;
                }
            return n;
        }

        /// <summary>
        /// An inner window behind another, activated by its title bar or from code, repaints in front: Dock only raises its
        /// ZIndex and the compositor re-sorted the windows without redrawing them, leaving the other window's table and graph
        /// painted over it until it was dragged.
        /// </summary>
        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void BackInnerWindowRepaintsInFront(bool click)
        {
            string file = Path.Combine(s_dir, $"zorder-{click}.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new T7MainWindowViewModel();
                var window = new T7App.Views.MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                vm.OpenSymbolByName("IgnNormCal.Map");
                vm.OpenSymbolByName("BFuelCal.Map");
                window.CaptureRenderedFrame();
                var docs = DockHost(window);
                var dock = (Dock.Model.Controls.IDocumentDock)window.GetVisualDescendants().OfType<Dock.Avalonia.Controls.DocumentDockControl>().First().DataContext!;
                var ign = (MapViewerViewModel)vm.Viewers[0];
                var back = dock.VisibleDockables!.First(d => d.Context == ign);
                var front = dock.VisibleDockables!.First(d => d.Context != ign);
                ((Dock.Model.Controls.IMdiDocument)back).MdiBounds = new Dock.Model.Core.DockRect(0, 0, 800, 650);
                ((Dock.Model.Controls.IMdiDocument)front).MdiBounds = new Dock.Model.Core.DockRect(200, 120, 800, 650);
                docs.Factory!.SetActiveDockable(front);
                window.CaptureRenderedFrame();

                var windows = window.GetVisualDescendants().OfType<Dock.Avalonia.Controls.MdiDocumentWindow>().ToList();
                var backWindow = windows.First(w => (w.DataContext as Dock.Model.Core.IDockable)?.Context == ign);
                var frontWindow = windows.First(w => w != backWindow);
                Rect a = new(backWindow.TranslatePoint(default, window)!.Value, backWindow.Bounds.Size);
                Rect b = new(frontWindow.TranslatePoint(default, window)!.Value, frontWindow.Bounds.Size);
                PixelRect overlap = PixelRect.FromRect(a.Intersect(b).Deflate(4), 1);
                Assert.IsTrue(overlap.Width > 100 && overlap.Height > 100);

                if (click)
                {
                    var header = backWindow.GetVisualDescendants().OfType<Control>().First(c => c.Name == "PART_Header");
                    Point p = header.TranslatePoint(new Point(30, header.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(p, MouseButton.Left, RawInputModifiers.None);
                    window.MouseUp(p, MouseButton.Left, RawInputModifiers.None);
                }
                else
                    docs.Factory.SetActiveDockable(back);
                Assert.AreSame(back, dock.ActiveDockable);
                byte[] shown = Pixels(window, out int stride);

                // the same state painted from scratch
                foreach (Visual v in window.GetVisualDescendants()) v.InvalidateVisual();
                byte[] repainted = Pixels(window, out _);
                Assert.AreEqual(0, Diff(shown, repainted, stride, overlap), "stale pixels where the windows overlap");
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        /// <summary>
        /// A narrow inner window cuts its title short and keeps its buttons: Dock's panel measured the window at the workspace's
        /// width, the title column kept that width and pushed minimize / maximize / close out of the window.
        /// </summary>
        [TestMethod]
        public void NarrowInnerWindowTruncatesItsTitle()
        {
            string file = Path.Combine(s_dir, "9-5_B235E_07-08_BIOPOWER_EU_EUOAF01C.55P.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new T7MainWindowViewModel();
                var window = new T7App.Views.MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                vm.OpenSymbolByName("BFuelCal.Map");
                window.CaptureRenderedFrame();
                var dock = (Dock.Model.Controls.IDocumentDock)window.GetVisualDescendants().OfType<Dock.Avalonia.Controls.DocumentDockControl>().First().DataContext!;
                ((Dock.Model.Controls.IMdiDocument)dock.VisibleDockables![0]).MdiBounds = new Dock.Model.Core.DockRect(10, 10, 420, 600);
                window.CaptureRenderedFrame();

                var inner = window.GetVisualDescendants().OfType<Dock.Avalonia.Controls.MdiDocumentWindow>().Single();
                Assert.AreEqual(420, inner.Bounds.Width);
                var close = inner.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_CloseButton");
                Assert.IsTrue(close.IsEffectivelyVisible);
                Assert.IsLessThanOrEqualTo(inner.Bounds.Width, close.TranslatePoint(new Point(close.Bounds.Width, 0), inner)!.Value.X);
                var title = inner.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text?.StartsWith("Symbol: BFuelCal.Map") == true);
                Assert.IsLessThan(close.TranslatePoint(default, inner)!.Value.X, title.TranslatePoint(new Point(title.Bounds.Width, 0), inner)!.Value.X);
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Inner windows → tabs → inner windows → tabs → inner windows (the window's Layout mode menu): every switch shows the
        /// documents. The views moved into the tab stayed there and left their inner windows blank, and later the hidden tab took
        /// the view of whichever window was activated.
        /// </summary>
        [TestMethod]
        public void SwitchingBetweenTabsAndInnerWindowsKeepsTheViews()
        {
            string file = Path.Combine(s_dir, "layout.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new T7MainWindowViewModel();
                var window = new T7App.Views.MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                vm.OpenSymbolByName("IgnNormCal.Map");
                vm.OpenSymbolByName("BFuelCal.Map");
                window.CaptureRenderedFrame();
                var docs = DockHost(window);
                var dock = (Dock.Model.Controls.IDocumentDock)window.GetVisualDescendants().OfType<Dock.Avalonia.Controls.DocumentDockControl>().First().DataContext!;
                int Shown()
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    window.CaptureRenderedFrame();
                    return window.GetVisualDescendants().OfType<MapControls.MapViewer>().Count(v => v.IsEffectivelyVisible && v.Bounds.Width > 0);
                }
                Assert.AreEqual(2, Shown());
                for (int i = 0; i < 2; i++)
                {
                    docs.Factory!.SetDocumentDockLayoutModeTabbed(dock);
                    Assert.AreEqual(1, Shown(), "the active tab");
                    docs.Factory.SetDocumentDockLayoutModeMdi(dock);
                    Assert.AreEqual(2, Shown(), "both inner windows");
                    // activating (clicking, moving) an inner window afterwards: the hidden tab followed the active document and
                    // took its view, the window went grey
                    foreach (var d in dock.VisibleDockables!.ToList())
                    {
                        docs.Factory.SetActiveDockable(d);
                        Assert.AreEqual(2, Shown(), "both inner windows after activating one");
                    }
                }
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
