using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace SuiteApp.ViewModels;

/// <summary>The Information tools every ECU has: the axis browser, the disassembly and the hex view.</summary>
public abstract partial class MainWindowViewModel
{
    /// <summary>Information → Browse axis information (the symbol list's "Browse axis info" passes one symbol).</summary>
    [RelayCommand]
    public void BrowseAxes(string? symbol)
    {
        if (Binary is { } bin) ShowDocument(new AxisBrowserViewModel(this, bin, symbol));
    }

    /// <summary>
    /// Show disassembly (full: the linear sweep): &lt;bin&gt;.asm / &lt;bin&gt;_full.asm next to the bin, redone when asked or when
    /// it isn't there yet, then shown beside the bin's bytes.
    /// </summary>
    public async Task ShowDisassemblyAsync(bool full, Func<string, Task<bool>> redo)
    {
        if (Binary is not { } bin) return;
        string file = Path.Combine(Path.GetDirectoryName(bin.FileName) ?? "", Path.GetFileNameWithoutExtension(bin.FileName) + (full ? "_full.asm" : ".asm"));
        if (!File.Exists(file) || await redo("Assemblerfile already exists, do you want to redo the disassembly?"))
        {
            IsBusy = true;
            ProgressText = "Disassembling...";
            try
            {
                await Task.Run(() => bin.Disassemble(file, full));
            }
            finally
            {
                IsBusy = false;
                ProgressText = "";
            }
        }
        ShowDocument(new DisassemblyViewModel(file, File.ReadAllBytes(bin.FileName), full, bin));
    }

    /// <summary>View file in hex: the bin, and the imported SRAM snapshot next to it.</summary>
    [RelayCommand]
    private void ViewHex()
    {
        if (Binary is not { } bin) return;
        ShowDocument(new HexViewerViewModel(bin.FileName, bin));
        if (SramFile is { } ram && File.Exists(ram)) ShowDocument(new HexViewerViewModel(ram, bin, sram: true));
    }

    /// <summary>A file dropped on a hex view (HexViewer's OpenFile): the view shows it instead, once the open one may close.</summary>
    public async Task OpenInHexViewerAsync(HexViewerViewModel viewer, string file)
    {
        HexViewerViewModel opened;
        try
        {
            opened = viewer.WithFile(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowInfo(e.Message);
            return;
        }
        if (await CloseViewerAsync(viewer)) ShowDocument(opened);
    }
}
