using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7App.ViewModels;
using T7App.Views;

namespace T7AppTest
{
    public partial class AppTest
    {
        /// <summary>T8Suite's map preview popup in T7 too: hovering a name in the symbol list shows its map, read only.</summary>
        [TestMethod]
        public void MapPreviewPopup()
        {
            s_session!.Dispatch(async () =>
            {
                var vm = new T7MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenFileAsync(Path.Combine(s_dir, "5168646.bin"), true));
                SymbolHelper map = vm.Binary!.Find("IgnNormCal.Map");
                // on by default (T8Suite: off); off shows none
                Assert.IsTrue(vm.Settings.ShowMapPreviewPopup);
                try
                {
                    vm.Settings.ShowMapPreviewPopup = false;
                    Assert.IsNull(vm.MapPreview(map));
                    vm.Settings.ShowMapPreviewPopup = true;
                    vm.SearchText = "IgnNormCal.Map";
                    window.CaptureRenderedFrame();
                    var name = window.GetVisualDescendants().OfType<Border>().First(b => b.DataContext == map && ToolTip.GetTip(b) != null);
                    var opening = new Avalonia.Interactivity.CancelRoutedEventArgs(ToolTip.ToolTipOpeningEvent);
                    name.RaiseEvent(opening);
                    Assert.IsFalse(opening.Cancel);
                    var grid = (MapControls.MapGrid)ToolTip.GetTip(name)!;
                    Assert.IsTrue(grid.IsReadOnly);
                    Assert.AreEqual(vm.Binary.TableWidth("IgnNormCal.Map"), grid.Map!.Cols);
                }
                finally
                {
                    vm.Settings.ShowMapPreviewPopup = true;
                }
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
