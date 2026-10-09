using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using T7;

namespace T7App.Views;

/// <summary>frmImportResults: Map / Result per package entry, Success green, Fail red.</summary>
public partial class ImportResultsWindow : Window
{
    public ImportResultsWindow() => InitializeComponent();

    public ImportResultsWindow(IReadOnlyList<PackageResult> results) : this() => Grid.ItemsSource = results;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
