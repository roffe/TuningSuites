using System.IO;
using CommonSuite;
using MapControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.ViewModels;
using T7App.ViewModels;
using T7App.Views;

namespace T7AppTest
{
    public partial class AppTest
    {
        /// <summary>T7Suite's ShowAfrMAP took its viewer from MapViewerFactory.Get(settings): the AFR maps open in the default view.</summary>
        [TestMethod]
        public void AfrMapsOpenInTheDefaultView()
        {
            string file = Path.Combine(s_dir, "afrview.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new T7MainWindowViewModel();
                SuiteViewType before = vm.Settings.DefaultViewType;
                vm.Settings.DefaultViewType = SuiteViewType.Hexadecimal;
                try
                {
                    var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                    window.Show();
                    Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                    vm.OpenSymbolByName("BFuelCal.Map");
                    Assert.AreEqual(MapViewType.Hex, ((MapViewerViewModel)vm.SelectedViewer!).ViewType);
                    vm.ShowAfrTargetMapCommand.Execute(null);
                    var target = (MapViewerViewModel)vm.SelectedViewer!;
                    Assert.IsTrue(target.IsAfrMap);
                    Assert.AreEqual(MapViewType.Hex, target.ViewType);
                    vm.Settings.DefaultViewType = SuiteViewType.Decimal;
                    vm.ShowAfrCounterMapCommand.Execute(null);
                    Assert.AreEqual(MapViewType.Decimal, ((MapViewerViewModel)vm.SelectedViewer!).ViewType);
                    window.Close();
                }
                finally
                {
                    vm.Settings.DefaultViewType = before;
                }
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
