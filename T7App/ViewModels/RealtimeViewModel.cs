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
using T7;

namespace T7App.ViewModels;

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
/// The realtime panel (dashboard, Free logging, bottom panel), a document tab here. While it is open the engine polls the
/// table, the day's .t7l log gets a line per pass and open map viewers show the engine's cell.
/// </summary>
public partial class RealtimeViewModel : DocumentViewModel
{
    private readonly MainWindowViewModel m_owner;
    private readonly T7Binary m_bin;
    private readonly RealtimeEngine m_engine;
    private readonly CellTracker m_tracker;
    private CancellationTokenSource? m_cancel;
    private T7LogWriter? m_log;
    private volatile bool m_marker;
    private WidebandSupport.IWidebandReader? m_wideband;
    private Autotune? m_autotune;

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

    [ObservableProperty]
    private bool _isNight;

    partial void OnIsNightChanged(bool value) => m_owner.Settings.Panelmode = value ? PanelMode.Night : PanelMode.Day;

    public bool HasPerformanceMode => m_engine.PerformanceMode != null;

    /// <summary>0 eco, 1 normal, 2 sport, null unknown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEco), nameof(IsNormal), nameof(IsSport))]
    private int? _performanceMode;

    public bool IsEco => PerformanceMode == 0;
    public bool IsNormal => PerformanceMode == 1;
    public bool IsSport => PerformanceMode == 2;

    public RealtimeViewModel(MainWindowViewModel owner, T7Binary bin)
    {
        m_owner = owner;
        m_bin = bin;
        m_engine = new RealtimeEngine(owner.Ecu);
        m_tracker = new CellTracker(bin);
        if (bin.FindAny("Performance.Mode") is { Start_address: > 0 } mode) m_engine.PerformanceMode = mode;
        m_engine.Sample += OnSample;
        HasEgt = Realtime.HasEgtCalculation(bin);
        _isNight = owner.Settings.Panelmode == PanelMode.Night;
        _lambdaMode = owner.Settings.MeasureAFRInLambda;
        foreach (RealtimeSymbol s in Realtime.Merge(Realtime.Dashboard(bin, owner.Settings), Realtime.LoadLayout(LayoutFile, bin)))
            Rows.Add(new RealtimeRow(s));
        PushRows();
    }

    /// <summary>rtsymbols.txt: the user rows, kept between sessions.</summary>
    public string LayoutFile => Path.Combine(SettingsKey.Folder(MainWindowViewModel.Suite), "rtsymbols.txt");

    private void PushRows() => m_engine.Rows = Rows.Select(r => r.Symbol).ToList();

    private void SaveUserRows()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LayoutFile)!);
        Realtime.SaveLayout(LayoutFile, Rows.Select(r => r.Symbol));
    }

    /// <summary>ToggleRealtimePanel (show): connect, stop the keep-alive (the passes keep the session alive) and poll.</summary>
    public async Task StartAsync()
    {
        if (IsRunning || !await m_owner.EnsureConnectedAsync()) return;
        await m_owner.Ecu.RunAsync(t => t.SuspendAlivePolling());
        StartWideband();
        m_log = new T7LogWriter(m_bin.FileName);
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
            if (m_autotune != null) await StopAutotuneAsync();
            m_owner.AfrMaps?.Save();
            if (m_owner.Ecu.IsConnected) await m_owner.Ecu.RunAsync(t => t.ResumeAlivePolling());
            foreach (MapViewerViewModel v in m_owner.Viewers.OfType<MapViewerViewModel>()) v.LiveCell = null;
        }
    }

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
        m_engine.Extra = () => [("Wideband", Math.Round(lambda ? reader.LatestReading / AfrFeedback.Stoich : reader.LatestReading, 2))];
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
                // KnockCyl1..4 / MisfCyl1..4 from the per-cylinder counters
                row = new RealtimeRow(new RealtimeSymbol { Name = name, Maximum = 65535 });
                Rows.Add(row);
                byName[name] = row;
            }
            row.Value = value;
            if (row.Peak < value) row.Peak = value;
        }
        double V(string name) => sample[name] ?? 0;
        Speed = V("In.v_Vehicle");
        Torque = V("Out.M_Engine");
        IgnitionOffset = V("IgnProt.fi_Offset");
        AirmassRequest = V("m_Request");
        Boost = V("In.p_AirInlet");
        DutyCycle = V("Out.PWM_BoostCntrl");
        IgnitionAdvance = V("Out.fi_Ignition");
        Tps = V("Out.X_AccPedal");
        Airmass = V("MAF.m_AirInlet");
        Rpm = V("ActualIn.n_Engine");
        Coolant = V("ActualIn.T_Engine");
        IntakeAir = V("ActualIn.T_AirInlet");
        Egt = V("Exhaust.T_Calc");
        ActiveAirDemand = V("ECMStat.ST_ActiveAirDem");
        FuelConsumption = V("BFuelProt.CurrentFuelCon");
        Power = sample["ECMStat.P_Engine"] ?? Realtime.Power(Rpm, Torque);
        AppSettings s = m_owner.Settings;
        double? afr = s.UseWidebandLambda ? AfrFeedback.SymbolAfr(sample, s)
            : s.UseDigitalWidebandLambda && sample["Wideband"] is { } wb ? (s.MeasureAFRInLambda ? wb * AfrFeedback.Stoich : wb) : null;
        if (afr is { } a)
        {
            Lambda = a / AfrFeedback.Stoich;
            if (s.AutoCreateAFRMaps && m_owner.AfrMaps is { } maps && maps.Add(a, s.MeasureAFRInLambda, Rpm, Airmass, V("FCut.CutStatus")))
                m_owner.RefreshAfrViewers();
            m_autotune?.Handle(a, Rpm, Airmass);
        }
        else if (sample["Lambda.LambdaInt"] is { } lambda) Lambda = lambda;
        AirmassLimiter = RealtimeStatus.AirDemand((int)ActiveAirDemand);
        LambdaStatus = RealtimeStatus.Lambda((int)V("Lambda.Status"));
        FuelcutStatus = RealtimeStatus.Fuelcut((int)V("FCut.CutStatus"));
        if (sample.PerformanceMode is { } mode) PerformanceMode = mode;
        FpsText = $"{sample.Fps:F1} fps";

        double Input(CellTracker.Input i) => i switch
        {
            CellTracker.Input.Rpm => Rpm,
            CellTracker.Input.Airmass => Airmass,
            CellTracker.Input.Tps => Tps,
            CellTracker.Input.Torque => Torque,
            _ => IgnitionOffset,
        };
        foreach (MapViewerViewModel v in m_owner.Viewers.OfType<MapViewerViewModel>().Where(v => v.FileName == m_bin.FileName))
            v.LiveCell = m_tracker.Cell(v.MapName, Input) is var (col, row) ? new Avalonia.PixelPoint(col, row) : null;
    }

    // ---- autotune ----

    /// <summary>The AutoTune button shows for open binaries only.</summary>
    public bool CanAutotune => m_bin.IsSoftwareOpen;

    [ObservableProperty]
    private string _autotuneCaption = "AutoTune";

    [ObservableProperty]
    private bool _isAutotuning;

    /// <summary>btnAutoTune: start (open bin, wideband, coolant ≥ 70 °C) or stop.</summary>
    [RelayCommand]
    private async Task ToggleAutotune()
    {
        if (AutotuneCaption == "Wait...") return;
        if (m_autotune != null)
        {
            await StopAutotuneAsync();
            return;
        }
        if (Autotune.CannotStart(m_bin, m_owner.Settings, Coolant) is { } reason)
        {
            m_owner.ShowInfo(reason);
            return;
        }
        if (!IsRunning || m_owner.AfrMaps is not { } afr) return;
        AutotuneCaption = "Wait...";
        m_owner.ProgressText = "Starting autotune...";
        m_autotune = await Autotune.StartAsync(afr, m_owner.Ecu, m_owner.Settings);
        IsAutotuning = m_autotune != null;
        AutotuneCaption = IsAutotuning ? "Tuning..." : "AutoTune";
        m_owner.ProgressText = IsAutotuning ? "Autotune running..." : "Autotune init failed.";
    }

    /// <summary>
    /// Stop: the switches back. With auto update "Keep adjusted fuel map?" (yes: SRAM into the file, no: the original back
    /// into SRAM); without, the proposed changes to accept, written to SRAM and the file. The file gets a transaction entry and
    /// one checksum update (T7Suite updated the checksum per cell and logged nothing).
    /// </summary>
    private async Task StopAutotuneAsync()
    {
        if (m_autotune is not { } tune) return;
        m_autotune = null;
        IsAutotuning = false;
        AutotuneCaption = "Wait...";
        try
        {
            await tune.RestoreAsync();
            byte[]? keep = null;
            if (tune.AutoUpdate)
            {
                bool? answer = m_owner.AskYesNoCancel == null ? true : await m_owner.AskYesNoCancel("Keep adjusted fuel map?");
                if (answer == true) keep = await m_owner.Ecu.ReadMapAsync(tune.FuelMap);
                else if (answer == false) await m_owner.Ecu.WriteMapAsync(tune.FuelMap, tune.Original);
            }
            else if (m_owner.AcceptAutotune is { } accept && await accept(tune.Differences) is { Count: > 0 } cells)
            {
                keep = Autotune.Accept(tune.Original, tune.Differences, cells);
                await m_owner.Ecu.WriteMapAsync(tune.FuelMap, keep);
            }
            if (keep != null)
            {
                int before = m_owner.TransactionLog?.TransCollection.Count ?? 0;
                m_bin.WriteSymbol(m_bin.FileAddress(tune.FuelMap), keep, m_owner.Settings.AutoFixFooter, m_owner.TransactionLog, "Autotune");
                m_owner.TransactionsAdded(before);
                m_owner.RefreshViewers(m_bin.FileName);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            m_owner.ShowInfo(e.Message);
        }
        finally
        {
            AutotuneCaption = "AutoTune";
            m_owner.ProgressText = "Autotune stopped.";
        }
    }

    [RelayCommand]
    private void ToggleAfrMode() => LambdaMode = !LambdaMode;

    [RelayCommand]
    private void ToggleNight() => IsNight = !IsNight;

    [RelayCommand]
    private async Task SetPerformanceMode(string mode)
    {
        if (!await m_engine.SetPerformanceModeAsync(int.Parse(mode))) m_owner.ShowInfo("The ECU did not accept the performance mode");
    }

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

    /// <summary>Reset peak values (hidden in T7Suite): every peak back to the row's minimum.</summary>
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
        foreach (RealtimeSymbol s in Realtime.Merge(Realtime.Dashboard(m_bin, m_owner.Settings), Realtime.LoadLayout(file, m_bin)))
            Rows.Add(new RealtimeRow(s));
        PushRows();
        SaveUserRows();
    }

    /// <summary>The symbol names offered by the add / edit dialog.</summary>
    public IEnumerable<string> SymbolNames => m_bin.Symbols.Cast<SymbolHelper>().Select(s => s.SmartVarname).Distinct().OrderBy(n => n);

    public RealtimeSymbol? Lookup(string name) => m_bin.FindAny(name) is { } sh ? Realtime.FromSymbol(sh) : null;
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
