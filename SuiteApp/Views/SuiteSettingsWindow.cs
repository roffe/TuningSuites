using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

/// <summary>The settings dialog each suite's SettingsWindow.axaml derives from: Ok / Cancel and the project folder's "..." button.</summary>
public class SuiteSettingsWindow : Window
{
    protected async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a project folder" });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) ((SuiteSettingsViewModel)DataContext!).ProjectFolder = path;
    }

    protected void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    protected void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
