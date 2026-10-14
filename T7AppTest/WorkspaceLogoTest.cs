using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using CommonSuite;
using Dock.Avalonia.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TxSuite.ViewModels;
using TxSuite.Views;
using SuiteApp.Controls;
using T7App.ViewModels;
using T7App.Views;

namespace T7AppTest
{
    public partial class AppTest
    {
        /// <summary>The workspace shows the embossed logo (read from the SVG's path) empty and behind its open windows.</summary>
        [TestMethod]
        public void WorkspaceShowsTheLogo()
        {
            string file = Path.Combine(s_dir, "logo.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var brush = (DrawingBrush)WorkspaceLogo.Brush;
                Assert.IsTrue(brush.Drawing!.GetBounds().Width > 100, "the griffin's outline");
                var vm = new MainViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Control Shown(string name) => window.GetVisualDescendants().OfType<MdiDocumentControl>().Single()
                    .GetVisualDescendants().OfType<TemplatedControl>().Single(c => c.Name == name && c.IsVisible);
                // empty: Dock's empty content host; with a window open: the windows' items control, under them
                Assert.AreSame(brush, ((TemplatedControl)Shown("PART_EmptyContentHost")).Background);
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                vm.OpenSymbolCommand.Execute(vm.Symbols!.Cast<SymbolHelper>().First(s => s.Varname == "IgnNormCal.Map"));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.AreSame(brush, ((TemplatedControl)Shown("PART_ItemsControl")).Background);
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
