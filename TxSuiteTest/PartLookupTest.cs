using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp;
using SuiteApp.ViewModels;
using SuiteApp.Views;

namespace TxSuiteTest
{
    public partial class SwitchTest
    {
        private const string T5Part = "4239273", T7Part = "5168646", T8Part = "55353231_FA56_C_FME2_37_FIEF_81c";

        /// <summary>
        /// Lookup partnumber with a T5 bin open finds a T7 partnumber (its list has a tab per ECU), and both Open this file and Create
        /// new from original switch to T7 with that file open.
        /// </summary>
        [TestMethod]
        public void PartLookupOpensAnotherEcusStockBinary()
        {
            // the stock bins in Binaries next to the program, as the Extras put them
            string library = Path.Combine(AppContext.BaseDirectory, "Binaries");
            Directory.CreateDirectory(library);
            string t7Stock = Path.Combine(library, T7Part + ".bin");
            foreach (var (folder, name) in new[] { ("T5Binaries", T5Part), ("T7Binaries", T7Part), ("T8Binaries", T8Part) })
                File.Copy(Path.Combine(Here(), "..", folder, name + ".bin"), Path.Combine(library, name + ".bin"), true);
            string t5 = Stock("T5Binaries", T5Part + ".bin");
            string copy = Path.Combine(s_dir, "copy-of-" + T7Part + ".bin");
            s_session!.Dispatch(async () =>
            {
                try
                {
                    var desktop = new ClassicDesktopStyleApplicationLifetime();
                    var vm = new T5App.ViewModels.T5MainWindowViewModel();
                    var window = TxSuite.App.NewWindow(new T5App.Views.MainView());
                    window.Width = 1200;
                    window.Height = 800;
                    SuiteStartup.Attach(desktop, vm, window);
                    window.Show();
                    TxSuite.App.Combine(desktop);
                    Assert.IsTrue(await vm.OpenPlainFileAsync(t5, true));

                    // every ECU's partnumbers, each with its own details; Compare only for the open suite's
                    var (lookupWindow, lookup) = OpenLookup(window);
                    lookup.PartNumber = T5Part;
                    lookup.Lookup();
                    Assert.IsTrue(lookup.CanCompare);
                    Assert.IsTrue(lookup.Info!.Extra.Count > 0);
                    lookup.PartNumber = T8Part;
                    lookup.Lookup();
                    Assert.IsFalse(lookup.Details);
                    Assert.IsFalse(lookup.CanCompare);
                    lookup.PartNumber = "123";
                    lookup.Lookup();
                    Assert.AreEqual("The entered partnumber was not recognized by TxSuite", lookup.Message);

                    // the list: T5, T7 and T8 tabs, T5's in front; the T7 tab's row picked
                    var browsed = lookup.BrowseAsync();
                    Dispatcher.UIThread.RunJobs();
                    var list = window.OwnedWindows.Single(w => w.Title == "Partnumber list");
                    var tabs = list.GetLogicalDescendants().OfType<TabControl>().Single();
                    CollectionAssert.AreEqual(new[] { "T5", "T7", "T8" }, tabs.Items.OfType<TabItem>().Select(t => (string)t.Header!).ToArray());
                    Assert.AreEqual(0, tabs.SelectedIndex);
                    Save(list, "partnumbers-t5");
                    AssertFolded(tabs, 0);
                    Assert.AreEqual(22, Headers(tabs, 0).Length);
                    // a header click sorts by the table's cell (From MY: an int) within the groups, which stay in order; a second one turns it around
                    var fromMy = ((DataGrid)((TabItem)tabs.Items[0]!).Content!).Columns[18];
                    foreach (bool descending in new[] { false, true })
                    {
                        fromMy.Sort();
                        Dispatcher.UIThread.RunJobs();
                        var sorted = Rows(tabs, 0);
                        CollectionAssert.AreEqual(sorted.Select(r => (r.Carmodel, r.Enginetype)).OrderBy(g => g.Carmodel).ThenBy(g => g.Enginetype).ToArray(),
                            sorted.Select(r => (r.Carmodel, r.Enginetype)).ToArray());
                        foreach (var group in sorted.GroupBy(r => (r.Carmodel, r.Enginetype)))
                        {
                            int[] years = group.Select(r => (int)r.Cells[18]!).ToArray();
                            CollectionAssert.AreEqual(descending ? years.OrderDescending().ToArray() : years.Order().ToArray(), years, $"{group.Key}");
                        }
                    }
                    // T7Suite's and T8Suite's lists: every partnumber they know with their columns, the ones in Binaries marked
                    tabs.SelectedIndex = 2;
                    Dispatcher.UIThread.RunJobs();
                    Save(list, "partnumbers-t8");
                    AssertFolded(tabs, 2);
                    var t8Rows = Rows(tabs, 2);
                    Assert.AreEqual(70, t8Rows.Length);
                    CollectionAssert.AreEqual(new[] { "Carmodel", "Enginetype", "Partnumber", "Software Version", "Makeyear" }, Headers(tabs, 2));
                    CollectionAssert.AreEqual(new[] { T8Part }, t8Rows.Where(r => r.Marked != null).Select(r => r.Partnumber).Distinct().ToArray());
                    tabs.SelectedIndex = 1;
                    Dispatcher.UIThread.RunJobs();
                    Save(list, "partnumbers-t7");
                    AssertFolded(tabs, 1);
                    var t7Rows = Rows(tabs, 1);
                    Assert.AreEqual(560, t7Rows.Length);
                    CollectionAssert.AreEqual(new[] { "Carmodel", "Enginetype", "Partnumber", "Turbomodel", "Turbo", "Power", "Torque", "Software Version",
                        "Emission Variant", "Makeyear" }, Headers(tabs, 1));
                    CollectionAssert.AreEqual(new[] { T7Part }, t7Rows.Where(r => r.Marked != null).Select(r => r.Partnumber).Distinct().ToArray());
                    var t7Row = t7Rows.First(r => r.Partnumber == T7Part);
                    Assert.AreEqual("Saab95", t7Row.Carmodel);
                    var export = T7App.Views.MainView.PartNumberTab().Export().ToList();
                    Assert.AreEqual("Carmodel\tEnginetype\tPartnumber\tTurbomodel\tTurbo\tPower\tTorque\tSoftware Version\tEmission Variant\tMakeyear", export[0]);
                    Assert.AreEqual(561, export.Count);
                    var line = export.Single(l => l.Split('\t')[2] == "5169446").Split('\t');
                    CollectionAssert.AreEqual(new[] { "Saab93", "B205L", "EB2RV13C.45C", "LEV US", "2001" }, new[] { line[0], line[1], line[7], line[8], line[9] });
                    var grid = (DataGrid)((TabItem)tabs.Items[1]!).Content!;
                    grid.SelectedItem = t7Row;
                    grid.ScrollIntoView(t7Row, null);
                    Dispatcher.UIThread.RunJobs();
                    Save(list, "partnumbers-t7-library");
                    Click(list, "Ok");
                    await browsed;
                    Assert.AreEqual(T7Part, lookup.PartNumber);
                    Assert.IsTrue(lookup.Details);
                    Assert.AreEqual(t7Stock, lookup.Info!.Binary);
                    Dispatcher.UIThread.RunJobs();
                    Assert.IsFalse(ButtonIn(lookupWindow, "Compare to this file").IsEnabled);
                    Assert.IsTrue(ButtonIn(lookupWindow, "Open this file").IsEnabled);

                    // Open this file: T7's view with the stock bin
                    Click(lookupWindow, "Open this file");
                    var t7vm = await SwitchedTo<T7App.ViewModels.T7MainWindowViewModel>(window, t7Stock);
                    Assert.IsInstanceOfType<T7App.Views.MainView>(window.View);

                    // back to T5, then Create new from original (the save picker's answer given straight to the dialog)
                    Assert.IsFalse(await t7vm.OpenPlainFileAsync(t5, true));
                    await SwitchedTo<T5App.ViewModels.T5MainWindowViewModel>(window, t5);
                    (lookupWindow, lookup) = OpenLookup(window);
                    lookup.PartNumber = T7Part;
                    lookup.Lookup();
                    Assert.IsTrue(ButtonIn(lookupWindow, "Create new from original").IsEnabled);
                    lookupWindow.Close("create:" + copy);
                    await SwitchedTo<T7App.ViewModels.T7MainWindowViewModel>(window, copy);
                    CollectionAssert.AreEqual(File.ReadAllBytes(t7Stock), File.ReadAllBytes(copy));
                    window.Close();
                }
                finally
                {
                    MainWindowViewModel.Handoff = null;
                    MainWindowViewModel.Combined = null;
                    SuiteMainView.SettingsDialog = null;
                    SuiteMainView.LookupAll = null;
                    SuiteMainView.BrowseAll = null;
                    Directory.Delete(library, true);
                }
                return true;
            }, default).GetAwaiter().GetResult();
        }

        /// <summary>The partnumber list opens on the open binary's ECU, on T5 with none open whichever suite is in front.</summary>
        [TestMethod]
        public void PartNumberListStartsOnTheOpenBinarysEcu()
        {
            string t7 = Stock("T7Binaries", T7Part + ".bin");
            s_session!.Dispatch(async () =>
            {
                try
                {
                    var desktop = new ClassicDesktopStyleApplicationLifetime();
                    var vm = new T7App.ViewModels.T7MainWindowViewModel();
                    var window = TxSuite.App.NewWindow(new T7App.Views.MainView());
                    SuiteStartup.Attach(desktop, vm, window);
                    window.Show();
                    TxSuite.App.Combine(desktop);
                    async Task<int> Tab()
                    {
                        var (lookupWindow, lookup) = OpenLookup(window);
                        var browsed = lookup.BrowseAsync();
                        Dispatcher.UIThread.RunJobs();
                        var list = window.OwnedWindows.Single(w => w.Title == "Partnumber list");
                        int tab = list.GetLogicalDescendants().OfType<TabControl>().Single().SelectedIndex;
                        Click(list, "Close");
                        await browsed;
                        lookupWindow.Close();
                        Dispatcher.UIThread.RunJobs();
                        return tab;
                    }
                    Assert.AreEqual(0, await Tab());
                    Assert.IsTrue(await vm.OpenPlainFileAsync(t7, true));
                    Assert.AreEqual(1, await Tab());
                    window.Close();
                }
                finally
                {
                    MainWindowViewModel.Handoff = null;
                    MainWindowViewModel.Combined = null;
                    SuiteMainView.SettingsDialog = null;
                    SuiteMainView.LookupAll = null;
                    SuiteMainView.BrowseAll = null;
                }
                return true;
            }, default).GetAwaiter().GetResult();
        }

        // the main menu's Lookup partnumber
        private static (PartLookupWindow, PartLookupViewModel) OpenLookup(SuiteWindow window)
        {
            window.View.GetLogicalDescendants().OfType<MenuItem>().First(m => m.Header as string == "Lookup partnumber")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var lookupWindow = window.OwnedWindows.OfType<PartLookupWindow>().Single();
            return (lookupWindow, (PartLookupViewModel)lookupWindow.DataContext!);
        }

        // the suite of the file in front with it open; the switch and the open run on after the dialog closed
        private static async Task<T> SwitchedTo<T>(SuiteWindow window, string file) where T : MainWindowViewModel
        {
            for (int i = 0; i < 200 && !(window.DataContext is T { Binary: { } bin, IsBusy: false } && bin.FileName == file); i++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(50);
            }
            var vm = window.DataContext as T;
            Assert.IsNotNull(vm, $"{typeof(T).Name} in front, not {window.DataContext?.GetType().Name}");
            Assert.AreEqual(file, vm.Binary?.FileName);
            Assert.AreSame(vm, window.View.DataContext);
            return vm;
        }

        private static PartNumberRow[] Rows(TabControl tabs, int tab) =>
            ((System.Collections.IEnumerable)((DataGrid)((TabItem)tabs.Items[tab]!).Content!).ItemsSource!).Cast<PartNumberRow>().ToArray();

        // folded as DevExpress' grid started: only the car models' headers show
        private static void AssertFolded(TabControl tabs, int tab)
        {
            var grid = (DataGrid)((TabItem)tabs.Items[tab]!).Content!;
            var shown = grid.GetVisualDescendants().Where(c => c.IsEffectivelyVisible).ToList();
            Assert.AreEqual(0, shown.OfType<DataGridRow>().Count(), $"tab {tab}");
            Assert.AreEqual(Rows(tabs, tab).Select(r => r.Carmodel).Distinct().Count(), shown.OfType<DataGridRowGroupHeader>().Count(), $"tab {tab}");
        }

        private static string[] Headers(TabControl tabs, int tab) =>
            ((DataGrid)((TabItem)tabs.Items[tab]!).Content!).Columns.Select(c => (string)c.Header!).ToArray();

        private static Button ButtonIn(Window window, string caption) =>
            window.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == caption);

        private static void Click(Window window, string caption) => ButtonIn(window, caption).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
