using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CommonSuite;
using MapControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7App.ViewModels;
using T7App.Views;

namespace T7AppTest
{
    public partial class AppTest
    {
        /// <summary>Settings' Mapviewer cell spacing: saved, an open viewer's table takes it at once, a viewer opened later starts with it.</summary>
        [TestMethod]
        public void MapCellSpacingReachesOpenViewers()
        {
            string file = Path.Combine(s_dir, "cellgap.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new T7MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                try
                {
                    Assert.IsTrue(await vm.OpenFileAsync(file, true));
                    vm.OpenSymbolCommand.Execute(vm.Symbols!.Cast<SymbolHelper>().First(s => s.Varname == "IgnNormCal.Map"));
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    var grid = window.GetVisualDescendants().OfType<MapGrid>().Single();
                    Assert.AreEqual(0, grid.CellGap);

                    var settings = new SettingsViewModel(vm.Settings) { MapCellGap = 4 };
                    settings.Apply(vm.Settings);
                    vm.SettingsChanged();
                    Assert.AreEqual(4, new AppSettings(new T7.T7SuiteRegistry()).MapCellGap);
                    Assert.AreEqual(4, grid.CellGap);
                    Save(window, "main-cellgap");

                    vm.OpenSymbolCommand.Execute(vm.Symbols!.Cast<SymbolHelper>().First(s => s.Varname == "BFuelCal.Map"));
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Assert.IsTrue(window.GetVisualDescendants().OfType<MapGrid>().All(g => g.CellGap == 4));
                }
                finally
                {
                    vm.Settings.MapCellGap = 0;
                    vm.SettingsChanged();
                    window.Close();
                }
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
