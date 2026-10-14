using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;

namespace SuiteApp.Controls;

/// <summary>
/// The old suites' DevExpress ribbon over the program's menu (Settings → Show ribbon bar instead of menus): a tab per menu, its items
/// as buttons three to a column, in groups. The menu stays the one source: a button shows its menu item's caption, state and
/// visibility, and a click on it is a click on the menu item (its Click handlers, then its command). A group is a section of the menu
/// between separators, captioned by Ribbon.Group on the separator (the first section's on the menu itself); a submenu with a
/// Ribbon.Group is a group of its own, any other submenu a dropdown. Ribbon.Hidden leaves an item out. Groups that don't fit fold into
/// a button that drops them down, as DevExpress' did.
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

    public Menu? Menu { get => GetValue(MenuProperty); set => SetValue(MenuProperty, value); }

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

    private static void Fill(TabItem? tab)
    {
        if (tab is not { Tag: MenuItem menu } || tab.Content != null) return;
        var page = new RibbonPage();
        foreach (var (caption, items, submenu) in Groups(menu)) page.Children.Add(new RibbonGroup(caption, items, submenu));
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

/// <summary>
/// A tab's page: its groups in a row, the ones that don't fit folded. DevExpress folded from the right; here a group stays open while it
/// fits with the ones after it folded, so a wide group in the middle folds and the narrower ones after it don't.
/// </summary>
public class RibbonPage : Panel
{
    public RibbonPage() => MinHeight = RibbonGroup.FullHeight;

    protected override Size MeasureOverride(Size availableSize)
    {
        var groups = Children.OfType<RibbonGroup>().Where(g => g.IsVisible).ToList();
        foreach (RibbonGroup g in groups) g.Measure(Size.Infinity);
        double width = groups.Sum(g => g.FoldedWidth);
        foreach (RibbonGroup g in groups)
        {
            bool fits = width + g.FullWidth - g.FoldedWidth <= availableSize.Width;
            if (fits) width += g.FullWidth - g.FoldedWidth;
            if (g.Folded == fits)
            {
                g.Folded = !fits;
                g.InvalidateArrange();
            }
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
        var text = new TextBlock { Text = caption, Height = CaptionHeight, HorizontalAlignment = HorizontalAlignment.Center, Classes = { "ribbonCaption" } };
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
            Flyout = Ribbon.Dropdown(() => items),
        };
        Children.Add(m_full);
        Children.Add(m_folded);
        foreach (RibbonButton b in Buttons) b.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) Update(); };
        if (submenu != null) Ribbon.Follow(this, submenu, Update);
        Update();
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
