using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace T7App.Views;

/// <summary>Pick one of a list (frmSectionSelection: "Select logfile section to display"); the index, or null.</summary>
public partial class ChoiceWindow : Window
{
    public ChoiceWindow() => InitializeComponent();

    public ChoiceWindow(string title, IReadOnlyList<string> items) : this()
    {
        Title = title;
        List.ItemsSource = items;
        List.SelectedIndex = 0;
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(List.SelectedIndex >= 0 ? List.SelectedIndex : null);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
