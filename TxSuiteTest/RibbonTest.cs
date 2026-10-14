using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.Controls;
using SuiteApp.ViewModels;

namespace TxSuiteTest
{
    /// <summary>Settings → Show ribbon bar instead of menus: the menus as the old suites' ribbon.</summary>
    public partial class SwitchTest
    {
        // the selected tab's groups that show
        private static List<RibbonGroup> Groups(Ribbon ribbon) =>
            [.. ((RibbonPage)ribbon.Tabs.SelectedContent!).Children.OfType<RibbonGroup>().Where(g => g.IsVisible)];

        private static RibbonGroup Group(Ribbon ribbon, string caption) => Groups(ribbon).Single(g => g.Caption == caption);

        private static string[] Shown(RibbonGroup group) => [.. group.Buttons.Where(b => b.IsVisible).Select(b => b.Text)];

        private static void Select(Ribbon ribbon, string tab)
        {
            ribbon.Tabs.SelectedItem = ribbon.Tabs.Items.OfType<TabItem>().Single(t => (string)t.Header! == tab);
            Dispatcher.UIThread.RunJobs();
        }

        // a click with the mouse in the middle of the control, in the window or a dropdown's popup
        private static void Click(Control c)
        {
            var top = TopLevel.GetTopLevel(c)!;
            Point p = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), top)!.Value;
            top.MouseDown(p, MouseButton.Left);
            top.MouseUp(p, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        // two clicks in a row on the same spot: a double click
        private static void DoubleClick(Control c)
        {
            var top = TopLevel.GetTopLevel(c)!;
            Point p = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), top)!.Value;
            for (int i = 0; i < 2; i++)
            {
                top.MouseDown(p, MouseButton.Left);
                top.MouseUp(p, MouseButton.Left);
            }
            Dispatcher.UIThread.RunJobs();
        }

        // pressed on a handle, moved in steps to the left edge of another control, released there
        private static void Drag(Control handle, Control onto)
        {
            var top = TopLevel.GetTopLevel(handle)!;
            Point from = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), top)!.Value;
            Point to = onto.TranslatePoint(new Point(4, onto.Bounds.Height / 2), top)!.Value;
            top.MouseDown(from, MouseButton.Left);
            for (int i = 1; i <= 10; i++)
            {
                top.MouseMove(new Point(from.X + (to.X - from.X) * i / 10, from.Y + (to.Y - from.Y) * i / 10), RawInputModifiers.LeftMouseButton);
                Dispatcher.UIThread.RunJobs();
            }
            top.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        private static TextBlock Caption(RibbonGroup group) => group.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("ribbonCaption"));

        // an entry of an open dropdown, once its popup has laid it out (it shows)
        private static MenuItem Entry(Button dropdown, string header) => Entry(dropdown, header, m => m.Header as string == header);

        // a Recent file by its path, its tooltip (two files can share a name: on Windows the stock bins in Binaries are copies)
        private static MenuItem RecentEntry(Button dropdown, string path) => Entry(dropdown, path, m => ToolTip.GetTip(m) as string == path);

        private static MenuItem Entry(Button dropdown, string header, System.Func<MenuItem, bool> which)
        {
            var flyout = (MenuFlyout)dropdown.Flyout!;
            Assert.IsTrue(flyout.IsOpen);
            var entry = flyout.Items.OfType<MenuItem>().Single(which);
            for (int i = 0; i < 5 && TopLevel.GetTopLevel(entry) == null; i++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
            }
            Assert.IsNotNull(TopLevel.GetTopLevel(entry), header + " shows");
            return entry;
        }

        [TestMethod]
        public void RibbonShowsTheMenus()
        {
            string t5 = Stock("T5Binaries", "4239273.bin");
            string t7 = Stock("T7Binaries", "5168646.bin");
            s_session!.Dispatch(async () =>
            {
                var (vm, window) = NewWindow(new ClassicDesktopStyleApplicationLifetime());
                var ribbon = window.GetVisualDescendants().OfType<Ribbon>().Single();
                var menu = window.GetVisualDescendants().OfType<Menu>().Single();
                try
                {
                    // menus until the setting says otherwise
                    Assert.IsFalse(ribbon.IsVisible);
                    vm.Settings.ShowRibbon = true;
                    vm.SettingsChanged();
                    Dispatcher.UIThread.RunJobs();
                    Assert.IsTrue(ribbon.IsVisible);
                    Assert.IsFalse(menu.IsVisible);
                    CollectionAssert.AreEqual(new[] { "File", "Actions", "Tuning", "My Maps", "Realtime", "ECU", "Skin", "Help" },
                        ribbon.Tabs.Items.OfType<TabItem>().Select(t => (string)t.Header!).ToArray());

                    // T7Suite's File page: Projects is the Project submenu's items, Exit isn't there
                    Assert.IsTrue(await vm.OpenPlainFileAsync(t5, true));
                    Assert.IsTrue(await vm.OpenPlainFileAsync(t7, true));
                    Dispatcher.UIThread.RunJobs();
                    CollectionAssert.AreEqual(new[] { "File actions", "Options", "Projects" }, Groups(ribbon).Select(g => g.Caption).ToArray());
                    CollectionAssert.AreEqual(new[] { "Settings", "Lookup partnumber" }, Shown(Group(ribbon, "Options")));
                    var files = Group(ribbon, "File actions");
                    CollectionAssert.IsSubsetOf(new[] { "Open file...", "Recent ▾", "Save all", "Export to S19", "Generate Idc file" }, Shown(files));
                    var projects = Group(ribbon, "Projects").Buttons;
                    Assert.IsTrue(projects.Single(b => b.Text == "Create a project").IsEffectivelyEnabled);
                    Assert.IsFalse(projects.Single(b => b.Text == "Close project").IsEffectivelyEnabled);
                    Assert.IsFalse(Groups(ribbon).SelectMany(g => g.Buttons).Any(b => b.Text == "Exit"));
                    Save(window, "ribbon-file");

                    // too narrow for every group: the wide one folds into a button with its items in a dropdown, the ones before it stay
                    // open. The width from the groups' own, the fonts differ between systems: room for Checksum and Information, not Actions
                    Select(ribbon, "Actions");
                    window.Width = 5000;
                    Dispatcher.UIThread.RunJobs();
                    Assert.IsFalse(Groups(ribbon).Any(g => g.Folded));
                    var page = (RibbonPage)ribbon.Tabs.SelectedContent!;
                    var actions = Group(ribbon, "Actions");
                    window.Width = window.Width - page.Bounds.Width + Group(ribbon, "Checksum").FullWidth + Group(ribbon, "Information").FullWidth + actions.FullWidth - 1;
                    Dispatcher.UIThread.RunJobs();
                    Assert.IsTrue(actions.Folded);
                    Assert.IsFalse(Group(ribbon, "Checksum").Folded || Group(ribbon, "Information").Folded);
                    Assert.IsLessThanOrEqualTo(page.Bounds.Width, Groups(ribbon).Sum(g => g.Bounds.Width));
                    Save(window, "ribbon-actions");
                    Click(actions.FoldedButton);
                    // a click on an entry is a click on the menu item
                    Click(Entry(actions.FoldedButton, "View file in hex"));
                    Assert.IsInstanceOfType<HexViewerViewModel>(vm.SelectedViewer);
                    Assert.IsFalse(actions.FoldedButton.Flyout!.IsOpen);
                    window.Width = 1200;
                    Dispatcher.UIThread.RunJobs();

                    // a click on a button is a click on the menu item (Skin's: their Click handler)
                    Select(ribbon, "Skin");
                    Click(Group(ribbon, "Available skins").Buttons.Single(b => b.Text == "Dark"));
                    Assert.AreEqual(Avalonia.Styling.ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
                    Select(ribbon, "Realtime");
                    CollectionAssert.Contains(Shown(Group(ribbon, "CAN bus connection")), "Toggle realtime panel [Shift+F1]");
                    CollectionAssert.Contains(Shown(Group(ribbon, "Tuning in realtime")), "AFR maps ▾");
                    Save(window, "ribbon-realtime-dark");

                    // Help: every ECU's manuals in a group each, the release notes with the program's
                    Select(ribbon, "Help");
                    CollectionAssert.AreEqual(new[] { "T5", "T7", "T8", "Help on TxSuite" }, Groups(ribbon).Select(g => g.Caption).ToArray());
                    CollectionAssert.AreEqual(new[] { "User manual", "Trionic 5 documentation" }, Shown(Group(ribbon, "T5")));
                    CollectionAssert.AreEqual(new[] { "Release notes", "Check for updates", "About TxSuite" }, Shown(Group(ribbon, "Help on TxSuite")));

                    // the Tuning page: txlogger's map groups for T7
                    Select(ribbon, "Tuning");
                    var tuning = Groups(ribbon);
                    Assert.AreEqual("Diagnostics", tuning[0].Caption);
                    Assert.IsFalse(tuning[0].Folded);
                    Assert.IsLessThanOrEqualTo(ribbon.Bounds.Width, tuning.Sum(g => g.Bounds.Width));
                    Save(window, "ribbon-tuning");

                    // Recent drops down (it once opened empty), every time; a file of another ECU from it changes the pages to that ECU's
                    Select(ribbon, "File");
                    var recent = Group(ribbon, "File actions").Buttons.Single(b => b.Text == "Recent ▾");
                    Click(recent);
                    RecentEntry(recent, t7);
                    Click(recent);
                    Assert.IsFalse(recent.Flyout!.IsOpen);
                    Click(recent);
                    Click(RecentEntry(recent, t5));
                    Assert.IsFalse(recent.Flyout!.IsOpen);
                    for (int i = 0; i < 100 && vm.Binary?.FileName != t5; i++) await System.Threading.Tasks.Task.Delay(20);
                    Dispatcher.UIThread.RunJobs();
                    Assert.AreSame(vm.T5, vm.Module);
                    CollectionAssert.AreEqual(new[] { "Settings", "Lookup partnumber", "User library" }, Shown(Group(ribbon, "Options")));
                    CollectionAssert.DoesNotContain(Shown(Group(ribbon, "File actions")), "Export to S19");

                    // and back to menus
                    vm.Settings.ShowRibbon = false;
                    vm.SettingsChanged();
                    Dispatcher.UIThread.RunJobs();
                    Assert.IsFalse(ribbon.IsVisible);
                    Assert.IsTrue(menu.IsVisible);
                    window.Close();
                }
                finally
                {
                    vm.Settings.ShowRibbon = false;
                    Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
                }
                return true;
            }, default).GetAwaiter().GetResult();
        }

        [TestMethod]
        public void RibbonGroupsFoldMoveAndKeepPerEcu()
        {
            string t5 = Stock("T5Binaries", "4239273.bin");
            string t7 = Stock("T7Binaries", "5168646.bin");
            s_session!.Dispatch(async () =>
            {
                var (vm, window) = NewWindow(new ClassicDesktopStyleApplicationLifetime());
                var ribbon = window.GetVisualDescendants().OfType<Ribbon>().Single();
                RibbonGroup G(string caption) => Group(ribbon, caption);
                string[] Order() => [.. Groups(ribbon).Select(g => g.Caption!)];
                static string? Kept(string ecu, string name)
                {
                    using var key = SettingsKey.Open(SettingsKey.Program, $"Ribbon\\{ecu}\\File");
                    return key.GetValue(name) as string;
                }
                try
                {
                    vm.Settings.ShowRibbon = true;
                    vm.SettingsChanged();
                    window.Width = 5000;
                    Assert.IsTrue(await vm.OpenPlainFileAsync(t5, true));
                    Assert.IsTrue(await vm.OpenPlainFileAsync(t7, true));
                    Dispatcher.UIThread.RunJobs();
                    Select(ribbon, "File");
                    Assert.IsFalse(Groups(ribbon).Any(g => g.Folded));

                    // kept open beats folding when it doesn't fit: room for Projects or File actions, not both (from the groups' own widths,
                    // the fonts differ between systems)
                    double chrome = window.Width - ((RibbonPage)ribbon.Tabs.SelectedContent!).Bounds.Width;
                    window.Width = chrome + G("Projects").FullWidth + G("File actions").FullWidth - 1;
                    Dispatcher.UIThread.RunJobs();
                    Assert.IsTrue(G("Projects").Folded);
                    Assert.IsFalse(G("File actions").Folded);
                    DoubleClick(G("Projects").FoldedButton);
                    Assert.IsFalse(G("Projects").FoldedButton.Flyout!.IsOpen);
                    Assert.IsFalse(G("Projects").Folded);
                    Assert.IsTrue(G("File actions").Folded);
                    Assert.AreEqual("Open", Kept("T7", "Fold\\Projects"));
                    window.Width = 5000;
                    Dispatcher.UIThread.RunJobs();

                    // a double click on a caption folds a group that fits
                    DoubleClick(Caption(G("Options")));
                    Assert.IsTrue(G("Options").Folded);
                    Assert.AreEqual("Folded", Kept("T7", "Fold\\Options"));

                    // dragged by its caption in front of File actions, the order kept
                    Drag(Caption(G("Projects")), G("File actions"));
                    CollectionAssert.AreEqual(new[] { "Projects", "File actions", "Options" }, Order());
                    Assert.AreEqual("Projects\nFile actions\nOptions", Kept("T7", "Order"));
                    Assert.AreEqual(1, G("Projects").Opacity);
                    Save(window, "ribbon-layout");

                    // T5's File tab has a layout of its own: the menu's
                    Assert.IsTrue(await vm.OpenPlainFileAsync(t5, true));
                    Dispatcher.UIThread.RunJobs();
                    CollectionAssert.AreEqual(new[] { "File actions", "Options", "Projects" }, Order());
                    Assert.IsFalse(Groups(ribbon).Any(g => g.Folded));
                    Assert.IsTrue(await vm.OpenPlainFileAsync(t7, true));
                    Dispatcher.UIThread.RunJobs();
                    CollectionAssert.AreEqual(new[] { "Projects", "File actions", "Options" }, Order());
                    Assert.IsTrue(G("Options").Folded);

                    // a group's menu puts the tab back: the menu's order, folding only when it doesn't fit, nothing kept
                    G("Options").FoldedButton.ContextMenu!.Items.OfType<MenuItem>().Single().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Dispatcher.UIThread.RunJobs();
                    CollectionAssert.AreEqual(new[] { "File actions", "Options", "Projects" }, Order());
                    Assert.IsFalse(Groups(ribbon).Any(g => g.Folded));
                    Assert.IsNull(Kept("T7", "Order"));
                    Assert.IsNull(Kept("T7", "Fold\\Options"));
                    Assert.IsNull(Kept("T7", "Fold\\Projects"));
                    window.Close();
                }
                finally
                {
                    vm.Settings.ShowRibbon = false;
                    foreach (string ecu in new[] { "T5", "T7" })
                    {
                        using var key = SettingsKey.Open(SettingsKey.Program, $"Ribbon\\{ecu}\\File");
                        foreach (string name in key.GetValueNames()) key.DeleteValue(name);
                    }
                }
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
