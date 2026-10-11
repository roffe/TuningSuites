using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace TxSuite;

/// <summary>Settings with a tab for what the suites share and one per ECU (TxSettingsViewModel); Ok saves all of them.</summary>
public partial class TxSettingsWindow : Window
{
    public TxSettingsWindow()
    {
        InitializeComponent();
        // as big as the first tab (Common, the biggest) for every tab: sized to each, the dialog jumped at every tab change
        Opened += (_, _) => SizeToContent = SizeToContent.Manual;
    }

    private TxSettingsViewModel Vm => (TxSettingsViewModel)DataContext!;

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a project folder" });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) Vm.Common.ProjectFolder = path;
    }

    // frmSettings' "Autotune settings": its Ok hands the values back to Settings, whose Ok saves them (T5Suite, T7Suite)
    private async void OnT5Autotune(object? sender, RoutedEventArgs e)
    {
        var edit = Vm.NewT5Autotune();
        if (await new T5App.Views.AutotuneSettingsWindow { DataContext = edit }.ShowDialog<bool>(this)) Vm.T5Autotune = edit;
    }

    private async void OnT7Autotune(object? sender, RoutedEventArgs e)
    {
        var edit = Vm.NewT7Autotune();
        if (await new T7App.Views.AutotuneSettingsWindow { DataContext = edit }.ShowDialog<bool>(this)) Vm.T7Autotune = edit;
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
