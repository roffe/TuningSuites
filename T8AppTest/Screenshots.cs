using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TxSuite.ViewModels;
using TxSuite.Views;
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
                try
                {
                    // each shot in the light skin, then in the dark one (<name>-dark.png)
                    foreach (var (theme, suffix) in new[] { (ThemeVariant.Light, ""), (ThemeVariant.Dark, "-dark") })
                    {
                        var vm = new MainViewModel();
                        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
                        window.Show();
                        Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                        // after the window applied the skin from the settings
                        Avalonia.Application.Current!.RequestedThemeVariant = theme;

                        vm.OpenSymbolByName("IgnAbsCal.fi_NormalMAP");
                        Maximized(window, "mapviewer" + suffix);
                        Palettes(window, "bfuelcal", suffix);

                        vm.T8.ShowAirmassResultCommand.Execute(null);
                        Maximized(window, "airmass-table" + suffix);
                        window.GetVisualDescendants().OfType<AirmassResultView>().Single().GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 1;
                        Maximized(window, "airmass-dyno" + suffix);

                        var settings = new SettingsViewModel(vm.Settings);
                        // the default under the home folder, not whoever made the screenshots
                        settings.ProjectFolder = settings.ProjectFolder.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~");
                        foreach (var (w, name) in new (Window, string)[]
                        {
                            (new FirmwareInfoWindow { DataContext = new FirmwareInfoViewModel(vm.T8.FirmwareInfo()!) }, "firmware"),
                            (ShowSettings(vm, edit: s => s.Common.ProjectFolder = settings.ProjectFolder), "settings"),
                        })
                        {
                            w.Show();
                            Save(w, name + suffix);
                            w.Close();
                        }
                        window.Close();
                        await ManualScreenshots(file, theme, suffix);
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

        // Ctrl+P and Ctrl+Shift+P with something typed, over the map
        private static void Palettes(Window window, string symbols, string suffix)
        {
            foreach (var (shift, text, name) in new[] { (false, symbols, "palette-symbols"), (true, "compare", "palette-commands") })
            {
                window.KeyPress(Key.P, RawInputModifiers.Control | (shift ? RawInputModifiers.Shift : 0), PhysicalKey.P, "p");
                window.KeyTextInput(text);
                Save(window, name + suffix);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }
        }
    }
}
