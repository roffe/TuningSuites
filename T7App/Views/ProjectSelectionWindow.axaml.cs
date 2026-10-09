using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommonSuite;

namespace T7App.Views;

/// <summary>frmProjectSelection: closes with the chosen project's name, or null.</summary>
public partial class ProjectSelectionWindow : Window
{
    public ProjectSelectionWindow() => InitializeComponent();

    public ProjectSelectionWindow(IEnumerable<ProjectSummary> projects) : this() => Grid.ItemsSource = projects;

    private void OnOk(object? sender, RoutedEventArgs e) => Close((Grid.SelectedItem as ProjectSummary)?.Projectname);

    private void OnDoubleTapped(object? sender, TappedEventArgs e) => OnOk(sender, e);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
