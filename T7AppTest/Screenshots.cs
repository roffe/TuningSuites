using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.Views;
using T7App.ViewModels;
using T7App.Views;

namespace T7AppTest
{
    public partial class AppTest
    {
        /// <summary>
        /// docs/screenshots/t7, made by docs/screenshots/generate.sh (T7APP_DUMP set): a stock bin under its own name, each document
        /// maximized in the workspace, the dialogs on their own. Without T7APP_DUMP it only checks they still render.
        /// </summary>
        [TestMethod, TestCategory("Screenshots")]
        public void Screenshots()
        {
            string file = Path.Combine(Directory.CreateDirectory(Path.Combine(s_dir, "screenshots")).FullName, "5168646.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                try
                {
                    // each shot in the light skin, then in the dark one (<name>-dark.png)
                    foreach (var (theme, suffix) in new[] { (ThemeVariant.Light, ""), (ThemeVariant.Dark, "-dark") })
                    {
                        var vm = new T7MainWindowViewModel();
                        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
                        window.Show();
                        Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                        // after the window applied the skin from the settings
                        Avalonia.Application.Current!.RequestedThemeVariant = theme;

                        vm.OpenSymbolByName("IgnNormCal.Map");
                        Maximized(window, "mapviewer" + suffix);

                        vm.ShowAirmassResultCommand.Execute(null);
                        Maximized(window, "airmass-table" + suffix);
                        window.GetVisualDescendants().OfType<AirmassResultView>().Single().GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 1;
                        Maximized(window, "airmass-dyno" + suffix);

                        var settings = new SettingsViewModel(vm.Settings);
                        // the default under the home folder, not whoever made the screenshots
                        settings.ProjectFolder = settings.ProjectFolder.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~");
                        foreach (var (w, name) in new (Window, string)[]
                        {
                            (new FirmwareInfoWindow { DataContext = vm.FirmwareInfo()! }, "firmware"),
                            (new SettingsWindow { DataContext = settings }, "settings"),
                        })
                        {
                            w.Show();
                            Save(w, name + suffix);
                            w.Close();
                        }
                        window.Close();
                    }
                }
                finally
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
                }
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
