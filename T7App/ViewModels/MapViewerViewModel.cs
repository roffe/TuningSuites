using System;
using System.IO;
using System.Linq;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using MapControls;
using T7;

namespace T7App.ViewModels;

/// <summary>One open map, set up the way frmMain.StartTableViewer configured a MapViewerEx.</summary>
public partial class MapViewerViewModel : ObservableObject
{
    public required string MapName { get; init; }
    public required string FileName { get; init; }
    public required MapData Map { get; init; }

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

    /// <summary>Null when the map has no data in the file (it only lives in the ECU's SRAM).</summary>
    public static MapViewerViewModel? Create(T7Binary bin, SymbolHelper sh, AppSettings settings)
    {
        string name = sh.SmartVarname;
        byte[]? content = bin.ReadSymbol(sh);
        if (content == null || content.Length == 0) return null;

        var (_, _, xDescr, yDescr, zDescr) = T7Binary.AxisSymbols(name);
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
            MapName = name,
            FileName = bin.FileName,
            Map = map,
            // ponytail: read-only until editing and saving land (chunk 4)
            IsReadOnly = true,
            ViewType = viewType <= (int)MapViewType.Ascii ? (MapViewType)viewType : MapViewType.Easy,
            IsRedWhite = settings.ShowRedWhite,
            DisableColors = settings.DisableMapviewerColors,
            GraphVisible = settings.ShowGraphs,
            OpenLoopMark = (OpenLoopMark)Math.Clamp(settings.StandardFill, 0, 2),
        };
    }
}
