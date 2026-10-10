using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SuiteApp.ViewModels;

/// <summary>A row of the Free logging grid.</summary>
public partial class RealtimeRow(RealtimeSymbol symbol) : ObservableObject
{
    public RealtimeSymbol Symbol { get; } = symbol;
    public string Name => Symbol.Name;
    public string SymbolNumberText => Symbol.SymbolNumber.ToString();
    public string SramText => Symbol.SramAddress.ToString("X6");

    public string Description
    {
        get => Symbol.Description;
        set => SetProperty(Symbol.Description, value, Symbol, (s, v) => s.Description = v);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Fraction))]
    private double _value;

    [ObservableProperty]
    private double _peak;

    /// <summary>The value's place between minimum and maximum, the cell's bar.</summary>
    public double Fraction => Symbol.Maximum > Symbol.Minimum ? Math.Clamp((Value - Symbol.Minimum) / (Symbol.Maximum - Symbol.Minimum), 0, 1) : 0;

    public void Refresh()
    {
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(SymbolNumberText));
        OnPropertyChanged(nameof(SramText));
        OnPropertyChanged(nameof(Fraction));
    }
}

/// <summary>
/// The realtime panel (dashboard, Free logging, bottom panel), a document tab here. While it is open the suite's engine polls
/// the table, the day's log gets a line per pass and open map viewers show the engine's cell. What only T7Suite had (AutoTune,
/// the AFR maps, Eco / Norm / Sport) is its subclass's.
/// </summary>
public partial class RealtimeViewModel : DocumentViewModel
{
    protected readonly MainWindowViewModel m_owner;
    protected readonly SuiteBinary m_bin;
    protected readonly RealtimeRules m_rules;
    protected readonly RealtimeEngine m_engine;
    private readonly CellTracker m_tracker;
    private CancellationTokenSource? m_cancel;
    private RealtimeLogWriter? m_log;
    private volatile bool m_marker;
    private WidebandSupport.IWidebandReader? m_wideband;

    public override string Title => "Realtime panel";

    public ObservableCollection<RealtimeRow> Rows { get; } = [];

    [ObservableProperty]
    private RealtimeRow? _selectedRow;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _fpsText = "";

    // ---- the dashboard and the bottom panel ----

    [ObservableProperty] private double _speed;
    [ObservableProperty] private double _torque;
    [ObservableProperty] private double _ignitionOffset;
    [ObservableProperty] private double _power;
    [ObservableProperty] private double _airmassRequest;
    [ObservableProperty] private double _boost;
    [ObservableProperty] private double _dutyCycle;
    [ObservableProperty] private double _ignitionAdvance;
    [ObservableProperty] private double _tps;
    [ObservableProperty] private double _airmass;
    [ObservableProperty] private double _rpm;
    [ObservableProperty] private double _coolant;
    [ObservableProperty] private double _intakeAir;
    [ObservableProperty] private double _egt;
    [ObservableProperty] private double _activeAirDemand;
    [ObservableProperty] private double _fuelConsumption;
    [ObservableProperty] private double _lambda = 1;
    [ObservableProperty] private string _airmassLimiter = "";
    [ObservableProperty] private string _lambdaStatus = "";
    [ObservableProperty] private string _fuelcutStatus = "";

    /// <summary>The AFR / λ display: AFR mode shows λ × 14.7 (T7Suite's digits showed λ in both modes).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AfrValue), nameof(AfrText), nameof(AfrCaption), nameof(AfrMinimum), nameof(AfrMaximum), nameof(AfrFormat))]
    private bool _lambdaMode;

    public double AfrValue => LambdaMode ? Lambda : Lambda * 14.7;
    public string AfrText => AfrValue.ToString(AfrFormat);
    public string AfrCaption => LambdaMode ? "λ" : "AFR";
    public double AfrMinimum => LambdaMode ? 0.5 : 10;
    public double AfrMaximum => LambdaMode ? 1.5 : 20;
    public string AfrFormat => LambdaMode ? "F2" : "F1";

    partial void OnLambdaChanged(double value)
    {
        OnPropertyChanged(nameof(AfrValue));
        OnPropertyChanged(nameof(AfrText));
    }

    public bool HasEgt { get; }

    /// <summary>The airmass displays (requested, actual, air demand, limiter) and the consumption: greyed when the suite has no such symbols (T5).</summary>
    public bool HasAirmass => m_rules.Symbols.Airmass != "";

    public bool HasConsumption => m_rules.Symbols.FuelConsumption != "";

    [ObservableProperty]
    private bool _isNight;

    partial void OnIsNightChanged(bool value) => m_owner.Settings.Panelmode = value ? PanelMode.Night : PanelMode.Day;

    public RealtimeViewModel(MainWindowViewModel owner, SuiteBinary bin, RealtimeRules rules, RealtimeEngine engine)
    {
        m_owner = owner;
        m_bin = bin;
        m_rules = rules;
        m_engine = engine;
        m_engine.Sample += OnSample;
        HasEgt = Realtime.HasEgtCalculation(bin);
        _isNight = owner.Settings.Panelmode == PanelMode.Night;
        _lambdaMode = owner.Settings.MeasureAFRInLambda;
        foreach (RealtimeSymbol s in Realtime.Merge(rules.Dashboard(bin, owner.Settings), Realtime.LoadLayout(LayoutFile, bin)))
            Rows.Add(new RealtimeRow(s));
        // after Dashboard: T5's rules follow the bin's MAP sensor
        m_tracker = new CellTracker(bin, rules.CellRules);
        PushRows();
    }

    /// <summary>rtsymbols.txt: the user rows, kept between sessions.</summary>
    public string LayoutFile => Path.Combine(SettingsKey.Folder(m_owner.Suite), "rtsymbols.txt");

    /// <summary>"t7rtl": Save / Load layout's files.</summary>
    public string LayoutExtension => m_rules.LayoutExtension;

    /// <summary>The table to the engine: the rows Polled picks, in table order.</summary>
    protected void PushRows() => m_engine.Rows = Polled(Rows.Select(r => r.Symbol)).ToList();

    /// <summary>
    /// The rows the engine reads: every row (T7Suite, T8Suite); T5Suite read only the panel tab's watch list. Called from this
    /// constructor too, so an override may only use field initialisers.
    /// </summary>
    protected virtual IEnumerable<RealtimeSymbol> Polled(IEnumerable<RealtimeSymbol> rows) => rows;

    private void SaveUserRows()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LayoutFile)!);
        Realtime.SaveLayout(LayoutFile, Rows.Select(r => r.Symbol));
    }

    /// <summary>ToggleRealtimePanel (show): connect, let the engine take the session and poll.</summary>
    public async Task StartAsync()
    {
        if (IsRunning || !await m_owner.EnsureConnectedAsync()) return;
        await m_engine.BeginAsync();
        StartWideband();
        m_log = new RealtimeLogWriter(m_bin.FileName, m_rules.LogExtension);
        m_cancel = new CancellationTokenSource();
        IsRunning = true;
        try
        {
            await m_engine.RunAsync(m_cancel.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            m_owner.ShowInfo("Realtime stopped: " + e.Message);
        }
        finally
        {
            IsRunning = false;
            FpsText = "";
            lock (this)
            {
                m_log?.Dispose();
                m_log = null;
            }
            StopWideband();
            await OnStoppedAsync();
            await m_engine.EndAsync();
            foreach (MapViewerViewModel v in m_owner.Viewers.OfType<MapViewerViewModel>()) v.LiveCell = null;
        }
    }

    /// <summary>After the last pass (T7Suite: autotune stops, the AFR maps are saved).</summary>
    protected virtual Task OnStoppedAsync() => Task.CompletedTask;

    /// <summary>"Use wideband O2 on com port": WidebandSupport's reader, its AFR (or λ) as "Wideband" in every pass.</summary>
    private void StartWideband()
    {
        AppSettings s = m_owner.Settings;
        if (!s.UseDigitalWidebandLambda) return;
        try
        {
            m_wideband = new WidebandSupport.WidebandFactory(s.WidebandDevice, s.WbPort, false).CreateInstance();
            m_wideband.Start();
        }
        catch (Exception e)
        {
            m_wideband = null;
            m_owner.ShowInfo("Wideband error: " + e.Message);
            return;
        }
        bool lambda = s.MeasureAFRInLambda;
        WidebandSupport.IWidebandReader reader = m_wideband;
        m_engine.Extra = () => [("Wideband", Math.Round(lambda ? reader.LatestReading / WidebandAfr.Stoich : reader.LatestReading, 2))];
    }

    private void StopWideband()
    {
        m_engine.Extra = null;
        try
        {
            m_wideband?.Stop();
        }
        catch (InvalidOperationException)
        {
        }
        m_wideband?.Dispose();
        m_wideband = null;
    }

    /// <summary>ToggleRealtimePanel (hide) and closing the tab.</summary>
    public void Stop()
    {
        m_cancel?.Cancel();
        SaveUserRows();
    }

    public override void Closed() => Stop();

    /// <summary>Write log marker [F6]: the next line gets IMPORTANTLINE=1.</summary>
    [RelayCommand]
    private void WriteLogMarker() => m_marker = true;

    // on the ECU thread: the log line, then the screen
    private void OnSample(RealtimeSample sample)
    {
        lock (this)
        {
            if (m_log != null)
            {
                try
                {
                    m_log.Write(sample, m_marker);
                    m_marker = false;
                }
                catch (IOException e)
                {
                    m_log = null;
                    Dispatcher.UIThread.Post(() => m_owner.ShowInfo("Logging stopped: " + e.Message));
                }
            }
        }
        Dispatcher.UIThread.Post(() => Apply(sample));
    }

    /// <summary>A pass on screen: rows, peaks, the dashboard, the decoded statuses and the live cells.</summary>
    public void Apply(RealtimeSample sample)
    {
        var byName = Rows.ToDictionary(r => r.Name);
        foreach (var (name, value) in sample.Values)
        {
            if (!byName.TryGetValue(name, out RealtimeRow? row))
            {
                // KnockCyl1..4 / MisfCyl1..4 from the per-cylinder counters, the serial wideband
                row = new RealtimeRow(new RealtimeSymbol { Name = name, Maximum = 65535, Derived = true });
                Rows.Add(row);
                byName[name] = row;
            }
            row.Value = value;
            if (row.Peak < value) row.Peak = value;
        }
        double V(string name) => sample[name] ?? Unpolled(name);
        DashboardSymbols names = m_rules.Symbols;
        Speed = V(names.Speed);
        Torque = V(names.Torque);
        IgnitionOffset = V(names.IgnitionOffset);
        AirmassRequest = V(names.AirmassRequest);
        Boost = V(names.Boost);
        DutyCycle = V(names.DutyCycle);
        IgnitionAdvance = V(names.IgnitionAdvance);
        Tps = V(names.Tps);
        Airmass = V(names.Airmass);
        Rpm = V(names.Rpm);
        Coolant = V(names.Coolant);
        IntakeAir = V(names.IntakeAir);
        Egt = V(names.Egt);
        ActiveAirDemand = V(names.ActiveAirDemand);
        FuelConsumption = V(names.FuelConsumption);
        Power = sample[names.Power] ?? Realtime.Power(Rpm, Torque);
        AppSettings s = m_owner.Settings;
        double? afr = s.UseWidebandLambda ? WidebandAfr.SymbolAfr(sample, s)
            : s.UseDigitalWidebandLambda && sample["Wideband"] is { } wb ? (s.MeasureAFRInLambda ? wb * WidebandAfr.Stoich : wb) : null;
        if (afr is { } a)
        {
            Lambda = a / WidebandAfr.Stoich;
            OnAfr(a, V(names.Fuelcut));
        }
        else if (sample[names.LambdaInt] is { } lambda) Lambda = lambda;
        // the low 32 bits: T5's Pgm_status is a 48-bit field
        int Status(string name) => unchecked((int)(long)V(name));
        AirmassLimiter = m_rules.AirDemand((int)ActiveAirDemand);
        LambdaStatus = m_rules.Lambda(Status(names.LambdaStatus));
        FuelcutStatus = m_rules.Fuelcut(Status(names.Fuelcut));
        if (sample.PerformanceMode is { } mode) PerformanceMode = mode;
        FpsText = $"{sample.Fps:F1} fps";

        double Input(CellInput i) => i switch
        {
            CellInput.Rpm => Rpm,
            CellInput.Airmass => Airmass,
            CellInput.Tps => Tps,
            CellInput.Torque => Torque,
            CellInput.Boost => Boost,
            _ => IgnitionOffset,
        };
        foreach (MapViewerViewModel v in m_owner.Viewers.OfType<MapViewerViewModel>().Where(v => v.FileName == m_bin.FileName))
            v.LiveCell = m_tracker.Cell(v.MapName, Input) is var (col, row) ? new Avalonia.PixelPoint(col, row) : null;
        OnApplied(sample);
    }

    /// <summary>A dashboard value the pass didn't read: 0 (T7 / T8 read every row, so the bin lacks it); T5 keeps the row's last value.</summary>
    protected virtual double Unpolled(string name) => 0;

    /// <summary>After a pass is on screen (T5: the AFR maps and the autotune, which need the pass's other values).</summary>
    protected virtual void OnApplied(RealtimeSample sample)
    {
    }

    /// <summary>A pass's wideband AFR (T7Suite: into the AFR maps and the autotune).</summary>
    protected virtual void OnAfr(double afr, double fuelcut)
    {
    }

    [RelayCommand]
    private void ToggleAfrMode() => LambdaMode = !LambdaMode;

    [RelayCommand]
    private void ToggleNight() => IsNight = !IsNight;

    // ---- what only T7Suite had: AutoTune and Eco / Norm / Sport ----

    /// <summary>The AutoTune button shows.</summary>
    public virtual bool CanAutotune => false;

    [ObservableProperty]
    private string _autotuneCaption = "AutoTune";

    [ObservableProperty]
    private bool _isAutotuning;

    [RelayCommand]
    protected virtual Task ToggleAutotune() => Task.CompletedTask;

    public virtual bool HasPerformanceMode => false;

    /// <summary>0 eco, 1 normal, 2 sport, null unknown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEco), nameof(IsNormal), nameof(IsSport))]
    private int? _performanceMode;

    public bool IsEco => PerformanceMode == 0;
    public bool IsNormal => PerformanceMode == 1;
    public bool IsSport => PerformanceMode == 2;

    [RelayCommand]
    protected virtual Task SetPerformanceMode(string mode) => Task.CompletedTask;

    // ---- Free logging ----

    /// <summary>Add symbol / Add to realtime list: replaces a row of the same name.</summary>
    public void Add(RealtimeSymbol symbol)
    {
        symbol.UserDefined = true;
        if (Rows.FirstOrDefault(r => r.Name == symbol.Name) is { } existing) Rows[Rows.IndexOf(existing)] = new RealtimeRow(symbol) { Peak = symbol.Minimum };
        else Rows.Add(new RealtimeRow(symbol) { Peak = symbol.Minimum });
        PushRows();
        SaveUserRows();
    }

    /// <summary>Edit symbol: the row's settings changed; the peak starts again at the minimum.</summary>
    public void Edited(RealtimeRow row)
    {
        row.Symbol.UserDefined = true;
        row.Peak = row.Symbol.Minimum;
        row.Refresh();
        PushRows();
        SaveUserRows();
    }

    public void Remove(IEnumerable<RealtimeRow> rows)
    {
        foreach (RealtimeRow r in rows.ToList()) Rows.Remove(r);
        PushRows();
        SaveUserRows();
    }

    /// <summary>Ctrl+Up / Ctrl+Down: the row moves, and with it the poll order.</summary>
    public void Move(RealtimeRow row, int by)
    {
        int i = Rows.IndexOf(row), j = i + by;
        if (i < 0 || j < 0 || j >= Rows.Count) return;
        Rows.Move(i, j);
        PushRows();
    }

    /// <summary>Reset peak values (hidden in the suites): every peak back to the row's minimum.</summary>
    [RelayCommand]
    private void ResetPeaks()
    {
        foreach (RealtimeRow r in Rows) r.Peak = r.Symbol.Minimum;
    }

    public void SaveLayout(string file) => Realtime.SaveLayout(file, Rows.Select(r => r.Symbol));

    /// <summary>Load layout: the dashboard rows and the layout's.</summary>
    public void LoadLayout(string file)
    {
        Rows.Clear();
        foreach (RealtimeSymbol s in Realtime.Merge(m_rules.Dashboard(m_bin, m_owner.Settings), Realtime.LoadLayout(file, m_bin)))
            Rows.Add(new RealtimeRow(s));
        PushRows();
        SaveUserRows();
    }

    /// <summary>The symbol names offered by the add / edit dialog.</summary>
    public IEnumerable<string> SymbolNames => m_bin.Symbols.Cast<SymbolHelper>().Select(s => s.SmartVarname).Distinct().OrderBy(n => n);

    public RealtimeSymbol? Lookup(string name) => m_bin.FindAny(name) is { } sh ? m_rules.FromSymbol(sh) : null;
}

/// <summary>frmEditRealtimeSymbol: the symbol (only when adding), description, range, offset and correction.</summary>
public partial class RealtimeSymbolEdit : ObservableObject
{
    public IEnumerable<string> Names { get; init; } = [];
    public bool IsNew { get; init; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private double _minimum;
    [ObservableProperty] private double _maximum = 255;
    [ObservableProperty] private double _offset;
    [ObservableProperty] private double _correction = 1;

    public static RealtimeSymbolEdit From(RealtimeSymbol s) => new()
    {
        Name = s.Name, Description = s.Description, Minimum = s.Minimum, Maximum = s.Maximum, Offset = s.Offset, Correction = s.Correction,
    };

    public void ApplyTo(RealtimeSymbol s)
    {
        s.Description = Description;
        s.Minimum = Minimum;
        s.Maximum = Maximum;
        s.Offset = Offset;
        s.Correction = Correction;
    }
}
