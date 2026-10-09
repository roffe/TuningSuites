using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MapControls;
using T7;

namespace T7App.ViewModels;

/// <summary>One open map, set up the way frmMain.StartTableViewer configured a MapViewerEx, saved like tabdet_onSymbolSave.</summary>
public partial class MapViewerViewModel : ObservableObject
{
    public required MainWindowViewModel Owner { get; init; }
    public required T7Binary Binary { get; init; }
    public required SymbolHelper Symbol { get; init; }
    public required string MapName { get; init; }
    public required MapData Map { get; init; }

    /// <summary>Where the map's bytes sit in the file it was opened from.</summary>
    public required int Address { get; init; }

    public string FileName => Binary.FileName;

    /// <summary>The dock panel title T7Suite used, also how an already open viewer is found.</summary>
    public string Title => $"Symbol: {MapName} [{Path.GetFileName(FileName)}]";

    [ObservableProperty]
    private MapViewType _viewType;

    [ObservableProperty]
    private bool _isSelected;

    public bool IsReadOnly { get; init; }
    public bool IsRedWhite { get; init; }
    public bool DisableColors { get; init; }
    public bool GraphVisible { get; init; }
    public OpenLoopMark OpenLoopMark { get; init; }

    /// <summary>The axis symbols, when the bin has them ("Edit x-axis" / "Edit y-axis").</summary>
    public string? XAxisSymbol { get; init; }
    public string? YAxisSymbol { get; init; }
    public bool CanEditXAxis => XAxisSymbol != null;
    public bool CanEditYAxis => YAxisSymbol != null;

    /// <summary>Writes the map into its file, updates the checksum and adds a transaction entry when a project is open.</summary>
    [RelayCommand]
    private async Task Save()
    {
        // only writes into the project's own binary get transaction entries
        bool projectFile = Owner.Binary?.FileName == FileName && Owner.TransactionLog != null;
        string note = projectFile ? await Owner.AskTransactionNoteAsync() : "";
        int before = Owner.TransactionLog?.TransCollection.Count ?? 0;
        try
        {
            Binary.WriteSymbol(Address, Map.ToBytes(), Owner.Settings.AutoFixFooter, projectFile ? Owner.TransactionLog : null, note);
            Map.MarkSaved();
            if (projectFile) Owner.TransactionsAdded(before);
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
        if (Binary.ReadSymbol(Symbol) is { } content) Map.Load(content);
    }

    [RelayCommand]
    private void EditAxis(bool x)
    {
        if ((x ? XAxisSymbol : YAxisSymbol) is { } axis) Owner.OpenSymbolByName(axis);
    }

    /// <summary>Null when the map has no data in the file (it only lives in the ECU's SRAM).</summary>
    public static MapViewerViewModel? Create(MainWindowViewModel owner, T7Binary bin, SymbolHelper sh)
    {
        AppSettings settings = owner.Settings;
        string name = sh.SmartVarname;
        int address = bin.FileAddress(sh);
        byte[]? content = bin.ReadSymbol(sh);
        if (address < 0 || content == null || content.Length == 0) return null;

        var (xAxis, yAxis, xDescr, yDescr, zDescr) = T7Binary.AxisSymbols(name);
        double[] x = bin.GetXaxisValues(name).Select(v => (double)v).ToArray();
        double[] y = bin.GetYaxisValues(name).Select(v => (double)v).ToArray();
        var map = new MapData(name, content, bin.TableWidth(name), bin.IsSixteenBitTable(name))
        {
            Factor = bin.GetMapCorrectionFactor(name),
            Offset = T7Binary.GetMapCorrectionOffset(name),
            UpsideDown = true,
            XAxis = x,
            YAxis = y,
            XName = xDescr,
            YName = yDescr,
            ZName = zDescr,
        };
        // TryToAddOpenLoopTables, drawn by MapViewerEx only on load (mg/c) x rpm maps
        if (xDescr.Equals("mg/c", StringComparison.OrdinalIgnoreCase) && yDescr.Equals("rpm", StringComparison.OrdinalIgnoreCase)
            && bin.OpenLoopTable(name) is { Length: > 0 } ol)
        {
            map.OpenLoop = MapData.Decode(ol, true).Select(v => (double)v).ToArray();
        }

        int viewType = (int)settings.DefaultViewType;
        return new MapViewerViewModel
        {
            Owner = owner,
            Binary = bin,
            Symbol = sh,
            MapName = name,
            Map = map,
            Address = address,
            ViewType = viewType <= (int)MapViewType.Ascii ? (MapViewType)viewType : MapViewType.Easy,
            IsRedWhite = settings.ShowRedWhite,
            DisableColors = settings.DisableMapviewerColors,
            GraphVisible = settings.ShowGraphs,
            OpenLoopMark = (OpenLoopMark)Math.Clamp(settings.StandardFill, 0, 2),
            XAxisSymbol = xAxis != "" && bin.Find(xAxis) != null ? xAxis : null,
            YAxisSymbol = yAxis != "" && bin.Find(yAxis) != null ? yAxis : null,
        };
    }
}
