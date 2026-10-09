using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using T7App.ViewModels;

namespace T7App.Views;

/// <summary>frmFaultcodes: the codes read from the ECU; Clear clears the selected one and reads them again.</summary>
public partial class FaultCodesWindow : Window
{
    private readonly MainWindowViewModel? m_vm;

    public FaultCodesWindow() => InitializeComponent();

    public FaultCodesWindow(MainWindowViewModel vm, List<FaultCode> codes) : this()
    {
        m_vm = vm;
        Grid.ItemsSource = codes;
    }

    private async void OnClear(object? sender, RoutedEventArgs e)
    {
        if (m_vm != null && Grid.SelectedItem is FaultCode code && await m_vm.ClearFaultCodeAsync(code.Code) is { } codes) Grid.ItemsSource = codes;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
