using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommonSuite;

namespace SuiteApp.Controls;

/// <summary>
/// The old suites' DevExpress ribbon over the program's menu (Settings → Show ribbon bar instead of menus): a tab per menu, its items
/// as buttons three to a column, in groups. The menu stays the one source: a button shows its menu item's caption, state and
/// visibility, and a click on it is a click on the menu item (its Click handlers, then its command). A group is a section of the menu
/// between separators, captioned by Ribbon.Group on the separator (the first section's on the menu itself); a submenu with a
/// Ribbon.Group is a group of its own, any other submenu a dropdown. Ribbon.Hidden leaves an item out. Groups that don't fit fold into
/// a button that drops them down, as DevExpress' did; the user folds or keeps open a group with a double click and drags groups into
/// another order, kept per LayoutKey (the open file's ECU) and tab.
/// </summary>
public class Ribbon : UserControl
{
    public static readonly StyledProperty<Menu?> MenuProperty = AvaloniaProperty.Register<Ribbon, Menu?>(nameof(Menu));

    /// <summary>On a separator (or a menu): the caption of the group it starts. On a submenu: its items are a group of their own.</summary>
    public static readonly AttachedProperty<string?> GroupProperty = AvaloniaProperty.RegisterAttached<Ribbon, Control, string?>("Group");

    /// <summary>A menu item the ribbon leaves out (File > Exit: T7Suite's ribbon had none).</summary>
    public static readonly AttachedProperty<bool> HiddenProperty = AvaloniaProperty.RegisterAttached<Ribbon, Control, bool>("Hidden");

    public static string? GetGroup(Control c) => c.GetValue(GroupProperty);

    public static void SetGroup(Control c, string? value) => c.SetValue(GroupProperty, value);

    public static bool GetHidden(Control c) => c.GetValue(HiddenProperty);

    public static void SetHidden(Control c, bool value) => c.SetValue(HiddenProperty, value);

    /// <summary>Whose group layout the pages show and keep (the open file's ECU): Ribbon\&lt;key&gt;\&lt;tab&gt; in the program's settings.</summary>
    public static readonly StyledProperty<string?> LayoutKeyProperty = AvaloniaProperty.Register<Ribbon, string?>(nameof(LayoutKey));

    public Menu? Menu { get => GetValue(MenuProperty); set => SetValue(MenuProperty, value); }

    public string? LayoutKey { get => GetValue(LayoutKeyProperty); set => SetValue(LayoutKeyProperty, value); }

    /// <summary>A tab per menu; the selected one's page holds RibbonGroups.</summary>
    public TabControl Tabs { get; } = new();

    private bool m_refill;

    public Ribbon()
    {
        Content = Tabs;
        Tabs.SelectionChanged += (_, _) => Fill(Tabs.SelectedItem as TabItem);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MenuProperty) BuildTabs();
        else if (change.Property == LayoutKeyProperty)
        {
            // another ECU's layout: every page made again with it
            foreach (TabItem tab in Tabs.Items.OfType<TabItem>()) tab.Content = null;
            Fill(Tabs.SelectedItem as TabItem);
        }
    }

    // a tab per menu; its page is made when it's shown, and again when the menu's items change (Tuning and My Maps are built per file)
    private void BuildTabs()
    {
        Tabs.Items.Clear();
        foreach (MenuItem menu in Menu?.Items.OfType<MenuItem>() ?? [])
        {
            var tab = new TabItem { Header = Text(menu.Header), Tag = menu, Focusable = false };
            menu.Items.CollectionChanged += (_, _) =>
            {
                tab.Content = null;
                if (m_refill) return;
                // once for a menu built item by item
                m_refill = true;
                Dispatcher.UIThread.Post(() =>
                {
                    m_refill = false;
                    Fill(Tabs.SelectedItem as TabItem);
                });
            };
            Tabs.Items.Add(tab);
        }
        Tabs.SelectedIndex = 0;
    }

    private void Fill(TabItem? tab)
    {
        if (tab is not { Tag: MenuItem menu } || tab.Content != null) return;
        var page = new RibbonPage(LayoutKey is { } key ? $"Ribbon\\{key}\\{tab.Header}" : null);
        foreach (var (caption, items, submenu) in Groups(menu)) page.Children.Add(new RibbonGroup(caption, items, submenu));
        page.Restore();
        // the Tuning menu without a file
        Follow(page, menu, () => page.IsEnabled = menu.IsEnabled);
        tab.Content = page;
    }

    // the menu's sections between separators; a submenu with a Ribbon.Group is a group of its own, its section goes on after it
    private static IEnumerable<(string? Caption, List<MenuItem> Items, MenuItem? Submenu)> Groups(MenuItem menu)
    {
        string? caption = GetGroup(menu);
        var items = new List<MenuItem>();
        foreach (object? o in menu.Items)
        {
            if (o is Separator separator)
            {
                if (items.Count > 0) yield return (caption, items, null);
                (caption, items) = (GetGroup(separator), []);
            }
            else if (o is MenuItem item && !GetHidden(item))
            {
                if (GetGroup(item) is { } own && item.ItemCount > 0)
                {
                    if (items.Count > 0) yield return (caption, items, null);
                    items = [];
                    yield return (own, [.. item.Items.OfType<MenuItem>().Where(i => !GetHidden(i))], item);
                }
                else items.Add(item);
            }
        }
        if (items.Count > 0) yield return (caption, items, null);
    }

    /// <summary>A menu header as plain text: a single underscore is its access key, a double one a real underscore (MenuTextConverter).</summary>
    public static string Text(object? header) => Regex.Replace(header?.ToString() ?? "", "_(.)", "$1");

    /// <summary>
    /// Keeps a ribbon control in step with its menu item while it is shown: the menu lives as long as the window, a ribbon page only
    /// until it is made again.
    /// </summary>
    internal static void Follow(Control control, Control item, Action update)
    {
        void Changed(object? sender, AvaloniaPropertyChangedEventArgs e) => update();
        control.AttachedToVisualTree += (_, _) =>
        {
            item.PropertyChanged += Changed;
            update();
        };
        control.DetachedFromVisualTree += (_, _) => item.PropertyChanged -= Changed;
        update();
    }

    /// <summary>A submenu as a dropdown: its items as menu items again, made each time it opens (Recent changes).</summary>
    internal static MenuFlyout Dropdown(Func<IEnumerable<object?>> items) => new DropdownFlyout(items);

    // the items are made before it shows: added in its Opening event, the first time it opened it opened empty (its presenter, made
    // just before Opening, never showed them)
    private sealed class DropdownFlyout(Func<IEnumerable<object?>> items) : MenuFlyout
    {
        protected override bool ShowAtCore(Control placementTarget, bool showAtPointer = false)
        {
            if (!IsOpen)
            {
                Items.Clear();
                foreach (Control c in Mirrors(items())) Items.Add(c);
            }
            return base.ShowAtCore(placementTarget, showAtPointer);
        }
    }

    internal static IEnumerable<Control> Mirrors(IEnumerable<object?> items)
    {
        foreach (object? o in items)
        {
            if (o is Separator s)
            {
                var separator = new Separator();
                Follow(separator, s, () => separator.IsVisible = s.IsVisible);
                yield return separator;
            }
            else if (o is MenuItem m && !GetHidden(m)) yield return new MenuMirror(m);
        }
    }
}

/// <summary>How a group folds: when it doesn't fit, as the user folded it, or kept open by the user.</summary>
public enum RibbonFold { Auto, Folded, Open }

/// <summary>
/// A tab's page: its groups in a row, the ones that don't fit folded. DevExpress folded from the right; here a group stays open while it
/// fits with the ones after it folded, so a wide group in the middle folds and the narrower ones after it don't. A double click on a
/// group (its caption or the space around its buttons) folds it, on a folded one keeps it open; dragged there, a group moves. Folds and
/// order are kept per ECU and tab in the program's settings (Ribbon\&lt;ECU&gt;\&lt;tab&gt;: Fold\&lt;group&gt;, Order).
/// </summary>
public class RibbonPage : Panel
{
    private readonly string? m_layout;
    private List<RibbonGroup> m_natural = [];
    private (RibbonGroup Group, Point Start)? m_press;
    private bool m_dragging;
    private TopLevel? m_top;

    /// <param name="layout">Where the page keeps its groups' folds and order, null for nowhere.</param>
    public RibbonPage(string? layout = null)
    {
        m_layout = layout;
        MinHeight = RibbonGroup.FullHeight;
        // before the buttons: a folded group's button would drop down, and a press the open dropdown dismissed still counts
        AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>The groups' keys (their captions, a repeated one numbered), then the folds and order kept for this ECU and tab.</summary>
    public void Restore()
    {
        m_natural = [.. Children.OfType<RibbonGroup>()];
        foreach (var same in m_natural.GroupBy(g => g.Caption ?? ""))
            foreach (var (g, i) in same.Select((g, i) => (g, i))) g.Key = i == 0 ? same.Key : $"{same.Key}#{i + 1}";
        if (m_layout == null) return;
        using var key = SettingsKey.Open(SettingsKey.Program, m_layout);
        foreach (RibbonGroup g in m_natural) g.Fold = Enum.TryParse(key.GetValue("Fold\\" + g.Key) as string, out RibbonFold fold) ? fold : RibbonFold.Auto;
        if (key.GetValue("Order") is not string order) return;
        // the kept order; a group it doesn't know (the menu grew) goes after the one the menu has before it
        var byKey = m_natural.ToDictionary(g => g.Key);
        var ordered = order.Split('\n').Where(byKey.ContainsKey).Select(k => byKey[k]).Distinct().ToList();
        for (int i = 0; i < m_natural.Count; i++)
            if (!ordered.Contains(m_natural[i])) ordered.Insert(i == 0 ? 0 : ordered.IndexOf(m_natural[i - 1]) + 1, m_natural[i]);
        Children.Clear();
        Children.AddRange(ordered);
    }

    /// <summary>The menu's order and every group folding only when it doesn't fit, as nothing was ever kept.</summary>
    public void Reset()
    {
        foreach (RibbonGroup g in m_natural) g.Fold = RibbonFold.Auto;
        Children.Clear();
        Children.AddRange(m_natural);
        Save();
        InvalidateMeasure();
    }

    private void Save()
    {
        if (m_layout == null) return;
        using var key = SettingsKey.Open(SettingsKey.Program, m_layout);
        var groups = Children.OfType<RibbonGroup>().ToList();
        foreach (RibbonGroup g in groups)
            if (g.Fold == RibbonFold.Auto) key.DeleteValue("Fold\\" + g.Key);
            else key.SetValue("Fold\\" + g.Key, g.Fold);
        // only a moved order is kept: a menu that grows puts its new groups in their places
        if (groups.SequenceEqual(m_natural)) key.DeleteValue("Order");
        else key.SetValue("Order", string.Join('\n', groups.Select(g => g.Key)));
    }

    // the group a press is on, not on one of its buttons (they run their menu items)
    private static RibbonGroup? HandleOf(object? source)
    {
        for (var v = source as Visual; v != null; v = v.GetVisualParent())
        {
            if (v is RibbonButton) return null;
            if (v is RibbonGroup g) return g;
        }
        return null;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        m_press = null;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || HandleOf(e.Source) is not { } group) return;
        if (e.ClickCount == 2)
        {
            Toggle(group);
            // a folded group's button doesn't drop down again
            e.Handled = true;
            return;
        }
        m_press = (group, e.GetPosition(this));
    }

    // a double click folds an open group and keeps a folded one open
    private void Toggle(RibbonGroup group)
    {
        group.Fold = group.Folded ? RibbonFold.Open : RibbonFold.Folded;
        group.FoldedButton.Flyout?.Hide();
        Save();
        InvalidateMeasure();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        m_top = TopLevel.GetTopLevel(this);
        m_top?.AddHandler(PointerPressedEvent, OnWindowPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        m_top?.RemoveHandler(PointerPressedEvent, OnWindowPressed);
        m_top = null;
    }

    // a folded group's open dropdown lays its dismiss overlay over the window: a double click's second press on the group's button lands
    // there, not on the button
    private void OnWindowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 2 || e.Source is Visual v && this.IsVisualAncestorOf(v)) return;
        if (Children.OfType<RibbonGroup>().FirstOrDefault(g => g.IsVisible && g.Folded && new Rect(g.FoldedButton.Bounds.Size).Contains(e.GetPosition(g.FoldedButton))) is not { } group) return;
        Toggle(group);
        e.Handled = true;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (m_press is not { } press) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            EndDrag();
            return;
        }
        Point p = e.GetPosition(this);
        if (!m_dragging)
        {
            if (Math.Abs(p.X - press.Start.X) < 6) return;
            // the page takes the pointer: a folded group's button neither clicks nor drops down
            m_dragging = true;
            press.Group.FoldedButton.Flyout?.Hide();
            press.Group.Opacity = 0.6;
            e.Pointer.Capture(this);
        }
        // before the first other group whose middle is right of the pointer
        RibbonGroup? before = Children.OfType<RibbonGroup>().FirstOrDefault(g => g != press.Group && g.IsVisible && g.Bounds.X + g.Bounds.Width / 2 > p.X);
        int from = Children.IndexOf(press.Group), to = before == null ? Children.Count - 1 : Children.IndexOf(before);
        if (to > from && before != null) to--;
        if (to != from) Children.Move(from, to);
        e.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (m_dragging)
        {
            e.Handled = true;
            e.Pointer.Capture(null);
        }
        EndDrag();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndDrag();
    }

    private void EndDrag()
    {
        if (m_dragging && m_press is { } press)
        {
            press.Group.Opacity = 1;
            Save();
        }
        m_dragging = false;
        m_press = null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var groups = Children.OfType<RibbonGroup>().Where(g => g.IsVisible).ToList();
        foreach (RibbonGroup g in groups) g.Measure(Size.Infinity);
        // the kept-open ones open and the rest folded, then the automatic ones open from the left while they fit; kept-open ones that
        // still don't fit fold from the right
        var folded = groups.ToDictionary(g => g, g => g.Fold != RibbonFold.Open);
        double width = groups.Sum(g => folded[g] ? g.FoldedWidth : g.FullWidth);
        foreach (RibbonGroup g in groups.Where(g => g.Fold == RibbonFold.Auto && width + g.FullWidth - g.FoldedWidth <= availableSize.Width))
        {
            width += g.FullWidth - g.FoldedWidth;
            folded[g] = false;
        }
        foreach (RibbonGroup g in Enumerable.Reverse(groups).Where(g => g.Fold == RibbonFold.Open))
            if (width > availableSize.Width)
            {
                width -= g.FullWidth - g.FoldedWidth;
                folded[g] = true;
            }
        foreach (RibbonGroup g in groups.Where(g => g.Folded != folded[g]))
        {
            g.Folded = folded[g];
            g.InvalidateArrange();
        }
        return new Size(width, RibbonGroup.FullHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        foreach (RibbonGroup g in Children.OfType<RibbonGroup>().Where(g => g.IsVisible))
        {
            double width = g.Folded ? g.FoldedWidth : g.FullWidth;
            g.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }
        return finalSize;
    }
}

/// <summary>A ribbon group: its buttons over its caption, or folded into one button when the page is too narrow.</summary>
public class RibbonGroup : Panel
{
    public const double RowHeight = 22, CaptionHeight = 16;

    public const double FullHeight = RibbonColumns.Rows * RowHeight + CaptionHeight + 4;

    private readonly Grid m_full;
    private readonly Button m_folded;
    private readonly MenuItem? m_submenu;

    public string? Caption { get; }

    public IReadOnlyList<RibbonButton> Buttons { get; }

    /// <summary>Folded into its button: set by the page as it measures (only the arrange follows it).</summary>
    public bool Folded { get; internal set; }

    /// <summary>How the user wants it folded (a double click), kept per ECU and tab.</summary>
    public RibbonFold Fold { get; set; }

    /// <summary>The group in the kept layout: its caption, numbered when the page has it twice.</summary>
    public string Key { get; internal set; } = "";

    private const string HandleTip = "Double-click to fold this group, or a folded one to keep it open; drag it to move it. Right-click to put this tab's groups back as they were.";

    public double FullWidth => m_full.DesiredSize.Width;

    public double FoldedWidth => m_folded.DesiredSize.Width;

    /// <summary>The button the group folds into.</summary>
    public Button FoldedButton => m_folded;

    /// <param name="submenu">The submenu the group is made of (shown while it is).</param>
    public RibbonGroup(string? caption, List<MenuItem> items, MenuItem? submenu)
    {
        Caption = caption;
        m_submenu = submenu;
        Buttons = [.. items.Select(i => new RibbonButton(i))];
        var columns = new RibbonColumns { Margin = new Thickness(3, 2, 3, 0) };
        columns.Children.AddRange(Buttons);
        // the caption across the group: something to double-click and to drag it by
        var text = new TextBlock
        {
            Text = caption, Height = CaptionHeight, TextAlignment = TextAlignment.Center, Background = Brushes.Transparent, Classes = { "ribbonCaption" },
            ContextMenu = ResetMenu(), [ToolTip.TipProperty] = HandleTip,
        };
        var line = new Border { Width = 1, Margin = new Thickness(0, 4), Classes = { "ribbonLine" } };
        Grid.SetRow(text, 1);
        Grid.SetColumn(line, 1);
        Grid.SetRowSpan(line, 2);
        m_full = new Grid { ColumnDefinitions = new("Auto,Auto"), RowDefinitions = new("*,Auto"), Height = FullHeight, ClipToBounds = true, Children = { columns, text, line } };
        // DevExpress' folded group: its caption and an arrow, its items in a dropdown
        m_folded = new Button
        {
            Classes = { "ribbonGroup" }, Focusable = false, ClipToBounds = true, Height = FullHeight,
            Content = new StackPanel { Children = { new TextBlock { Text = caption ?? "" }, new TextBlock { Text = "▾", HorizontalAlignment = HorizontalAlignment.Center } } },
            Flyout = Ribbon.Dropdown(() => items), ContextMenu = ResetMenu(), [ToolTip.TipProperty] = HandleTip,
        };
        // the space around the buttons is a handle too
        Background = Brushes.Transparent;
        Children.Add(m_full);
        Children.Add(m_folded);
        foreach (RibbonButton b in Buttons) b.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) Update(); };
        if (submenu != null) Ribbon.Follow(this, submenu, Update);
        Update();
    }

    private ContextMenu ResetMenu()
    {
        var reset = new MenuItem { Header = "Reset this tab's groups" };
        reset.Click += (_, _) => (Parent as RibbonPage)?.Reset();
        return new ContextMenu { Items = { reset } };
    }

    // shown while one of its buttons is (and its submenu)
    private void Update()
    {
        IsVisible = (m_submenu?.IsVisible ?? true) && Buttons.Any(b => b.IsVisible);
        IsEnabled = m_submenu?.IsEnabled ?? true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        m_full.Measure(Size.Infinity);
        m_folded.Measure(Size.Infinity);
        return new Size(Folded ? FoldedWidth : FullWidth, FullHeight);
    }

    // the other form at no size, clipped: out of sight and out of reach of the pointer
    protected override Size ArrangeOverride(Size finalSize)
    {
        m_full.Arrange(Folded ? default : new Rect(finalSize));
        m_folded.Arrange(Folded ? new Rect(finalSize) : default);
        return finalSize;
    }
}

/// <summary>A group's buttons column by column, three to a column as DevExpress' small ribbon items; a shorter column spreads them out.</summary>
public class RibbonColumns : Panel
{
    public const int Rows = 3;

    private List<List<Control>> Columns() =>
        [.. Children.Where(c => c.IsVisible).Select((c, i) => (c, i)).GroupBy(x => x.i / Rows, x => x.c).Select(g => g.ToList())];

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (Control c in Children) c.Measure(Size.Infinity);
        return new Size(Columns().Sum(column => column.Max(c => c.DesiredSize.Width)), Rows * RibbonGroup.RowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        foreach (List<Control> column in Columns())
        {
            double width = column.Max(c => c.DesiredSize.Width), slot = finalSize.Height / column.Count;
            for (int i = 0; i < column.Count; i++)
                column[i].Arrange(new Rect(x, i * slot + (slot - RibbonGroup.RowHeight) / 2, width, RibbonGroup.RowHeight));
            x += width;
        }
        return finalSize;
    }
}

/// <summary>
/// A menu item as a ribbon button: its caption (with its shortcut, as T7Suite's ribbon captions had it) and state. A click on it is a
/// click on the menu item; a submenu drops down.
/// </summary>
public class RibbonButton : Button
{
    // a TextBlock, not a string: the button would take a string's underscore for an access key (MAFCal.m_RedundantAirMap)
    private readonly TextBlock m_text = new();

    public MenuItem Item { get; }

    protected override Type StyleKeyOverride => typeof(Button);

    public RibbonButton(MenuItem item)
    {
        Item = item;
        Classes.Add("ribbonItem");
        Focusable = false;
        Content = m_text;
        Ribbon.Follow(this, item, Update);
    }

    /// <summary>What the button says.</summary>
    public string Text => m_text.Text ?? "";

    private void Update()
    {
        bool submenu = Item.ItemCount > 0;
        m_text.Text = Ribbon.Text(Item.Header) + (Item.InputGesture is { } key ? $" [{key}]" : "") + (submenu ? " ▾" : "");
        IsVisible = Item.IsVisible;
        IsEnabled = Item.IsEnabled;
        ToolTip.SetTip(this, ToolTip.GetTip(Item));
        // its command for the enabled state only: the click goes to the menu item
        CommandParameter = submenu ? null : Item.CommandParameter;
        Command = submenu ? null : Item.Command;
        if (submenu) Flyout ??= Ribbon.Dropdown(() => Item.Items);
        else Flyout = null;
    }

    protected override void OnClick()
    {
        if (Flyout != null) base.OnClick();
        else Item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }
}

/// <summary>A menu item again in a ribbon dropdown: its caption and state; a click on it is a click on the menu item.</summary>
public class MenuMirror : MenuItem
{
    private readonly MenuItem m_item;

    protected override Type StyleKeyOverride => typeof(MenuItem);

    public MenuMirror(MenuItem item)
    {
        m_item = item;
        foreach (Control c in Ribbon.Mirrors(item.Items)) Items.Add(c);
        Ribbon.Follow(this, item, Update);
    }

    private void Update()
    {
        Header = m_item.Header;
        InputGesture = m_item.InputGesture;
        IsVisible = m_item.IsVisible;
        IsEnabled = m_item.IsEnabled;
        ToolTip.SetTip(this, ToolTip.GetTip(m_item));
        CommandParameter = m_item.CommandParameter;
        Command = m_item.Command;
    }

    // MenuItem.OnClick runs the command; here the menu item's click does, with its Click handlers
    protected override void OnClick(RoutedEventArgs e)
    {
        if (ItemCount > 0) return;
        m_item.RaiseEvent(new RoutedEventArgs(ClickEvent));
        e.Handled = true;
    }
}
