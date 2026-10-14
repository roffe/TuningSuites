using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommonSuite;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TxSuite.ViewModels;
using TxSuite.Views;
using SuiteApp.ViewModels;
using SuiteApp.Views;
using T7App.ViewModels;
using T7App.Views;

namespace T7AppTest
{
    public partial class AppTest
    {
        /// <summary>
        /// A compare row's double-click: both maps in one window, as tabs along its bottom (T7Suite's DockAsTab onto the panel of the
        /// same symbol), the open file's first and selected; the caption is the selected tab's.
        /// </summary>
        [TestMethod]
        public void CompareOpensMapsAsTabs()
        {
            string a = Path.Combine(s_dir, "tabs-a.bin"), b = Path.Combine(s_dir, "tabs-b.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), a, true);
            File.Copy(a, b, true);
            var binB = T7.T7Binary.Open(b, 0, false);
            foreach (string name in new[] { "IgnNormCal.Map", "BFuelCal.Map" })
            {
                var sh = binB.Find(name);
                byte[] changed = binB.ReadSymbol(sh);
                changed[1] ^= 0x10;
                binB.WriteSymbol(binB.FileAddress(sh), changed, false);
            }
            s_session!.Dispatch(async () =>
            {
                var vm = new MainViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                vm.Settings.SynchronizeMapviewers = true;
                Assert.IsTrue(await vm.OpenPlainFileAsync(a, true));
                await vm.CompareToFileAsync(b);
                var results = (CompareResultsViewModel)vm.SelectedViewer!;
                var rows = results.Rows.SourceCollection.Cast<CompareRow>().ToList();
                CompareRow ign = rows.Single(r => r.SymbolName == "IgnNormCal.Map"), fuel = rows.Single(r => r.SymbolName == "BFuelCal.Map");

                results.Open(ign);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var tabs = vm.DockedViewers.OfType<DocumentTabsViewModel>().Single();
                CollectionAssert.AreEqual(new DocumentViewModel[] { results, tabs }, vm.DockedViewers.ToArray(), "one new window, not two");
                // the compare file's map first and selected, as T7Suite's DockAsTab at index 0; the open file's second
                CollectionAssert.AreEqual(new[] { "Symbol: IgnNormCal.Map [tabs-b.bin]", "Symbol: IgnNormCal.Map [tabs-a.bin]" },
                    tabs.Items.Select(t => t.Title).ToArray());
                var theirs = (MapViewerViewModel)tabs.Items[0];
                var mine = (MapViewerViewModel)tabs.Items[1];
                Assert.IsFalse(mine.IsReadOnly);
                Assert.IsTrue(theirs.IsReadOnly);
                Assert.AreSame(theirs, vm.SelectedViewer);
                Assert.AreSame(theirs, tabs.Selected);
                Assert.AreEqual(theirs.Title, tabs.Title);

                // the window: the caption is the selected tab's, the tabs sit along its bottom under the shown map
                window.UpdateLayout();
                window.CaptureRenderedFrame();
                var mdis = window.GetVisualDescendants().OfType<MdiDocumentWindow>().ToList();
                Assert.HasCount(2, mdis);
                var mdi = mdis.Single(w => (w.DataContext as IDockable)?.Context == tabs);
                var dockable = (IDockable)mdi.DataContext!;
                var dock = (Dock.Model.Controls.IDocumentDock)dockable.Owner!;
                Assert.AreSame(dockable, dock.ActiveDockable);
                string Caption() => mdi.GetVisualDescendants().OfType<Control>().First(c => c.Name == "PART_Header")
                    .GetVisualDescendants().OfType<TextBlock>().First(t => !string.IsNullOrEmpty(t.Text)).Text!;
                Assert.AreEqual(theirs.Title, Caption());
                var strip = mdi.GetVisualDescendants().OfType<TabStrip>().Single();
                Assert.IsTrue(strip.IsEffectivelyVisible);
                var headers = strip.GetVisualDescendants().OfType<TabStripItem>().ToList();
                CollectionAssert.AreEqual(new[] { theirs.Title, mine.Title },
                    headers.Select(h => h.GetVisualDescendants().OfType<TextBlock>().First().Text).ToArray());
                var viewers = mdi.GetVisualDescendants().OfType<MapControls.MapViewer>().ToList();
                Assert.HasCount(2, viewers, "both tabs keep a full viewer");
                MapControls.MapViewer mineView = viewers.Single(v => v.Map == mine.Map), theirsView = viewers.Single(v => v.Map == theirs.Map);
                bool Shown(MapControls.MapViewer v) => v.IsEffectivelyEnabled && v.GetVisualAncestors().OfType<ContentControl>().First(c => c.Classes.Contains("page")).Opacity == 1;
                Assert.IsTrue(Shown(theirsView));
                Assert.IsFalse(Shown(mineView));
                var content = mdi.GetVisualDescendants().OfType<DocumentTabsView>().Single();
                double stripTop = strip.TranslatePoint(default, content)!.Value.Y;
                Assert.AreEqual(content.Bounds.Height, stripTop + strip.Bounds.Height, 0.5, "the tabs at the bottom");
                Assert.IsTrue(theirsView.TranslatePoint(new Point(0, theirsView.Bounds.Height), content)!.Value.Y <= stripTop + 0.5);
                // a file picker disables the main window: the shown map stays shown
                window.IsEnabled = false;
                Assert.AreEqual(1, theirsView.GetVisualAncestors().OfType<ContentControl>().First(c => c.Classes.Contains("page")).Opacity);
                window.IsEnabled = true;
                Save(window, "compare-tabs");

                // the other tab: its map shows, caption and selected document follow
                Point at = headers[1].TranslatePoint(new Point(20, headers[1].Bounds.Height / 2), window)!.Value;
                window.MouseDown(at, MouseButton.Left);
                window.MouseUp(at, MouseButton.Left);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.AreSame(mine, tabs.Selected);
                Assert.AreSame(mine, vm.SelectedViewer);
                Assert.AreEqual(mine.Title, dockable.Title);
                Assert.AreEqual(mine.Title, Caption());
                Assert.IsTrue(Shown(mineView));
                Assert.IsFalse(Shown(theirsView));
                Save(window, "compare-tabs-other");

                // "Synchronize mapviewers": the hidden tab follows the shown one's selection
                mineView.Grid.Select([3, 4]);
                CollectionAssert.AreEquivalent(new[] { 3, 4 }, theirsView.Grid.SelectedCells.ToArray());

                // the same row again brings that window forward, the compare tab selected; nothing new opens
                vm.SelectedViewer = results;
                int open = vm.Viewers.Count;
                results.Open(ign);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(open, vm.Viewers.Count);
                Assert.AreSame(theirs, vm.SelectedViewer);
                Assert.AreSame(theirs, tabs.Selected);
                Assert.AreSame(dockable, dock.ActiveDockable);
                Assert.HasCount(2, window.GetVisualDescendants().OfType<MdiDocumentWindow>());
                // and the symbol list's double-click on the map selects its tab
                tabs.Selected = theirs;
                vm.OpenSymbolByName("IgnNormCal.Map");
                Assert.AreSame(mine, vm.SelectedViewer);
                Assert.AreSame(mine, tabs.Selected);

                // the tabs are the main window's map viewers: what looks for them finds them (here the reload of a changed file)
                CollectionAssert.IsSubsetOf(new[] { mine, theirs }, vm.Viewers.OfType<MapViewerViewModel>().ToList());
                var binA = T7.T7Binary.Open(a, 0, false);
                byte[] edited = binA.ReadSymbol(binA.Find("IgnNormCal.Map"));
                edited[0] ^= 0x01;
                binA.WriteSymbol(binA.FileAddress(binA.Find("IgnNormCal.Map")), edited, false);
                vm.RefreshViewers(a);
                CollectionAssert.AreEqual(edited, mine.Map.ToBytes());

                // a map open on its own moves into the compare's window (T7Suite docked onto the panel of the same symbol)
                var ownFuel = vm.OpenSymbolByName("BFuelCal.Map")!;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Contains(ownFuel, vm.DockedViewers);
                results.Open(fuel);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var fuelTabs = (DocumentTabsViewModel)ownFuel.Tabs!;
                Assert.DoesNotContain(ownFuel, vm.DockedViewers);
                CollectionAssert.AreEqual(new DocumentViewModel[] { results, tabs, fuelTabs }, vm.DockedViewers.ToArray());
                CollectionAssert.AreEqual(new[] { "Symbol: BFuelCal.Map [tabs-b.bin]", "Symbol: BFuelCal.Map [tabs-a.bin]" },
                    fuelTabs.Items.Select(t => t.Title).ToArray());
                Assert.AreSame(ownFuel, fuelTabs.Items[1]);
                Assert.AreSame(fuelTabs.Items[0], vm.SelectedViewer);

                // a tab's own close button closes that tab alone; the last one left shows as a plain window
                var fuelMdi = window.GetVisualDescendants().OfType<MdiDocumentWindow>().Single(w => (w.DataContext as IDockable)?.Context == fuelTabs);
                window.UpdateLayout();
                var close = fuelMdi.GetVisualDescendants().OfType<TabStripItem>().First().GetVisualDescendants().OfType<Button>().Single();
                close.Command!.Execute(close.CommandParameter);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.AreSame(ownFuel, fuelTabs.Items.Single());
                Assert.IsFalse(fuelMdi.GetVisualDescendants().OfType<TabStrip>().Single().IsVisible);
                Assert.HasCount(1, vm.Viewers.OfType<MapViewerViewModel>().Where(v => v.MapName == "BFuelCal.Map"));
                Assert.AreSame(ownFuel, vm.SelectedViewer);

                // dragged out the window floats with its tabs, the selected one kept; Dock all brings it back
                tabs.Selected = theirs;
                tabs.IsFloating = true;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var floating = window.OwnedWindows.OfType<FloatingDocumentWindow>().Single();
                floating.UpdateLayout();
                Assert.AreEqual(theirs.Title, floating.Title);
                Assert.HasCount(2, floating.GetVisualDescendants().OfType<TabStripItem>());
                Assert.AreSame(theirs, tabs.Selected);
                Save(floating, "compare-tabs-floating");
                vm.DockAllCommand.Execute(null);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.IsEmpty(window.OwnedWindows);
                Assert.Contains(tabs, vm.DockedViewers);
                Assert.AreSame(theirs, tabs.Selected);

                // the window's close button closes every tab, each asking about its own changes: Cancel keeps them, No closes them
                mine.Map.Set([(0, mine.Map[0] + 1)]);
                bool? answer = null;
                int asked = 0;
                vm.AskYesNoCancel = _ => { asked++; return Task.FromResult(answer); };
                dockable = dock.VisibleDockables!.Single(d => d.Context == tabs);
                dock.Factory!.CloseDockable(dockable);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(1, asked);
                Assert.AreSame(mine, vm.SelectedViewer, "the question shows the map it is about");
                CollectionAssert.AreEqual(new DocumentViewModel[] { theirs, mine }, tabs.Items.ToArray(), "Cancel keeps every tab, the one before too");
                Assert.Contains(tabs, vm.DockedViewers);
                answer = false;
                dock.Factory.CloseDockable(dockable);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(2, asked);
                Assert.DoesNotContain(mine, vm.Viewers);
                Assert.DoesNotContain(theirs, vm.Viewers);
                Assert.DoesNotContain(tabs, vm.Viewers);
                CollectionAssert.AreEqual(new DocumentViewModel[] { results, fuelTabs }, vm.DockedViewers.ToArray());
                Assert.HasCount(2, window.GetVisualDescendants().OfType<MdiDocumentWindow>());
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
