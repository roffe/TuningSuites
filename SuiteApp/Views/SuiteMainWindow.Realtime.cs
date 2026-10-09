using System.Threading.Tasks;
using Avalonia.Interactivity;
using SuiteApp.Services;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

/// <summary>The Realtime menu's log tools both suites have, and the symbol list's Add to realtime list.</summary>
public partial class SuiteMainWindow
{
    private Task<string?> OpenLogFile() => Dialogs.OpenFile(this, Vm.LogFilesName, "*." + Vm.RealtimeRules.LogExtension);

    protected async void OnOpenLog(object? sender, RoutedEventArgs e)
    {
        if (await OpenLogFile() is not { } file) return;
        await Vm.OpenLogAsync(file, sections => new ChoiceWindow("Select logfile section to display", sections).ShowDialog<int?>(this));
    }

    private async Task<(string file, LogSelectionViewModel selection)?> ChooseLogData()
    {
        if (await OpenLogFile() is not { } file) return null;
        LogSelectionViewModel selection = Vm.LogSelection(file);
        return await new LogSelectionWindow { DataContext = selection }.ShowDialog<bool>(this) ? (file, selection) : null;
    }

    protected async void OnExportLogCsv(object? sender, RoutedEventArgs e)
    {
        if (await ChooseLogData() is var (file, selection)) Vm.ExportLogCsv(file, selection);
    }

    protected async void OnExportLogDif(object? sender, RoutedEventArgs e)
    {
        if (await ChooseLogData() is var (file, selection)) Vm.ExportLogDif(file, selection);
    }

    protected async void OnLogMatrix(object? sender, RoutedEventArgs e)
    {
        if (await OpenLogFile() is not { } file) return;
        var (lines, selection) = Vm.MatrixSelection(file);
        if (await new MatrixSelectionWindow { DataContext = selection }.ShowDialog<bool>(this)) Vm.ShowMatrix(lines, selection);
    }

    protected async void OnLogFilters(object? sender, RoutedEventArgs e)
    {
        var filters = new LogFiltersViewModel(Vm.LoadLogFilters(), Vm.LogFilterSymbols);
        if (await new LogFiltersWindow { DataContext = filters }.ShowDialog<bool>(this)) Vm.SaveLogFilters(filters.ToCollection());
    }

    protected async void OnSymbolColors(object? sender, RoutedEventArgs e)
    {
        SymbolColorsViewModel colors = Vm.SymbolColorChoices();
        if (await new SymbolColorsWindow { DataContext = colors }.ShowDialog<bool>(this)) colors.Save();
    }

    protected void OnAddToRealtime(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedSymbol is { } sh) Vm.AddToRealtime(sh);
    }
}
