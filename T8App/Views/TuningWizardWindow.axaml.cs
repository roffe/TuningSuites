using Avalonia.Controls;
using Avalonia.Interactivity;
using SuiteApp.Services;
using T8App.ViewModels;

namespace T8App.Views;

public partial class TuningWizardWindow : Window
{
    public TuningWizardWindow() => InitializeComponent();

    private TuningWizardViewModel Vm => (TuningWizardViewModel)DataContext!;

    // applied: the pack's message, if it has one
    private async void OnNext(object? sender, RoutedEventArgs e)
    {
        if (await Vm.NextAsync() && Vm.Selected is { Message: not "" } pack) await Dialogs.Info(this, pack.Message, "Tuning Wizard Message");
    }

    private void OnBack(object? sender, RoutedEventArgs e) => Vm.Back();

    private void OnFinish(object? sender, RoutedEventArgs e) => Close();
}
