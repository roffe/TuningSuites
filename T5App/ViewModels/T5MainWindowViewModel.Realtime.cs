using System;
using System.Linq;
using CommonSuite;
using CommunityToolkit.Mvvm.Input;
using MapControls;
using SuiteApp.ViewModels;
using Trionic5Tools;

namespace T5App.ViewModels;

/// <summary>Online tuning's "Air/Fuel maps": the AFR target, feedback and error maps (main and idle) next to the bin.</summary>
public partial class T5MainWindowViewModel
{
    private AFRMaps? m_afr;
    private string? m_afrFile;

    /// <summary>The open bin's AFR maps (AFRMaps folder next to it), made on open with "Always create AFR maps", else on first use.</summary>
    public AFRMaps? AfrMaps
    {
        get
        {
            if (T5 is not { } bin) return null;
            if (m_afr == null || m_afrFile != bin.FileName)
            {
                m_afr = new AFRMaps { TrionicFile = bin.File };
                m_afr.InitializeMaps();
                m_afrFile = bin.FileName;
            }
            return m_afr;
        }
    }

    protected override RealtimeViewModel CreateRealtimePanel(SuiteBinary bin) => new T5RealtimeViewModel(this, (T5Binary)bin);

    [RelayCommand]
    private void ShowAfr(string kind)
    {
        if (AfrMaps is not { } afr || T5 is not { } bin) return;
        if (CreateAfrViewer(afr, bin, kind) is { } viewer) ShowDocument(viewer);
    }

    /// <summary>Generate AFR target: T5Suite's target for the file (lambda control, open loop, idle), over the old one without asking.</summary>
    [RelayCommand]
    private void GenerateAfrTarget()
    {
        if (AfrMaps is not { } afr || T5 is not { } bin) return;
        afr.CreateTargetMap(bin.File.GetTrionicProperties().InjectorType);
        foreach (MapViewerViewModel v in Viewers.OfType<MapViewerViewModel>().Where(v => v.IsAfrMap && v.MapName == "TargetAFR")) v.Map.Load(AfrContent(afr, v.MapName));
        ShowAfr("TargetAFR");
    }

    private static byte[] AfrContent(AFRMaps afr, string kind) => kind switch
    {
        "TargetAFR" => afr.GetTargetAFRMapinBytes(),
        "FeedbackAFR" => afr.GetFeedbackAFRMapinBytes(),
        "FeedbackvsTargetAFR" => afr.GetDifferenceMapinBytes(),
        "IdleTargetAFR" => afr.GetIdleTargetAFRMapinBytes(),
        "IdleFeedbackAFR" => afr.GetIdleFeedbackAFRMapinBytes(),
        _ => afr.GetIdleDifferenceMapinBytes(),
    };

    /// <summary>
    /// ShowAfrMap: the AFR × 10 maps (16-bit) on the fuel map's axes (Insp_mat!, the idle ones on Idle_fuel_korr!). The targets save
    /// into their .afr files; feedback and error are read-only and follow the passes.
    /// </summary>
    private MapViewerViewModel? CreateAfrViewer(AFRMaps afr, T5Binary bin, string kind)
    {
        bool idle = kind.StartsWith("Idle"), target = kind.EndsWith("TargetAFR") && !kind.Contains("vs");
        string fuelName = idle ? "Idle_fuel_korr!" : "Insp_mat!";
        if (bin.Find(fuelName) is not { } fuel) return null;
        var (_, _, xDescr, yDescr, _) = bin.AxisSymbols(fuelName);
        var map = new MapData(kind, AfrContent(afr, kind), bin.TableWidth(fuelName), true)
        {
            Factor = 0.1,
            UpsideDown = true,
            XAxis = bin.GetXaxisValues(fuelName).Select(v => (double)v).ToArray(),
            YAxis = bin.GetYaxisValues(fuelName).Select(v => (double)v).ToArray(),
            XName = xDescr,
            YName = yDescr,
            ZName = "AFR",
            OpenLoop = idle ? null : bin.OpenLoopLimits(fuelName),
        };
        AppSettings settings = Settings;
        return new MapViewerViewModel
        {
            Owner = this,
            Binary = bin,
            Symbol = fuel,
            MapName = kind,
            Map = map,
            Address = -1,
            IsReadOnly = !target,
            IsAfrMap = true,
            SaveTo = !target ? null : data =>
            {
                if (idle)
                {
                    afr.SetIdleTargetAFRMapInBytes(data);
                    afr.SaveIdleMaps();
                }
                else
                {
                    afr.SetTargetAFRMapInBytes(data);
                    afr.SaveMaps();
                }
            },
            ReadFrom = target ? () => AfrContent(afr, kind) : null,
            ViewType = MapViewType.Easy,
            IsRedWhite = settings.ShowRedWhite,
            DisableColors = settings.DisableMapviewerColors,
            GraphVisible = settings.ShowGraphs,
            OpenLoopMark = (OpenLoopMark)Math.Clamp(settings.StandardFill, 0, 2),
        };
    }

    /// <summary>UpdateFeedbackMaps: the open feedback and error viewers show the maps as they are now.</summary>
    public void RefreshAfrViewers()
    {
        if (m_afr == null) return;
        foreach (MapViewerViewModel v in Viewers.OfType<MapViewerViewModel>().Where(v => v.IsAfrMap && v.MapName.Contains("Feedback") && v.FileName == m_afrFile))
        {
            byte[] data = AfrContent(m_afr, v.MapName);
            if (data.Length == v.Map.Count * 2) v.Map.Load(data);
        }
    }
}
