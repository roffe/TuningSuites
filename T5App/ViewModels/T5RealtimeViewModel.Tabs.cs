using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MapControls;
using SuiteApp.Controls;
using Trionic5Tools;

namespace T5App.ViewModels;

/// <summary>An engine status LED (Pgm_status bit).</summary>
public partial class StatusLed(string caption) : ObservableObject
{
    public string Caption { get; } = caption;

    [ObservableProperty] private bool _isOn;
}

/// <summary>A Pgm_mod! switch written to the ECU when clicked; IsOn shows what the ECU has.</summary>
public partial class EcuToggle(string caption, Func<EcuToggle, System.Threading.Tasks.Task> toggle) : ObservableObject
{
    public string Caption { get; } = caption;

    [ObservableProperty] private bool _isOn;

    [ObservableProperty] private bool _isAvailable = true;

    [RelayCommand]
    private System.Threading.Tasks.Task Toggle() => toggle(this);
}

/// <summary>A row of the Fuel tab's enrichment grid: one cylinder's byte of Lacc / Acc / Lret / Ret_mangd.</summary>
public partial class EnrichmentRow(string caption, string row, bool enlean) : ObservableObject
{
    public string Caption { get; } = caption;
    public string Row { get; } = row;
    public bool Enlean { get; } = enlean;

    [ObservableProperty] private double _value;
}

/// <summary>A cylinder on the Ignition and Knock tabs: knock count, its last increase, ignition offset and its peak.</summary>
public partial class KnockCylinder(int number) : ObservableObject
{
    public int Number { get; } = number;
    public string CountCaption => Number == 2 ? "Knock cyl #2" : $"Knocks cyl #{Number}"; // [sic]

    [ObservableProperty] private double _count;
    [ObservableProperty] private double _delta;
    // T5Suite's digits turned red at the first delta and stayed red (its fade timer never ran)
    [ObservableProperty] private bool _hasDelta;
    [ObservableProperty] private double _offset;
    [ObservableProperty] private double _peakOffset;

    internal double Previous = -1;
    internal double PeakSeen = -1;
}

/// <summary>A user map: a map the realtime panel's User maps tab opens.</summary>
public sealed record UserMap(string Mapname, string Description);

/// <summary>
/// T5Suite's realtime panel tabs (ctrlRealtime): each tab polls its own watch list (FillRealtimePool), and the displays keep the
/// last values of what isn't read on the current tab.
/// </summary>
public partial class T5RealtimeViewModel
{
    // ---- tabs and watch lists ----

    /// <summary>The panel tab, T5RealtimeTab's order.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAutotuneTab), nameof(TabMaps), nameof(HasTabMaps))]
    private int _selectedTab;

    // field initialisers only: the base constructor already calls Polled
    private T5RealtimeTab m_pollTab = T5RealtimeTab.Fuel;
    private bool m_firstList = true;
    private T5RealtimeTab m_mapsTab = T5RealtimeTab.Fuel;

    private T5RealtimeTab Tab => (T5RealtimeTab)SelectedTab;

    // the panel's own rows: FillRealtimePool added the tab's symbols whatever the user had removed from the table
    private readonly List<RealtimeSymbol> m_watchable = [];

    /// <summary>The tab's watch list plus the user rows on User defined and in the list of going online (Fuel and the user symbols).</summary>
    protected override IEnumerable<RealtimeSymbol> Polled(IEnumerable<RealtimeSymbol> rows)
    {
        IReadOnlySet<string> names = T5Realtime.WatchList(m_pollTab, m_tuning != null, m_ignition != null, T5Realtime.Rules.LambdaSymbol);
        bool user = m_pollTab == T5RealtimeTab.Userdefined || m_firstList;
        List<RealtimeSymbol> table = [.. rows];
        return table.Concat(m_watchable.Where(w => table.All(r => r.Name != w.Name)))
            .Where(r => !r.Derived && (names.Contains(r.Name) || user && r.UserDefined));
    }

    /// <summary>What the engine reads now: the polled rows' names (the tab's watch list as the table has it).</summary>
    public IReadOnlyList<string> PolledNames => [.. Polled(Rows.Select(r => r.Symbol)).Select(r => r.Name)];

    /// <summary>A display whose symbol this tab doesn't read keeps its last value (T5Suite's _last fields).</summary>
    protected override double Unpolled(string name) => Rows.FirstOrDefault(r => r.Name == name)?.Value ?? 0;

    // the tab switch refills the watch list; User maps keeps the previous tab's (FillRealtimePool isn't called for it)
    partial void OnSelectedTabChanged(int value)
    {
        var tab = (T5RealtimeTab)value;
        if (tab != T5RealtimeTab.AutotuneIgnition && tab != T5RealtimeTab.AutotuneFuel) m_mapsTab = tab == T5RealtimeTab.UserMaps ? m_mapsTab : tab;
        if (tab == T5RealtimeTab.UserMaps) return;
        m_pollTab = tab;
        m_firstList = false;
        PushRows();
    }

    public bool IsAutotuneTab => Tab == T5RealtimeTab.AutotuneFuel;

    /// <summary>Settings → Advanced mode: the Settings, User defined and autotune tabs, the autotune buttons and Edit maps.</summary>
    public bool AdvancedMode => m_t5.AdvancedMode;

    public void AdvancedModeChanged()
    {
        // the advanced tabs hide: off them (an autotune running keeps its tab)
        if (!AdvancedMode && !IsAnyAutotuning && Tab is T5RealtimeTab.Settings or T5RealtimeTab.Userdefined or T5RealtimeTab.AutotuneFuel or T5RealtimeTab.AutotuneIgnition)
            SelectedTab = (int)T5RealtimeTab.Fuel;
        OnPropertyChanged(nameof(AdvancedMode));
        OnPropertyChanged(nameof(CanAutotune));
        OnPropertyChanged(nameof(HasTabMaps));
    }

    public string IgnitionAutotuneCaption => IsIgnitionAutotuning ? "Tuning..." : "Autotune ignition";

    [RelayCommand]
    private System.Threading.Tasks.Task ToggleIgnitionAutotune() => ToggleIgnitionAutotuneAsync();

    // ---- the strip ----

    [ObservableProperty] private double _peakBoost = -1;
    [ObservableProperty] private bool _isIdleLed;
    [ObservableProperty] private bool _isClosedLoopLed;
    [ObservableProperty] private bool _isKnockLed;
    [ObservableProperty] private bool _isWarmupLed;
    [ObservableProperty] private double _targetAfr = 14.7;

    public string TargetAfrText => LambdaMode ? (TargetAfr / WidebandAfr.Stoich).ToString("F2") : TargetAfr.ToString("F1");

    partial void OnTargetAfrChanged(double value) => OnPropertyChanged(nameof(TargetAfrText));

    /// <summary>A click on Peak boost starts it again (T5Suite showed -1.00).</summary>
    [RelayCommand]
    private void ResetPeakBoost() => PeakBoost = -1;

    // ---- Fuel ----

    public ObservableCollection<EnrichmentRow> Enrichments { get; } =
    [
        .. Enumerable.Range(1, 4).Select(c => new EnrichmentRow($"Enrich load accel cyl #{c}", "LoadAccCyl" + c, false)),
        .. Enumerable.Range(1, 4).Select(c => new EnrichmentRow($"Enrich TPS accel cyl #{c}", "TPSAccCyl" + c, false)),
        .. Enumerable.Range(1, 4).Select(c => new EnrichmentRow($"Enlean load accel cyl #{c}", "LoadRetCyl" + c, true)),
        .. Enumerable.Range(1, 4).Select(c => new EnrichmentRow($"Enlean TPS accel cyl #{c}", "TPSRetCyl" + c, true)),
    ];

    [ObservableProperty] private double _injectionTime;
    [ObservableProperty] private double _injectorDc;

    public EcuToggle LambdaToggle => ToggleFor("Lambda control");
    public EcuToggle IdleLambdaToggle => ToggleFor("Lambda during idle");

    // ---- Ignition, Knock ----

    public IReadOnlyList<KnockCylinder> Cylinders { get; } = [.. Enumerable.Range(1, 4).Select(c => new KnockCylinder(c))];

    public EcuToggle KnockToggle => ToggleFor("Knock control");

    // ---- Boost ----

    [ObservableProperty] private double _boostRequest;
    [ObservableProperty] private double _boostTarget;
    [ObservableProperty] private double _boostError;
    [ObservableProperty] private double _boostReduction;
    [ObservableProperty] private double _pFactor;
    [ObservableProperty] private double _iFactor;
    [ObservableProperty] private double _dFactor;

    public EcuToggle ApcToggle => ToggleFor("APC control");

    // ---- Dashboard ----

    [ObservableProperty] private double _peakTorque;
    [ObservableProperty] private double _peakTorqueRpm;
    [ObservableProperty] private double _peakPower;
    [ObservableProperty] private double _peakPowerRpm;
    [ObservableProperty] private double _consumption;
    [ObservableProperty] private string _consumptionUnit = "km/l";

    // read once when the panel opens (GetTrionicProperties reads the file); ponytail: a firmware options change shows on the next panel
    private static int Cc(InjectorType type) => type switch
    {
        InjectorType.Stock => 365,
        InjectorType.GreenGiants => 413,
        InjectorType.Siemens630Dekas => 630,
        InjectorType.Siemens1000cc => 1000,
        _ => 875,
    };

    // ---- the values of a pass ----

    private void ShowTabValues(RealtimeSample sample)
    {
        double Last(string name) => sample[name] ?? Unpolled(name);
        long status = (long)Last("Pgm_status");
        IsIdleLed = (status & 0x40000000) != 0;
        IsClosedLoopLed = (status & 0x02000000) != 0;
        IsWarmupLed = (status & 0x10) == 0;
        IsKnockLed = Tab == T5RealtimeTab.AutotuneIgnition ? Last("Knock_offset1234") > 0 : (status & 0x200) != 0;
        if (sample["P_medel"] is { } boost && boost is <= 3 and >= -1 && boost > PeakBoost) PeakBoost = boost;

        foreach (EnrichmentRow e in Enrichments) e.Value = Last(e.Row);
        InjectionTime = Last("Insptid_ms10");
        InjectorDc = Rpm * InjectionTime / 1200;

        for (int c = 1; c <= 4; c++)
        {
            KnockCylinder cyl = Cylinders[c - 1];
            if (sample["Knock_count_cyl" + c] is { } count)
            {
                // the increase since the last change; none for the first value
                if (count != cyl.Previous)
                {
                    if (cyl.Previous >= 0)
                    {
                        cyl.Delta = count - cyl.Previous;
                        cyl.HasDelta = true;
                    }
                    cyl.Previous = count;
                }
                cyl.Count = count;
            }
            if (sample["Knock_offset" + c] is { } offset)
            {
                cyl.Offset = offset;
                if (offset > cyl.PeakSeen) cyl.PeakOffset = cyl.PeakSeen = offset;
            }
        }

        BoostRequest = Last("Max_tryck");
        BoostTarget = Last("Regl_tryck");
        BoostError = BoostTarget - Boost;
        BoostReduction = Last("Apc_decrese");
        PFactor = Last("P_fak");
        IFactor = Last("I_fak");
        DFactor = Last("D_fak");

        if (sample["TQ"] is { } torque && torque > PeakTorque)
        {
            PeakTorque = torque;
            PeakTorqueRpm = Rpm;
        }
        if (sample["TQ"] != null && Power > PeakPower)
        {
            PeakPower = Power;
            PeakPowerRpm = Rpm;
        }
        // injections per minute × cc per injection; T5Suite took the duty cycle (%) for the injection time (ms) here
        double litresPerHour = Rpm * 2 * (m_injectorCc / 60000.0 * InjectionTime) * 60 / 1000;
        ConsumptionUnit = Speed == 0 ? "l/h" : "km/l";
        Consumption = Speed == 0 ? litresPerHour : litresPerHour > 0 ? Speed / litresPerHour : 0;

        if (Tab == T5RealtimeTab.OnlineGraph) AddGraphPoint(sample);
        OnPropertyChanged(nameof(TargetAfrText));
    }

    // ---- Edit maps ----

    /// <summary>The Edit maps button's list for the tab (T5Suite's MapEditButton); maps the bin lacks are left out.</summary>
    public IReadOnlyList<UserMap> TabMaps => (Tab is T5RealtimeTab.AutotuneIgnition ? m_mapsTab : Tab) switch
    {
        T5RealtimeTab.Fuel => Existing([("Main fuel map", "Insp_mat!"), ("Fuel adaption", m_bin.IsTrionic55 ? "Adapt_korr!" : "Adapt_korr"),
            ("Knock fuel map", "Fuel_knock_mat!"), ("Injector constant", "Inj_konst!")]),
        T5RealtimeTab.Ignition => Existing([("Main ignition map", "Ign_map_0!"), ("Warmup ignition map", "Ign_map_4!"), ("Knock ignition map", "Ign_map_2!")]),
        T5RealtimeTab.Boost => Existing([("Boost request map", "Tryck_mat!"), ("Boost bias map", "Reg_kon_mat!"), ("P factors", "P_fors!"),
            ("I factors", "I_fors!"), ("D factors", "D_fors!")]),
        T5RealtimeTab.Knock => Existing([("Knock sensitivity", m_bin.IsTrionic55 ? "Knock_ref_matrix!" : "Knock_ref_tab!"), ("Knock counter map", "Knock_count_map"),
            ("Knock ignition map", "Ign_map_2!"), ("Knock fuel map", "Fuel_knock_mat!")]),
        _ => [],
    };

    public bool HasTabMaps => AdvancedMode && Tab != T5RealtimeTab.AutotuneFuel && TabMaps.Count > 0;

    private List<UserMap> Existing(IEnumerable<(string Caption, string Symbol)> maps) =>
        maps.Where(m => m_bin.Find(m.Symbol) != null).Select(m => new UserMap(m.Symbol, m.Caption)).ToList();

    [RelayCommand]
    private void OpenMap(string symbol) => m_t5.OpenSymbolByName(symbol);

    // ---- Graph ----

    /// <summary>T5Suite's online graph lines: name, symbol (null: the lambda input), range, colour.</summary>
    private static readonly (string Name, string? Symbol, double Min, double Max, Color Color)[] GraphLines =
    [
        ("Rpm", "Rpm", 0, 8000, Colors.Crimson), ("Boost", "P_medel", -1, 2.5, Colors.Red), ("AFR", null, 7, 24, Colors.AntiqueWhite),
        ("IAT", "Lufttemp", -40, 100, Colors.BlueViolet), ("CT", "Kyl_temp", -40, 120, Colors.Cornsilk), ("Inj.dur", "Insptid_ms10", 0, 30, Colors.AliceBlue),
        ("Ign", "Ign_angle", -10, 45, Colors.Pink), ("PWM", "PWM_ut10", 0, 100, Colors.PowderBlue), ("TQ", "TQ", 0, 800, Colors.SlateGray),
    ];

    private readonly Queue<double>[] m_graph = [.. Enumerable.Range(0, 9).Select(_ => new Queue<double>())];
    private readonly Queue<DateTime> m_graphTimes = new();

    /// <summary>A new point on the Graph tab (the view redraws).</summary>
    public event Action? GraphChanged;

    // the last 100 samples of each line, only while the Graph tab shows (OnlineGraphControl)
    private void AddGraphPoint(RealtimeSample sample)
    {
        m_graphTimes.Enqueue(sample.Time);
        if (m_graphTimes.Count > 100) m_graphTimes.Dequeue();
        for (int i = 0; i < GraphLines.Length; i++)
        {
            double v = GraphLines[i].Symbol is { } symbol ? sample[symbol] ?? Unpolled(symbol) : AfrValue;
            m_graph[i].Enqueue(v);
            if (m_graph[i].Count > 100) m_graph[i].Dequeue();
        }
        GraphChanged?.Invoke();
    }

    /// <summary>The graph's lines with T5Suite's fixed ranges, in seconds from the oldest sample (Start).</summary>
    public (IReadOnlyList<LogChannel> Channels, DateTime Start) GraphChannels()
    {
        DateTime start = m_graphTimes.Count > 0 ? m_graphTimes.Peek() : DateTime.Now;
        double[] time = [.. m_graphTimes.Select(t => (t - start).TotalSeconds)];
        return ([.. GraphLines.Select((l, i) => new LogChannel(l.Symbol ?? "AFR", l.Name, l.Color, time, [.. m_graph[i]], l.Min, l.Max))], start);
    }

    // ---- User maps ----

    /// <summary>The User maps tab: maps added from the symbol list ("Add to realtime user maps").</summary>
    public ObservableCollection<UserMap> UserMaps => m_t5.RealtimeUserMaps;

    public void RemoveUserMap(UserMap map) => m_t5.RemoveRealtimeUserMap(map);

    // ---- Autotune grids ----

    private MapData? m_feedbackGrid, m_fuelGrid, m_ignitionGrid;

    /// <summary>The Autotune tab's left grid: the feedback AFR (T5Suite's showed AFR in lambda mode too).</summary>
    public MapData? FeedbackGrid
    {
        get => m_feedbackGrid;
        private set => SetProperty(ref m_feedbackGrid, value);
    }

    /// <summary>The Autotune tab's right grid: the fuel map being tuned as a multiplier (raw / 256 + 0.5).</summary>
    public MapData? FuelGrid
    {
        get => m_fuelGrid;
        private set => SetProperty(ref m_fuelGrid, value);
    }

    /// <summary>The Autotune ignition tab's grid: Ign_map_0! as the autotune has it, in degrees.</summary>
    public MapData? IgnitionGrid
    {
        get => m_ignitionGrid;
        private set => SetProperty(ref m_ignitionGrid, value);
    }

    private MapData Grid(string name, string axesOf, byte[] data, bool sixteenBit, double factor, double offset)
    {
        var (_, _, xDescr, yDescr, _) = m_bin.AxisSymbols(axesOf);
        return new MapData(name, data, Math.Max(1, m_bin.TableWidth(axesOf)), sixteenBit, sixteenBit ? 32000 : null)
        {
            Factor = factor,
            Offset = offset,
            UpsideDown = true,
            XAxis = [.. m_bin.GetXaxisValues(axesOf).Select(v => (double)v)],
            YAxis = [.. m_bin.GetYaxisValues(axesOf).Select(v => (double)v)],
            XName = xDescr,
            YName = yDescr,
        };
    }

    // the grids follow the autotunes (UpdateMutatedFuelMap, UpdateFeedbackAFR, UpdateMutatedIgnitionMap)
    private void ShowAutotuneGrids()
    {
        if (m_tuning is { } maps && FuelMap is { } fuel)
        {
            // AFR to one decimal, rounded as T5Suite's F1 (GetFeedbackAFRMapinBytes rounds up)
            byte[] feedback = [.. maps.GetFeedbackAFRMap().Select(f => (int)Math.Round(f * 10, MidpointRounding.AwayFromZero)).SelectMany(v => new[] { (byte)(v >> 8), (byte)v })];
            byte[] mutated = maps.GetCurrentlyMutatedFuelMap();
            if (FeedbackGrid is { } f && f.Count * 2 == feedback.Length) f.Load(feedback);
            else FeedbackGrid = Grid("FeedbackAFR", fuel.SmartVarname, feedback, true, 0.1, 0);
            if (FuelGrid is { } g && g.Count == mutated.Length) g.Load(mutated);
            else FuelGrid = Grid(fuel.SmartVarname, fuel.SmartVarname, mutated, false, 1 / 256.0, 0.5);
        }
        if (m_ignition is { } ignition)
        {
            byte[] map = Bytes(ignition.GetCurrentlyMutatedIgnitionMap());
            if (IgnitionGrid is { } i && i.Count * 2 == map.Length) i.Load(map);
            else IgnitionGrid = Grid("Ign_map_0!", "Ign_map_0!", map, true, 0.1, 0);
        }
    }
}
