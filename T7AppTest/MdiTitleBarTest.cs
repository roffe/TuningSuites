using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommonSuite;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7App.ViewModels;
using T7App.Views;

namespace T7AppTest
{
    public partial class AppTest
    {
        [TestMethod]
        public void TitleBarDoubleClickMaximizesAndRestores()
        {
            string file = Path.Combine(s_dir, "mdi.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new T7MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenFileAsync(file, true));
                vm.OpenSymbolCommand.Execute(vm.Symbols!.Cast<SymbolHelper>().First(s => s.Varname == "IgnNormCal.Map"));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var mdi = window.GetVisualDescendants().OfType<MdiDocumentWindow>().Single();
                var doc = (IMdiDocument)mdi.DataContext!;

                // two clicks on the title, left of the window buttons
                void DoubleClick()
                {
                    window.UpdateLayout();
                    var header = mdi.GetVisualDescendants().OfType<Control>().First(c => c.Name == "PART_Header");
                    Point at = header.TranslatePoint(new Point(60, header.Bounds.Height / 2), window)!.Value;
                    for (int i = 0; i < 2; i++)
                    {
                        window.MouseDown(at, MouseButton.Left);
                        window.MouseUp(at, MouseButton.Left);
                    }
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                }

                Assert.AreEqual(MdiWindowState.Normal, doc.MdiState);
                DoubleClick();
                Assert.AreEqual(MdiWindowState.Maximized, doc.MdiState, "a double click on the title bar maximizes");
                DoubleClick();
                Assert.AreEqual(MdiWindowState.Normal, doc.MdiState, "and another restores");
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
