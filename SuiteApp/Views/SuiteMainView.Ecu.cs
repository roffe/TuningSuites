using System.IO;
using Avalonia.Interactivity;
using CommonSuite;
using SuiteApp.Services;

namespace SuiteApp.Views;

/// <summary>The ECU menus both suites have: Read ECU, Flash, fault codes, the SRAM snapshot file and reading maps from it.</summary>
public partial class SuiteMainView
{
    protected async void OnReadEcu(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.SaveFile(Host, "Read ECU", "Binary files", "bin", null) is { } file) await Vm.ReadEcuAsync(file);
    }

    protected async void OnFlashEcu(object? sender, RoutedEventArgs e) =>
        await Vm.FlashEcuAsync(text => Dialogs.YesNo(Host, text, "Question"));

    protected async void OnFaultCodes(object? sender, RoutedEventArgs e)
    {
        if (await Vm.ReadFaultCodesAsync() is { } codes) new FaultCodesWindow(Vm, codes).Show(Host);
    }

    protected async void OnCompareToSram(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(Host, "Select SRAM file to compare...", "SRAM dumps", ["*.ram"]) is { } file) await Vm.CompareToSramAsync(file);
    }

    protected async void OnCompareSram(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(Host, "First SRAM dump...", "SRAM dumps", ["*.ram"]) is { } first
            && await Dialogs.OpenFile(Host, "Second SRAM dump...", "SRAM dumps", ["*.ram"]) is { } second)
            await Vm.CompareSramAsync(first, second);
    }

    /// <summary>T5Suite's "Binary compare SRAM snapshots": the 16-byte line diff of two snapshots, no bin needed.</summary>
    protected async void OnBinaryCompareSram(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(Host, "First SRAM dump...", "SRAM dumps", ["*.ram"]) is not { } first
            || await Dialogs.OpenFile(Host, "Second SRAM dump...", "SRAM dumps", ["*.ram"]) is not { } second) return;
        var lines = SuiteCompare.BinaryDiff(first, second);
        var text = new System.Text.StringBuilder($"{Path.GetFileName(first)} / {Path.GetFileName(second)}: {lines.Count} lines differ\n\n");
        foreach (var (mine, theirs) in lines) text.Append(mine).Append('\n').Append(theirs).Append("\n\n");
        await Dialogs.Text(Host, "Binary compare", text.ToString());
    }

    // Import SRAM snapshot's type, also what a dropped file is imported as
    private const string SramPattern = "*.RAM";

    protected async void OnImportSram(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(Host, "Select a SRAM snapshot", "SRAM dump files", [SramPattern]) is { } file) Vm.ImportSramSnapshot(file);
    }

    protected void OnReadFromSramFile(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedSymbol is { } sh) Vm.OpenFromSramFile(sh);
    }
}
