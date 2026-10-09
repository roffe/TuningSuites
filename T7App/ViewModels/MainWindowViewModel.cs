using System.Reflection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace T7App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public string Title { get; } = "T7Suite " +
        typeof(MainWindowViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    [ObservableProperty]
    private string _status = "No file loaded";

    [RelayCommand]
    private static void Exit() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
}
