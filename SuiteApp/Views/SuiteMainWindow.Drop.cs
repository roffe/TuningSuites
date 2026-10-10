using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace SuiteApp.Views;

/// <summary>
/// Files dropped anywhere on the main window load the way their menu command loads them: a binary as Open file, an SRAM dump
/// as Import SRAM snapshot, a logfile as Load logfile. A hex view takes a dropped file itself (HexViewerView).
/// </summary>
public partial class SuiteMainWindow
{
    private enum DropKind { None, Binary, Sram, Log }

    private void InitDrop()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    /// <summary>
    /// The dropped files' paths, folders left out; none while the window shows a dialog, as its menus (X11 hands a drop to a
    /// window its dialog disabled).
    /// </summary>
    internal static string[] DroppedFiles(DragEventArgs e) =>
        TopLevel.GetTopLevel(e.Source as Visual) is Window w && w.OwnedWindows.Any(o => o.IsDialog) ? []
            : e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().Select(f => f.TryGetLocalPath()).OfType<string>().ToArray() ?? [];

    // by the menu command's picker types, in any letter case
    private DropKind KindOf(string file) =>
        BinaryPatterns.Any(p => Matches(file, p)) ? DropKind.Binary
        : Matches(file, SramPattern) ? DropKind.Sram
        : Matches(file, "*." + Vm.RealtimeRules.LogExtension) ? DropKind.Log
        : DropKind.None;

    private static bool Matches(string file, string pattern) => file.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);

    // Copy or None, never the source's Move (a file manager would delete the file); a text drag over the disassembly's editor
    // never gets here, the editor handles it
    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = DroppedFiles(e).Any(f => KindOf(f) != DropKind.None) ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        OnDragOver(sender, e);
        string[] files = DroppedFiles(e);
        if (files.Length == 0) return;
        e.Handled = true;
        // once the drop returns: the app the files came from waits for it, and a log is read on this thread
        Dispatcher.UIThread.Post(() => LoadDropped(files));
    }

    /// <summary>
    /// Several files at once: the first binary opens, then the first SRAM dump is imported (so it goes with that binary), then
    /// every logfile opens in the order dropped. One binary and one snapshot are open at a time: the others, and the files
    /// nothing loads, are named in one message at the end.
    /// </summary>
    private async void LoadDropped(string[] files)
    {
        string? bin = files.FirstOrDefault(f => KindOf(f) == DropKind.Binary), ram = files.FirstOrDefault(f => KindOf(f) == DropKind.Sram);
        if (bin != null) await Vm.OpenPlainFileAsync(bin, true);
        if (ram != null) Vm.ImportSramSnapshot(ram);
        foreach (string log in files.Where(f => KindOf(f) == DropKind.Log)) await LoadLog(log);
        var rest = files.Where(f => f != bin && f != ram && KindOf(f) != DropKind.Log).Select(Path.GetFileName).ToList();
        if (rest.Count > 0)
            Vm.ShowInfo($"Not loaded: {string.Join(", ", rest)}. Drop one binary ({string.Join(", ", BinaryPatterns)}), one SRAM snapshot ({SramPattern}) "
                + $"and logfiles (*.{Vm.RealtimeRules.LogExtension}).");
    }
}
