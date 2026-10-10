using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SuiteApp.Services;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

/// <summary>The realtime table with its layout and symbol buttons (frmMain's Free logging, T5Suite's User defined tab).</summary>
public partial class FreeLoggingView : UserControl
{
    public FreeLoggingView()
    {
        InitializeComponent();
        // the grid moves its selection on Up / Down before a bubbling handler sees them
        Grid.AddHandler(KeyDownEvent, OnGridMoveKey, RoutingStrategies.Tunnel);
    }

    private RealtimeViewModel Vm => (RealtimeViewModel)DataContext!;

    private Window? Owner => TopLevel.GetTopLevel(this) as Window;

    private async void OnSaveLayout(object? sender, RoutedEventArgs e)
    {
        if (Owner is { } w && await Dialogs.SaveFile(w, "Realtime layout files", Vm.LayoutExtension) is { } file) Vm.SaveLayout(file);
    }

    private async void OnLoadLayout(object? sender, RoutedEventArgs e)
    {
        if (Owner is { } w && await Dialogs.OpenFile(w, "Realtime layout files", "*." + Vm.LayoutExtension) is { } file) Vm.LoadLayout(file);
    }

    private async void OnAdd(object? sender, RoutedEventArgs e)
    {
        if (Owner is not { } w) return;
        var edit = new RealtimeSymbolEdit { Names = Vm.SymbolNames, IsNew = true };
        if (await new RealtimeSymbolWindow { DataContext = edit }.ShowDialog<bool>(w) != true) return;
        if (Vm.Lookup(edit.Name) is not { } symbol)
        {
            await Dialogs.Info(w, $"Symbol {edit.Name} does not exist in this file");
            return;
        }
        edit.ApplyTo(symbol);
        Vm.Add(symbol);
    }

    private async void OnEdit(object? sender, RoutedEventArgs e)
    {
        if (Owner is not { } w || Vm.SelectedRow is not { } row) return;
        var edit = RealtimeSymbolEdit.From(row.Symbol);
        if (await new RealtimeSymbolWindow { DataContext = edit }.ShowDialog<bool>(w) != true) return;
        edit.ApplyTo(row.Symbol);
        Vm.Edited(row);
    }

    private void OnGridDoubleTapped(object? sender, TappedEventArgs e) => OnEdit(sender, e);

    private void OnRemove(object? sender, RoutedEventArgs e) => Vm.Remove(Grid.SelectedItems.OfType<RealtimeRow>());

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || Vm.SelectedRow is null) return;
        Vm.Remove(Grid.SelectedItems.OfType<RealtimeRow>());
        e.Handled = true;
    }

    // Ctrl+Up / Ctrl+Down: the row moves (not while a cell is edited)
    private void OnGridMoveKey(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Key is not (Key.Up or Key.Down) || e.Source is TextBox || Vm.SelectedRow is not { } row) return;
        Vm.Move(row, e.Key == Key.Up ? -1 : 1);
        Grid.SelectedItem = row;
        e.Handled = true;
    }
}
