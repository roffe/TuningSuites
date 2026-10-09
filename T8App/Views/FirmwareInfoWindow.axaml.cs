using Avalonia.Controls;
using Avalonia.Interactivity;
using T8SuitePro;

namespace T8App.Views;

public partial class FirmwareInfoWindow : Window
{
    public FirmwareInfoWindow() => InitializeComponent();

    // the browser reads the file again, as T8Suite did
    private async void OnFlashBlocks(object? sender, RoutedEventArgs e)
    {
        if (Owner is Window { DataContext: ViewModels.T8MainWindowViewModel { Binary: { } bin } })
            await new FlashBlocksWindow(FirmwareInfo.FlashBlocks(bin.FileName)).ShowDialog(this);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
