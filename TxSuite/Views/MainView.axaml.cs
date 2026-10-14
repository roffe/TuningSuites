using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using SuiteApp.Services;
using SuiteApp.ViewModels;
using SuiteApp.Views;
using T5App.ViewModels;
using T7App.ViewModels;
using T8App.ViewModels;
using TxSuite.ViewModels;

namespace TxSuite.Views;

/// <summary>
/// TxSuite's main window: one menu for T5, T7 and T8 binaries (MainView.axaml), the actions every ECU has in SuiteMainView, each
/// ECU's own in MainView.T5.cs / .T7.cs / .T8.cs, as the old suites' main windows had them.
/// </summary>
public partial class MainView : SuiteMainView
{
    public MainView() => InitializeComponent();

    private new MainViewModel Vm => (MainViewModel)DataContext!;

    protected override string BinaryFilesName => "Trionic binary or Motorola S19";

    protected override string OpenFileTitle => "Open a binary Trionic file...";

    // the old suites' filter names for the open file's ECU
    protected override string CompareFilesName => Vm.Module switch
    {
        T5Module => "Trionic 5 binary files",
        T8Module => "Trionic 8 binaries",
        _ => "binary files",
    };

    // T5Suite's compare took several files at once
    protected override bool CompareSeveralFiles => Vm.IsT5;

    protected override (string caption, string text) TransferWizard => Vm.IsT5 ? T5TransferWizard : base.TransferWizard;

    /// <summary>The three old suites' thanks in one list.</summary>
    protected override string AboutThanks =>
        "Dilemma, Actitis H., Steve Hayes, Hook, mackan, MrAze, Sandy_rus, T5_Germany, Seb, Tomili, sourcode, J.K Nilsson, G-ice, " +
        "General Failure, Danibjor, Johnc, tomas0student, Janus0070, Mattias Claesson, Roffe and...";

    /// <summary>The partnumber list (frmPartNumberList): a tab per ECU, the open file's in front, T5's with none open.</summary>
    protected override Func<Task<string?>>? BrowsePartNumbers => () =>
        PartNumberListWindow.Show(Host, Vm.Binary == null ? 0 : Math.Max(0, Vm.Ecus.ToList().IndexOf(Vm.Module)), PartNumberTabs());

    /// <summary>Each old suite's PartnumberCollection as its frmPartNumberList showed it: T5, T7, T8.</summary>
    public static PartNumberTab[] PartNumberTabs() =>
    [
        T5App.Views.T5ToolWindows.PartNumberTab(),
        PartNumberListWindow.Tab("T7", new T7.PartnumberCollection().GeneratePartNumberCollection()),
        PartNumberListWindow.Tab("T8", new T8SuitePro.PartnumberCollection().GeneratePartNumberCollection()),
    ];

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not MainViewModel vm) return;
        vm.T5.ShowFirmwareOptions = ShowFirmwareOptions;
        vm.T8.ShowBitmask = bits => new T8App.Views.BitmaskWindow { DataContext = bits }.ShowDialog<bool>(Host);
        // "Select mutations to accept" on the autotuned fuel map's axes: T5's Insp_mat!, T7's BFuelCal.Map (or the one set)
        vm.AcceptAutotune = percent =>
        {
            if (vm.IsT5)
            {
                double[] x5 = vm.Binary?.GetXaxisValues("Insp_mat!").Select(v => (double)v).ToArray() ?? [];
                double[] y5 = vm.Binary?.GetYaxisValues("Insp_mat!").Select(v => (double)v).ToArray() ?? [];
                return new AutotuneAcceptWindow("Insp_mat!", percent, x5, y5, "MAP", "RPM", vm.Binary?.XAxisEasy("Insp_mat!"))
                    .ShowDialog<System.Collections.Generic.IReadOnlyCollection<int>?>(Host);
            }
            string map = string.IsNullOrEmpty(vm.Settings.AutoTuneFuelMap) ? "BFuelCal.Map" : vm.Settings.AutoTuneFuelMap;
            double[] x = vm.Binary?.GetXaxisValues("BFuelCal.Map").Select(v => (double)v).ToArray() ?? [];
            double[] y = vm.Binary?.GetYaxisValues("BFuelCal.Map").Select(v => (double)v).ToArray() ?? [];
            return new AutotuneAcceptWindow(map, percent, x, y).ShowDialog<System.Collections.Generic.IReadOnlyCollection<int>?>(Host);
        };
    }

    /// <summary>File → Settings: Common and a tab per ECU (TxSettingsWindow); Ok applies them to every ECU at once.</summary>
    private async void OnSettings(object? sender, RoutedEventArgs e)
    {
        var settings = new TxSettingsViewModel(Vm);
        if (!await new TxSettingsWindow { DataContext = settings }.ShowDialog<bool>(Host)) return;
        settings.Apply();
        Vm.SettingsChanged();
        ApplyHideSymbolTable();
    }

    /// <summary>Actions → Firmware information: T5Suite's firmware options, T7Suite's or T8Suite's firmware information.</summary>
    private async void OnFirmwareInformation(object? sender, RoutedEventArgs e)
    {
        if (Vm.IsT5) await Vm.T5.EditFirmwareAsync();
        else if (Vm.IsT7) await T7FirmwareInformation();
        else if (Vm.IsT8) await T8FirmwareInformation();
    }
}
