using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

/// <summary>
/// The settings dialog each suite's SettingsWindow.axaml derives from: Ok / Cancel, the project folder's "..." button and the
/// "Autotune settings" button (T5Suite's and T7Suite's, their own dialog over this one).
/// </summary>
public class SuiteSettingsWindow : Window
{
    /// <summary>Shows the suite's autotune settings over this window.</summary>
    public Func<Window, Task>? AutotuneSettings { get; set; }

    protected async void OnAutotuneSettings(object? sender, RoutedEventArgs e)
    {
        if (AutotuneSettings != null) await AutotuneSettings(this);
    }

    protected async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a project folder" });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) ((SuiteSettingsViewModel)DataContext!).ProjectFolder = path;
    }

    protected void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    protected void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
