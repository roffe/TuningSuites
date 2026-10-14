using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CommonSuite;

namespace SuiteApp.Views;

/// <summary>
/// VS Code's quick open (Ctrl+P: a symbol of the open bin) and command palette (Ctrl+Shift+P: any menu command), Cmd on macOS.
/// The commands are read from the menus each time, so whatever a menu holds now (the Tuning and My Maps menus are built at run
/// time, a new item in an app's MainWindow.axaml) is in the palette without a list to keep.
/// </summary>
public partial class SuiteMainView
{
    private void OnPaletteKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.P || (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0) return;
        e.Handled = true;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) ShowCommandPalette();
        else ShowSymbolPalette();
    }

    /// <summary>The open palette (tests).</summary>
    public CommandPalette? Palette { get; private set; }

    public void ShowSymbolPalette()
    {
        if (Vm.Binary is not { } bin) return;
        // the symbols a double click opens, by the name the list shows
        ShowPalette("Open a symbol", bin.Symbols.Cast<SymbolHelper>().Where(sh => sh.Flash_start_address != 0 || sh.Start_address != 0)
            .OrderBy(sh => sh.SmartVarname, System.StringComparer.OrdinalIgnoreCase)
            .Select(sh => new PaletteEntry(sh.SmartVarname, () => Vm.OpenSymbolCommand.Execute(sh))).ToList());
    }

    public void ShowCommandPalette()
    {
        if (this.FindLogicalDescendantOfType<Menu>() is { } menu) ShowPalette("Run a menu command", MenuCommands(menu.Items, "").ToList());
    }

    // the menu's enabled leaves as "Menu > Submenu > Item", Recent's files too
    private static IEnumerable<PaletteEntry> MenuCommands(ItemCollection items, string path)
    {
        foreach (MenuItem item in items.OfType<MenuItem>().Where(m => m.IsVisible && m.IsEnabled))
        {
            // a header's single underscore is its access key, a double one a real underscore (MenuTextConverter)
            string text = path + Regex.Replace(item.Header?.ToString() ?? "", "_(.)", "$1");
            if (item.ItemsSource != null || item.Items.Count > 0)
            {
                foreach (PaletteEntry entry in MenuCommands(item.Items, text + " > ")) yield return entry;
            }
            else if (item.Command?.CanExecute(item.CommandParameter) != false)
                // as a click: its Click handlers, then its Command
                yield return new PaletteEntry(text, () => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)));
        }
    }

    private void ShowPalette(string watermark, List<PaletteEntry> entries)
    {
        if (Palette != null) return;
        var palette = new CommandPalette(watermark, entries);
        // at the top of the workspace, under the menu, as VS Code's
        var popup = new Popup
        {
            Child = palette, PlacementTarget = Space, Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = PopupAnchor.Top, PlacementGravity = PopupGravity.Bottom, VerticalOffset = 8, IsLightDismissEnabled = true,
        };
        IInputElement? before = Host.FocusManager?.GetFocusedElement();
        PaletteEntry? picked = null;
        palette.Done += entry =>
        {
            picked = entry;
            popup.IsOpen = false;
        };
        popup.Opened += (_, _) => palette.FocusFilter();
        popup.Closed += (_, _) =>
        {
            LogicalChildren.Remove(popup);
            Palette = null;
            before?.Focus();
            // after the popup is gone: a command may open a dialog or a document that takes the focus
            if (picked != null) Dispatcher.UIThread.Post(picked.Run);
        };
        LogicalChildren.Add(popup);
        Palette = palette;
        popup.IsOpen = true;
    }
}
