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

        // an entry of an open dropdown, once its popup has laid it out (it shows)
        private static MenuItem Entry(Button dropdown, string header)
        {
            var flyout = (MenuFlyout)dropdown.Flyout!;
            Assert.IsTrue(flyout.IsOpen);
            var entry = flyout.Items.OfType<MenuItem>().Single(m => m.Header as string == header);
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

                    // too narrow for every group: the wide one folds into a button with its items in a dropdown, the ones after it fit
                    Select(ribbon, "Actions");
                    var actions = Group(ribbon, "Actions");
                    Assert.IsTrue(actions.Folded);
                    Assert.IsFalse(Groups(ribbon).Except([actions]).Any(g => g.Folded));
                    Assert.IsLessThanOrEqualTo(ribbon.Bounds.Width, Groups(ribbon).Sum(g => g.Bounds.Width));
                    Save(window, "ribbon-actions");
                    Click(actions.FoldedButton);
                    // a click on an entry is a click on the menu item
                    Click(Entry(actions.FoldedButton, "View file in hex"));
                    Assert.IsInstanceOfType<HexViewerViewModel>(vm.SelectedViewer);
                    Assert.IsFalse(actions.FoldedButton.Flyout!.IsOpen);
                    window.Width = 5000;
                    Dispatcher.UIThread.RunJobs();
                    Assert.IsFalse(Groups(ribbon).Any(g => g.Folded));
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
                    Entry(recent, System.IO.Path.GetFileName(t7));
                    Click(recent);
                    Assert.IsFalse(recent.Flyout!.IsOpen);
                    Click(recent);
                    Click(Entry(recent, System.IO.Path.GetFileName(t5)));
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
    }
}
