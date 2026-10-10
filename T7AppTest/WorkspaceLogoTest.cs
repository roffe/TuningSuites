using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.Controls;
using T7App.ViewModels;
using T7App.Views;

namespace T7AppTest
{
    public partial class AppTest
    {
        /// <summary>The empty workspace shows the embossed logo, read from the SVG's path.</summary>
        [TestMethod]
        public void EmptyWorkspaceShowsTheLogo()
        {
            s_session!.Dispatch(() =>
            {
                var brush = (DrawingBrush)WorkspaceLogo.Brush;
                Assert.IsTrue(brush.Drawing!.GetBounds().Width > 100, "the griffin's outline");
                var window = new MainWindow { DataContext = new T7MainWindowViewModel(), Width = 1500, Height = 950 };
                window.Show();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var host = window.GetVisualDescendants().OfType<MdiDocumentControl>().Single()
                    .GetVisualDescendants().OfType<ContentControl>().Single(c => c.Name == "PART_EmptyContentHost");
                Assert.IsTrue(host.IsVisible);
                Assert.AreSame(brush, host.Background);
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
