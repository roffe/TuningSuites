using Avalonia.Controls;
using Avalonia.Interactivity;
using CommonSuite;
using T7App.ViewModels;

namespace T7App.Views;

public partial class TransactionLogWindow : Window
{
    public TransactionLogWindow() => InitializeComponent();

    private TransactionLogViewModel Vm => (TransactionLogViewModel)DataContext!;

    private void OnRollBack(object? sender, RoutedEventArgs e) => Vm.Roll(true);

    private void OnRollForward(object? sender, RoutedEventArgs e) => Vm.Roll(false);

    private void OnOk(object? sender, RoutedEventArgs e) => Close();

    private void OnCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit && e.Row.DataContext is TransactionEntry entry) Vm.NoteChanged(entry);
    }
}
