using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using T8SuitePro;

namespace T8App.Views;

public partial class FlashBlocksWindow : Window
{
    public FlashBlocksWindow() => InitializeComponent();

    public FlashBlocksWindow(IReadOnlyList<FlashBlockRow> rows) : this() => Grid.ItemsSource = rows;

    private void OnOk(object? sender, RoutedEventArgs e) => Close();
}
