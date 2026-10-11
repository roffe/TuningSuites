using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T5App.ViewModels;
using T5App.Views;

namespace T5AppTest
{
    public partial class AppTest
    {
        /// <summary>
        /// docs/screenshots/t5, made by docs/screenshots/generate.sh (T5APP_DUMP set): a stock bin under its own name, each document
        /// maximized in the workspace, the tool windows and dialogs on their own. Without T5APP_DUMP it only checks they still render.
        /// </summary>
        [TestMethod, TestCategory("Screenshots")]
        public void Screenshots()
        {
            string file = Path.Combine(Directory.CreateDirectory(Path.Combine(s_dir, "screenshots")).FullName, StockBin);
            File.Copy(Path.Combine(Here(), "..", "T5Binaries", StockBin), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new T5MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));

                vm.OpenSymbolByName("Insp_mat!");
                Maximized(window, "mapviewer");

                var settings = new SettingsViewModel(vm.Settings, vm.T5Settings);
                // the default under the home folder, not whoever made the screenshots
                settings.ProjectFolder = settings.ProjectFolder.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~");
                foreach (var (w, name) in new (Window, string)[]
                {
                    (T5ToolWindows.Dyno(window, vm), "dyno"),
                    (new FirmwareOptionsWindow { DataContext = vm.FirmwareOptions()! }, "firmware"),
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
