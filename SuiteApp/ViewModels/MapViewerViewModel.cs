using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MapControls;

namespace SuiteApp.ViewModels;

/// <summary>One open map, set up the way StartTableViewer configured a MapViewerEx, saved like tabdet_onSymbolSave.</summary>
public partial class MapViewerViewModel : DocumentViewModel
{
    public required MainWindowViewModel Owner { get; init; }
    public required SuiteBinary Binary { get; init; }
    public required SymbolHelper Symbol { get; init; }
    public required string MapName { get; init; }
    public required MapData Map { get; init; }

    /// <summary>Where the map's bytes sit in the file it was opened from.</summary>
    public required int Address { get; init; }

    public string FileName => Binary.FileName;

    public override string Title => TitleOverride ?? $"Symbol: {MapName} [{Path.GetFileName(FileName)}]";

    /// <summary>"Symbol difference: ..." and other titles than a plain map's.</summary>
    public string? TitleOverride { get; init; }

    [ObservableProperty]
    private MapViewType _viewType;

    /// <summary>Showing the ECU's SRAM (IsRAMViewer / OnlineMode: blue colours).</summary>
    [ObservableProperty]
    private bool _onlineMode;

    /// <summary>The engine's cell while the realtime panel runs (column, data row).</summary>
    [ObservableProperty]
    private Avalonia.PixelPoint? _liveCell;

    /// <summary>Maps that only live in SRAM have no file to save to.</summary>
    public bool CanSaveToFile => Address >= 0 || SaveTo != null;

    /// <summary>Where a map that isn't in the bin saves and reloads (the AFR target map's .afr file).</summary>
    public Action<byte[]>? SaveTo { get; set; }
    public Func<byte[]>? ReadFrom { get; set; }

    /// <summary>AFR maps live in files of their own, not in the ECU.</summary>
    public bool IsAfrMap { get; init; }

    /// <summary>The viewer's Read from ECU / Save to ECU buttons: none for AFR maps, nor for symbols without an SRAM copy (T8).</summary>
    public IAsyncRelayCommand? EcuReadCommand => IsAfrMap || NoEcu || !Binary.InSram(Symbol) ? null : ReadEcuCommand;
    public IAsyncRelayCommand? EcuWriteCommand => IsAfrMap || NoEcu || !Binary.InSram(Symbol) ? null : WriteEcuCommand;

    /// <summary>No ECU buttons (a tuning package's map).</summary>
    public bool NoEcu { get; set; }

    [RelayCommand]
    private Task ReadEcu() => Owner.ReadMapFromEcuAsync(this);

    [RelayCommand]
    private Task WriteEcu() => Owner.WriteMapToEcuAsync(this);

    public bool IsReadOnly { get; init; }
    public bool IsRedWhite { get; init; }
    public bool DisableColors { get; init; }
    public bool GraphVisible { get; init; }
    public OpenLoopMark OpenLoopMark { get; init; }

    /// <summary>Viewers of the same map follow each other's selection and 3D view, when "Synchronize mapviewers" is on.</summary>
    public string? SyncGroup => Owner.Settings.SynchronizeMapviewers ? MapName : null;

    /// <summary>The axis symbols, when the bin has them ("Edit x-axis" / "Edit y-axis").</summary>
    public string? XAxisSymbol { get; init; }
    public string? YAxisSymbol { get; init; }
    public bool CanEditXAxis => XAxisSymbol != null;
    public bool CanEditYAxis => YAxisSymbol != null;

    /// <summary>Writes the map into its file, updates the checksum and adds a transaction entry when a project is open.</summary>
    [RelayCommand]
    private async Task Save()
    {
        if (!CanSaveToFile) return;
        if (SaveTo != null)
        {
            SaveTo(Map.ToBytes());
            Map.MarkSaved();
            return;
        }
        // only writes into the project's own binary get transaction entries
        bool projectFile = Owner.Binary?.FileName == FileName && Owner.TransactionLog != null;
        string note = projectFile ? await Owner.AskTransactionNoteAsync() : "";
        int before = Owner.TransactionLog?.TransCollection.Count ?? 0;
        try
        {
            Binary.WriteSymbol(Address, Map.ToBytes(), projectFile ? Owner.TransactionLog : null, note);
            Map.MarkSaved();
            if (projectFile) Owner.TransactionsAdded(before);
            if (Owner.SaveWritesEcu && OnlineMode && Symbol.Start_address > 0) await Owner.WriteMapToEcuAsync(this);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Owner.ShowInfo("Failed to write to binary. Is it read-only? Details: " + e.Message);
        }
        catch (InvalidOperationException e)
        {
            Owner.ShowInfo(e.Message);
        }
    }

    /// <summary>tabdet_onSymbolRead: the map again as the file has it, dropping unsaved changes.</summary>
    [RelayCommand]
    private void Read()
    {
        if (!CanSaveToFile) return;
        if ((ReadFrom != null ? ReadFrom() : Binary.ReadSymbol(Symbol)) is { } content) Map.Load(content);
    }

    [RelayCommand]
    private void EditAxis(bool x)
    {
        if ((x ? XAxisSymbol : YAxisSymbol) is { } axis) Owner.OpenSymbolByName(axis);
    }

    /// <summary>
    /// Null when the map has no data in the file (it only lives in the ECU's SRAM). content replaces the file's bytes (a
    /// difference map), readOnly makes a compare viewer, title replaces "Symbol: name [file]".
    /// </summary>
    public static MapViewerViewModel? Create(MainWindowViewModel owner, SuiteBinary bin, SymbolHelper sh, byte[]? content = null, bool readOnly = false,
        string? title = null, bool sram = false)
    {
        AppSettings settings = owner.Settings;
        string name = sh.SmartVarname;
        // sram: the content came from the ECU or a snapshot, not from the file
        int address = sram ? -1 : bin.FileAddress(sh);
        content ??= bin.ReadSymbol(sh);
        if ((address < 0 && !sram) || content == null || content.Length == 0) return null;

        var (xAxis, yAxis, xDescr, yDescr, zDescr) = bin.AxisSymbols(name);
        // T5's MAP axes follow the sensor (integer math, as T5Suite)
        int xScale = bin.AxisScalePercent(xDescr), yScale = bin.AxisScalePercent(yDescr);
        double[] x = bin.GetXaxisValues(name).Select(v => (double)(v * xScale / 100)).ToArray();
        double[] y = bin.GetYaxisValues(name).Select(v => (double)(v * yScale / 100)).ToArray();
        var map = new MapData(name, content, bin.TableWidth(name), bin.IsSixteenBitTable(name), bin.SignAbove(name))
        {
            Factor = bin.GetMapCorrectionFactor(name),
            Offset = bin.GetMapCorrectionOffset(name),
            ScalePercent = bin.ScalePercent(name),
            UpsideDown = true,
            XAxis = x,
            YAxis = y,
            XName = xDescr,
            YName = yDescr,
            ZName = zDescr,
            // TryToAddOpenLoopTables, drawn only on load
            OpenLoop = bin.OpenLoopLimits(name)?.Select(v => (double)((int)v * xScale / 100)).ToArray(),
        };

        int viewType = (int)settings.DefaultViewType;
        return new MapViewerViewModel
        {
            Owner = owner,
            Binary = bin,
            Symbol = sh,
            MapName = name,
            Map = map,
            Address = address,
            IsReadOnly = readOnly,
            TitleOverride = title,
            ViewType = viewType <= (int)MapViewType.Ascii ? (MapViewType)viewType : MapViewType.Easy,
            IsRedWhite = settings.ShowRedWhite,
            DisableColors = settings.DisableMapviewerColors,
            GraphVisible = settings.ShowGraphs,
            OpenLoopMark = (OpenLoopMark)Math.Clamp(settings.StandardFill, 0, 2),
            XAxisSymbol = xAxis != "" && bin.Find(xAxis) != null ? xAxis : null,
            YAxisSymbol = yAxis != "" && bin.Find(yAxis) != null ? yAxis : null,
        };
    }

    /// <summary>MapViewerEx's close: unsaved changes ask Yes (save) / No (discard) / Cancel (keep open).</summary>
    public override async Task<bool> CanCloseAsync(MainWindowViewModel owner)
    {
        if (!Map.Mutated || owner.AskYesNoCancel == null) return true;
        owner.SelectedViewer = this;
        bool? save = await owner.AskYesNoCancel("Data was mutated, do you want to save these changes in you binary?");
        if (save == null) return false;
        if (save == false) return true;
        await SaveCommand.ExecuteAsync(null);
        // the save failed: keep the changes on screen
        return !Map.Mutated;
    }
}
