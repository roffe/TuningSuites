using System.Threading.Tasks;
using Avalonia.Interactivity;
using SuiteApp.Services;

namespace SuiteApp.Views;

/// <summary>The Information tools both suites have: interrupt vectors, disassembly, the symbol list's Browse axis info.</summary>
public partial class SuiteMainWindow
{
    protected async void OnVectors(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is { } bin) await new VectorsWindow(bin.InterruptVectors()).ShowDialog(this);
    }

    protected async void OnDisassembly(object? sender, RoutedEventArgs e) =>
        await Vm.ShowDisassemblyAsync(false, text => Dialogs.YesNo(this, text, "Question"));

    // the full sweep is reused when it exists, as the suites did
    protected async void OnFullDisassembly(object? sender, RoutedEventArgs e) =>
        await Vm.ShowDisassemblyAsync(true, _ => Task.FromResult(false));

    protected void OnBrowseAxes(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedSymbol is { } sh) Vm.BrowseAxes(sh.SmartVarname);
    }
}
