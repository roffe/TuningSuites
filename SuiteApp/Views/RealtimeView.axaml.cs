using Avalonia.Controls;
using Avalonia.Input;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

public partial class RealtimeView : UserControl
{
    public RealtimeView() => InitializeComponent();

    private RealtimeViewModel Vm => (RealtimeViewModel)DataContext!;

    private void OnToggleAfr(object? sender, PointerPressedEventArgs e) => Vm.ToggleAfrModeCommand.Execute(null);
}
