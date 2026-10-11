using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SuiteApp.Views;

/// <summary>One line of the palette: what it shows and what picking it does.</summary>
public sealed record PaletteEntry(string Text, Action Run)
{
    public override string ToString() => Text;
}

/// <summary>
/// VS Code's quick open / command palette: a filter box over a list, every entry containing the typed text (any case). Up / Down
/// move, Enter or a click picks, Escape closes. The main window hosts it in a popup (Ctrl+P symbols, Ctrl+Shift+P menu commands).
/// </summary>
public partial class CommandPalette : UserControl
{
    private readonly IReadOnlyList<PaletteEntry> m_entries = [];

    /// <summary>Picked (with the entry) or dismissed (null); the host closes the popup, then runs the entry.</summary>
    public event Action<PaletteEntry?>? Done;

    public CommandPalette() => InitializeComponent();

    public CommandPalette(string watermark, IReadOnlyList<PaletteEntry> entries) : this()
    {
        m_entries = entries;
        Filter.PlaceholderText = watermark;
        Filter.TextChanged += (_, _) => Show();
        Filter.AddHandler(KeyDownEvent, OnFilterKey, RoutingStrategies.Tunnel);
        Show();
    }

    public IReadOnlyList<PaletteEntry> Shown => (IReadOnlyList<PaletteEntry>)Results.ItemsSource!;

    public void FocusFilter() => Filter.Focus();

    private void Show()
    {
        string text = Filter.Text ?? "";
        Results.ItemsSource = m_entries.Where(e => e.Text.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        Results.SelectedIndex = Shown.Count > 0 ? 0 : -1;
    }

    private void OnFilterKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down or Key.Up or Key.PageDown or Key.PageUp when Shown.Count > 0:
                int step = e.Key switch { Key.Down => 1, Key.Up => -1, Key.PageDown => 15, _ => -15 };
                Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + step, 0, Shown.Count - 1);
                if (Results.SelectedItem != null) Results.ScrollIntoView(Results.SelectedItem);
                break;
            case Key.Enter:
                if (Results.SelectedItem is PaletteEntry entry) Done?.Invoke(entry);
                break;
            case Key.Escape:
                Done?.Invoke(null);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // a click on a row picks it (not one on the scroll bar)
    private void OnResultTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Avalonia.Visual)?.FindAncestorOfType<ListBoxItem>(true)?.DataContext is PaletteEntry entry) Done?.Invoke(entry);
    }
}
