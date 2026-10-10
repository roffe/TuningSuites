using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
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

    private IgnitionMaps? m_ignition;
    private string? m_ignitionFile;

    /// <summary>The open bin's ignition autotune maps (IgnitionMaps folder next to it: feedback, counters, locked cells).</summary>
    public IgnitionMaps? IgnitionMaps
    {
        get
        {
            if (T5 is not { } bin) return null;
            if (m_ignition == null || m_ignitionFile != bin.FileName)
            {
                m_ignition = new IgnitionMaps { TrionicFile = bin.File };
                m_ignition.InitializeMaps();
                m_ignitionFile = bin.FileName;
            }
            return m_ignition;
        }
    }

    /// <summary>Release locked ignition cells: the lock map cleared and saved, no question, no message (T5Suite).</summary>
    [RelayCommand]
    private void ReleaseIgnitionLocks() => IgnitionMaps?.ClearIgnitionLockedMap();

    /// <summary>Ignition lock map: the cells the ignition autotune locked (1) on Ign_map_0!'s axes, read-only.</summary>
    [RelayCommand]
    private void ShowIgnitionLocks()
    {
        if (T5 is not { } bin || IgnitionMaps is not { } maps || bin.Find("Ign_map_0!") is not { } ign) return;
        int[] locks = maps.GetIgnitionLockedMap();
        var (_, _, xDescr, yDescr, _) = bin.AxisSymbols("Ign_map_0!");
        var map = new MapData("IgnitionLockMap", locks.Select(v => (byte)Math.Clamp(v, 0, 255)).ToArray(), bin.TableWidth("Ign_map_0!"), false)
        {
            UpsideDown = true,
            XAxis = bin.GetXaxisValues("Ign_map_0!").Select(v => (double)v).ToArray(),
            YAxis = bin.GetYaxisValues("Ign_map_0!").Select(v => (double)v).ToArray(),
            XName = xDescr,
            YName = yDescr,
            ZName = "locked",
        };
        ShowDocument(new MapViewerViewModel
        {
            Owner = this, Binary = bin, Symbol = ign, MapName = "IgnitionLockMap", Map = map, Address = -1, IsReadOnly = true,
            ViewType = MapViewType.Decimal, IsRedWhite = Settings.ShowRedWhite, DisableColors = Settings.DisableMapviewerColors, GraphVisible = false,
        });
    }

    protected override RealtimeViewModel CreateRealtimePanel(SuiteBinary bin) => new T5RealtimeViewModel(this, (T5Binary)bin);

    // ---- realtime user maps ----

    private ObservableCollection<UserMap>? m_userMaps;

    private string UserMapsFile => System.IO.Path.Combine(SettingsKey.Folder(Suite), "UserMaps.json");

    /// <summary>The realtime panel's User maps (T5Suite: UserMaps.xml in its app data folder), kept in the settings folder.</summary>
    public ObservableCollection<UserMap> RealtimeUserMaps
    {
        get
        {
            if (m_userMaps != null) return m_userMaps;
            List<UserMap> maps = [];
            try
            {
                if (System.IO.File.Exists(UserMapsFile))
                    maps = [.. (System.Text.Json.JsonSerializer.Deserialize<List<UserMap?>>(System.IO.File.ReadAllText(UserMapsFile)) ?? []).OfType<UserMap>().Where(m => m.Mapname != null)];
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
            }
            return m_userMaps = new ObservableCollection<UserMap>(maps);
        }
    }

    private void SaveUserMaps()
    {
        try
        {
            System.IO.Directory.CreateDirectory(SettingsKey.Folder(Suite));
            System.IO.File.WriteAllText(UserMapsFile, System.Text.Json.JsonSerializer.Serialize(RealtimeUserMaps.ToList()));
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            ShowInfo("Could not save the user maps: " + e.Message);
        }
    }

    /// <summary>"Add to realtime user maps" (AddToRealtimeUserMaps): once per map name, with its description.</summary>
    public void AddRealtimeUserMap(SymbolHelper sh)
    {
        if (RealtimeUserMaps.Any(m => m.Mapname == sh.SmartVarname)) return;
        RealtimeUserMaps.Add(new UserMap(sh.SmartVarname, sh.Description ?? ""));
        SaveUserMaps();
    }

    public void RemoveRealtimeUserMap(UserMap map)
    {
        RealtimeUserMaps.Remove(map);
        SaveUserMaps();
    }

    // ---- knock map snapshots ----

    /// <summary>The snapshots folder: the project's, else Snapshots next to the bin.</summary>
    public string? SnapshotFolder => Project is { } project ? System.IO.Path.Combine(project.Dir, "Snapshots")
        : T5 is { } bin ? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(bin.FileName) ?? "", "Snapshots") : null;

    /// <summary>
    /// "Knock counter snapshot after disconnect" (T5.5): Knock_count_map (576 bytes) as hex into Snapshots\Knockmap&lt;MMddyyyyHHmmss&gt;.KNK
    /// when the ECU disconnects.
    /// </summary>
    private async Task KnockSnapshotAsync()
    {
        if (!T5Settings.KnockCounterSnapshot || T5 is not { IsTrionic55: true } bin || !EcuConnected || SnapshotFolder is not { } folder
            || bin.Find("Knock_count_map") is not { Start_address: > 0 } sh || await Ecu.ReadMapAsync(sh) is not { Length: 576 } data) return;
        System.IO.Directory.CreateDirectory(folder);
        await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(folder, $"Knockmap{DateTime.Now:MMddyyyyHHmmss}.KNK"), Convert.ToHexString(data));
    }

    /// <summary>"Knock map snapshots": the .KNK files of 1152 hex characters, newest first, with their total of knocks.</summary>
    public List<(string File, DateTime Time, int Knocks)> KnockSnapshots()
    {
        if (SnapshotFolder is not { } folder || !System.IO.Directory.Exists(folder)) return [];
        return System.IO.Directory.GetFiles(folder, "*.KNK").Select(f => (f, Data: KnockData(f)))
            .Where(k => k.Data != null)
            .Select(k => (k.f, System.IO.File.GetLastWriteTime(k.f), Enumerable.Range(0, k.Data!.Length / 2).Sum(i => k.Data[i * 2] << 8 | k.Data[i * 2 + 1])))
            .OrderByDescending(k => k.Item2).ToList();
    }

    private static byte[]? KnockData(string file)
    {
        string text = System.IO.File.ReadAllText(file).Trim();
        try
        {
            return text.Length == 1152 ? Convert.FromHexString(text) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>ShowKnockMap: a snapshot as Knock_count_map on the ignition map's axes, or |a − b| per cell of two.</summary>
    public void ShowKnockMap(string file, string? compare = null)
    {
        if (T5 is not { } bin || bin.Find("Knock_count_map") is not { } sh || KnockData(file) is not { } data) return;
        string title = "Knock counter snapshot: " + System.IO.Path.GetFileName(file);
        if (compare != null && KnockData(compare) is { } other)
        {
            for (int i = 0; i + 1 < data.Length; i += 2)
            {
                int d = Math.Abs((data[i] << 8 | data[i + 1]) - (other[i] << 8 | other[i + 1]));
                data[i] = (byte)(d >> 8);
                data[i + 1] = (byte)d;
            }
            title = $"Knock counter difference: {System.IO.Path.GetFileName(file)} vs {System.IO.Path.GetFileName(compare)}";
        }
        var (_, _, xDescr, yDescr, _) = bin.AxisSymbols("Ign_map_0!");
        var map = new MapData("Knock_count_map", data, bin.TableWidth("Ign_map_0!"), true, 0xFFFF)
        {
            UpsideDown = true,
            XAxis = bin.GetXaxisValues("Ign_map_0!").Select(v => (double)v).ToArray(),
            YAxis = bin.GetYaxisValues("Ign_map_0!").Select(v => (double)v).ToArray(),
            XName = xDescr,
            YName = yDescr,
            ZName = "knocks",
        };
        ShowDocument(new MapViewerViewModel
        {
            Owner = this, Binary = bin, Symbol = sh, MapName = "Knock_count_map", Map = map, Address = -1, IsReadOnly = true, TitleOverride = title,
            ViewType = MapViewType.Decimal, IsRedWhite = Settings.ShowRedWhite, DisableColors = Settings.DisableMapviewerColors, GraphVisible = Settings.ShowGraphs,
        });
    }

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
