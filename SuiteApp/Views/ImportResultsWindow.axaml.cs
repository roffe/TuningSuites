using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommonSuite;

namespace SuiteApp.Views;

/// <summary>frmImportResults: Map / Result per package entry, Success green, Fail red.</summary>
public partial class ImportResultsWindow : Window
{
    public ImportResultsWindow() => InitializeComponent();

    public ImportResultsWindow(IReadOnlyList<PackageResult> results) : this() => Grid.ItemsSource = results;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
