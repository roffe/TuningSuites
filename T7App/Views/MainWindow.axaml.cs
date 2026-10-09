using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using T7App.Services;
using T7App.ViewModels;

namespace T7App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Enter opens the symbol; the grid would move to the next row, so catch it on the way down, unless a cell is being edited
        SymbolGrid.AddHandler(KeyDownEvent, OnSymbolKeyDown, RoutingStrategies.Tunnel);
        SymbolGrid.BeginningEdit += (_, _) => m_editingSymbol = true;
    }

    private bool m_editingSymbol;

    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Info += text => _ = Dialogs.Info(this, text, "T7Suite");
            vm.AskYesNoCancel = text => Dialogs.YesNoCancel(this, text, "Question");
            vm.AskText = caption => Dialogs.Prompt(this, caption);
            vm.AskOkCancel = text => Dialogs.OkCancel(this, text, "Transaction log size warning...");
        }
    }

    private async void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        string? path = await Dialogs.OpenFile(this, "Trionic 7 binary or Motorola S19", "*.bin", "*.s19");
        if (path != null) await Vm.OpenPlainFileAsync(path, true);
    }

    private async void OnFirmwareInformation(object? sender, RoutedEventArgs e)
    {
        if (Vm.FirmwareInfo() is not { } info) return;
        var dialog = new FirmwareInfoWindow { DataContext = info };
        info.Hint += text => _ = Dialogs.Info(dialog, text, "T7Suite");
        if (await dialog.ShowDialog<bool>(this))
            Vm.ApplyFirmware(info, text => Dialogs.Wait(() => Dialogs.YesNo(this, text, "Question")));
    }

    // ---- projects ----

    private async void OnCreateProject(object? sender, RoutedEventArgs e)
    {
        ProjectPropertiesViewModel p = Vm.NewProjectProperties();
        if (await new ProjectPropertiesWindow { DataContext = p }.ShowDialog<bool>(this)) await Vm.CreateProjectAsync(p);
    }

    private async void OnOpenProject(object? sender, RoutedEventArgs e)
    {
        var projects = T7.T7Project.List(Vm.Settings.ProjectFolder);
        if (projects.Count == 0)
        {
            await Dialogs.Info(this, "No projects were found, please create one first!", "T7Suite");
            return;
        }
        if (await new ProjectSelectionWindow(projects).ShowDialog<string?>(this) is { Length: > 0 } name) await Vm.OpenProjectAsync(name);
    }

    private async void OnEditProject(object? sender, RoutedEventArgs e)
    {
        if (Vm.Project is not { } project) return;
        var p = ProjectPropertiesViewModel.From(project.Properties);
        if (await new ProjectPropertiesWindow { DataContext = p }.ShowDialog<bool>(this)) await Vm.EditProjectAsync(p);
    }

    private void OnShowTransactionLog(object? sender, RoutedEventArgs e) =>
        new TransactionLogWindow { DataContext = new TransactionLogViewModel(Vm) }.Show(this);

    private void OnShowLogbook(object? sender, RoutedEventArgs e)
    {
        if (Vm.Project is { } project) new LogbookWindow { DataContext = new LogbookViewModel(project) }.Show(this);
    }

    private async void OnRebuild(object? sender, RoutedEventArgs e)
    {
        var p = new RebuildViewModel();
        if (!await new RebuildWindow { DataContext = p }.ShowDialog<bool>(this)) return;
        // the date picker gives midnight; the whole chosen day counts
        System.DateTime upTo = (p.UpTo ?? System.DateTime.Now).Date.AddDays(1).AddTicks(-1);
        if (Vm.Rebuild(upTo, p.StoreAsCurrent) is not { } rebuilt) return;
        if (await Dialogs.SaveFile(this, "Binary files", "bin", "rebuild.bin") is { } dest) System.IO.File.Copy(rebuilt, dest, true);
        System.IO.File.Delete(rebuilt);
    }

    private async void OnProduceLatest(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is { } bin && await Dialogs.SaveFile(this, "Binary files", "bin", System.IO.Path.GetFileName(bin.FileName)) is { } dest)
            System.IO.File.Copy(bin.FileName, dest, true);
    }

    // unsaved maps ask before the app closes; Cancel keeps it open
    private bool m_closeConfirmed;

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (m_closeConfirmed || DataContext is not MainWindowViewModel vm || !vm.Viewers.Any(v => v.Map.Mutated)) return;
        e.Cancel = true;
        if (!await vm.CloseMutatedViewersAsync()) return;
        m_closeConfirmed = true;
        Close();
    }

    private void OnSymbolCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        m_editingSymbol = false;
        if (e.EditAction == DataGridEditAction.Commit) Vm.SaveUserDescriptions();
    }

    private void OnSymbolDoubleTapped(object? sender, TappedEventArgs e) => Vm.OpenSymbolCommand.Execute(Vm.SelectedSymbol);

    private void OnSymbolKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || m_editingSymbol) return;
        Vm.OpenSymbolCommand.Execute(Vm.SelectedSymbol);
        e.Handled = true;
    }
}
