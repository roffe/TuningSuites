using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.ViewModels;
using SuiteApp.Views;
using T8App.ViewModels;
using T8App.Views;

namespace T8AppTest
{
    public partial class AppTest
    {
        /// <summary>
        /// docs/screenshots/t8, made by docs/screenshots/generate.sh (T8APP_DUMP set): a stock bin under its own name, each document
        /// maximized in the workspace, the dialogs on their own. Without T8APP_DUMP it only checks they still render.
        /// </summary>
        [TestMethod, TestCategory("Screenshots")]
        public void Screenshots()
        {
            string file = Path.Combine(Directory.CreateDirectory(Path.Combine(s_dir, "screenshots")).FullName, StockBin);
            File.Copy(Path.Combine(Here(), "..", "T8Binaries", StockBin), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new T8MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));

                vm.OpenSymbolByName("IgnAbsCal.fi_NormalMAP");
                Maximized(window, "mapviewer");

                vm.ShowAirmassResultCommand.Execute(null);
                Maximized(window, "airmass-table");
                window.GetVisualDescendants().OfType<AirmassResultView>().Single().GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 1;
                Maximized(window, "airmass-dyno");

                var settings = new SettingsViewModel(vm.Settings);
                // the default under the home folder, not whoever made the screenshots
                settings.ProjectFolder = settings.ProjectFolder.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~");
                foreach (var (w, name) in new (Window, string)[]
                {
                    (new FirmwareInfoWindow { DataContext = new FirmwareInfoViewModel(vm.FirmwareInfo()!) }, "firmware"),
                    (new SettingsWindow { DataContext = settings }, "settings"),
                })
                {
                    w.Show();
                    Save(w, name);
                    w.Close();
                }
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        private static void Maximized(Window window, string name)
        {
            // a pass first: the document's window has to be in the workspace to be maximized
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            foreach (var mdi in window.GetVisualDescendants().OfType<Dock.Avalonia.Controls.DocumentDockControl>().Select(d => d.DataContext)
                .OfType<Dock.Model.Controls.IDocumentDock>().SelectMany(d => d.VisibleDockables ?? []).OfType<Dock.Model.Controls.IMdiDocument>())
                mdi.MdiState = Dock.Model.Core.MdiWindowState.Maximized;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            Save(window, name);
        }
    }
}
