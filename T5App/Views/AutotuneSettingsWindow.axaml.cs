using Avalonia.Controls;
using Avalonia.Interactivity;

namespace T5App.Views;

public partial class AutotuneSettingsWindow : Window
{
    public AutotuneSettingsWindow() => InitializeComponent();

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
