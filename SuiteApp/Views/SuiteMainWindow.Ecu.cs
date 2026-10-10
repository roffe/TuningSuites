using System.IO;
using Avalonia.Interactivity;
using CommonSuite;
using SuiteApp.Services;

namespace SuiteApp.Views;

/// <summary>The ECU menus both suites have: Read ECU, Flash, fault codes, the SRAM snapshot file and reading maps from it.</summary>
public partial class SuiteMainWindow
{
    protected async void OnReadEcu(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.SaveFile(this, "Binary files", "bin") is { } file) await Vm.ReadEcuAsync(file);
    }

    protected async void OnFlashEcu(object? sender, RoutedEventArgs e) =>
        await Vm.FlashEcuAsync(text => Dialogs.YesNo(this, text, "Question"));

    protected async void OnFaultCodes(object? sender, RoutedEventArgs e)
    {
        if (await Vm.ReadFaultCodesAsync() is { } codes) new FaultCodesWindow(Vm, codes).Show(this);
    }

    protected async void OnCompareToSram(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, "SRAM dumps", "*.ram") is { } file) await Vm.CompareToSramAsync(file);
    }

    protected async void OnCompareSram(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, "First SRAM dump", "*.ram") is { } first && await Dialogs.OpenFile(this, "Second SRAM dump", "*.ram") is { } second)
            await Vm.CompareSramAsync(first, second);
    }

    /// <summary>T5Suite's "Binary compare SRAM snapshots": the 16-byte line diff of two snapshots, no bin needed.</summary>
    protected async void OnBinaryCompareSram(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, "SRAM dumps", "*.ram") is not { } first || await Dialogs.OpenFile(this, "SRAM dumps", "*.ram") is not { } second) return;
        var lines = SuiteCompare.BinaryDiff(first, second);
        var text = new System.Text.StringBuilder($"{Path.GetFileName(first)} / {Path.GetFileName(second)}: {lines.Count} lines differ\n\n");
        foreach (var (mine, theirs) in lines) text.Append(mine).Append('\n').Append(theirs).Append("\n\n");
        await Dialogs.Text(this, "Binary compare", text.ToString());
    }

    protected async void OnImportSram(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, "SRAM dump files", "*.RAM") is { } file) Vm.ImportSramSnapshot(file);
    }

    protected void OnReadFromSramFile(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedSymbol is { } sh) Vm.OpenFromSramFile(sh);
    }
}
