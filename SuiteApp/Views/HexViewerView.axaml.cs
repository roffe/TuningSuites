using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

public partial class HexViewerView : UserControl
{
    public HexViewerView()
    {
        InitializeComponent();
        Editor.Caret.LocationChanged += (_, _) => (DataContext as HexViewerViewModel)?.CaretAt(Editor.Caret.Location.ByteIndex);
        // HexViewer's hexBox1_DragDrop: one file dropped opens in the view (also in a floating window), several do nothing
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private Window? Owner => TopLevel.GetTopLevel(this) as Window;

    // the main window's view model, also from a floating document window
    private MainWindowViewModel? Main => Owner?.DataContext as MainWindowViewModel ?? (Owner?.Owner as Window)?.DataContext as MainWindowViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is HexViewerViewModel vm)
        {
            Editor.Document = vm.Document;
            vm.CaretAt(0);
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e) => (DataContext as HexViewerViewModel)?.Save();

    // Copy or None, never the source's Move, as the main window's
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = SuiteMainView.DroppedFiles(e) is [_] ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        OnDragOver(sender, e);
        // once the drop returns, as the main window's
        if (SuiteMainView.DroppedFiles(e) is [var file] && DataContext is HexViewerViewModel vm && Main is { } main)
            Dispatcher.UIThread.Post(() => _ = main.OpenInHexViewerAsync(vm, file));
    }
}
