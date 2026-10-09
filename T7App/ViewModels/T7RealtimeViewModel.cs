using System;
using System.IO;
using System.Threading.Tasks;
using SuiteApp.ViewModels;
using T7;

namespace T7App.ViewModels;

/// <summary>T7Suite's realtime panel: the shared panel plus Eco / Norm / Sport, AutoTune and the AFR maps fed by every pass.</summary>
public partial class T7RealtimeViewModel : RealtimeViewModel
{
    private readonly T7MainWindowViewModel m_t7;
    private readonly T7Binary m_t7bin;
    private readonly T7RealtimeEngine m_t7engine;
    private Autotune? m_autotune;

    public T7RealtimeViewModel(T7MainWindowViewModel owner, T7Binary bin) : this(owner, bin, new T7RealtimeEngine(owner.Ecu))
    {
    }

    private T7RealtimeViewModel(T7MainWindowViewModel owner, T7Binary bin, T7RealtimeEngine engine) : base(owner, bin, T7Realtime.Rules, engine)
    {
        m_t7 = owner;
        m_t7bin = bin;
        m_t7engine = engine;
        if (bin.FindAny("Performance.Mode") is { Start_address: > 0 } mode) engine.PerformanceMode = mode;
    }

    public override bool HasPerformanceMode => m_t7engine.PerformanceMode != null;

    protected override async Task SetPerformanceMode(string mode)
    {
        if (!await m_t7engine.SetPerformanceModeAsync(int.Parse(mode))) m_t7.ShowInfo("The ECU did not accept the performance mode");
    }

    // LogWidebandAFR and the autotune
    protected override void OnAfr(double afr, double fuelcut)
    {
        CommonSuite.AppSettings s = m_t7.Settings;
        if (s.AutoCreateAFRMaps && m_t7.AfrMaps is { } maps && maps.Add(afr, s.MeasureAFRInLambda, Rpm, Airmass, fuelcut))
            m_t7.RefreshAfrViewers();
        m_autotune?.Handle(afr, Rpm, Airmass);
    }

    protected override async Task OnStoppedAsync()
    {
        if (m_autotune != null) await StopAutotuneAsync();
        m_t7.AfrMaps?.Save();
    }

    /// <summary>The AutoTune button shows for open binaries only.</summary>
    public override bool CanAutotune => m_t7bin.IsSoftwareOpen;

    /// <summary>btnAutoTune: start (open bin, wideband, coolant ≥ 70 °C) or stop.</summary>
    protected override async Task ToggleAutotune()
    {
        if (AutotuneCaption == "Wait...") return;
        if (m_autotune != null)
        {
            await StopAutotuneAsync();
            return;
        }
        if (Autotune.CannotStart(m_t7bin, m_t7.Settings, Coolant) is { } reason)
        {
            m_t7.ShowInfo(reason);
            return;
        }
        if (!IsRunning || m_t7.AfrMaps is not { } afr) return;
        AutotuneCaption = "Wait...";
        m_t7.ProgressText = "Starting autotune...";
        m_autotune = await Autotune.StartAsync(afr, m_t7.Ecu, m_t7.Settings);
        IsAutotuning = m_autotune != null;
        AutotuneCaption = IsAutotuning ? "Tuning..." : "AutoTune";
        m_t7.ProgressText = IsAutotuning ? "Autotune running..." : "Autotune init failed.";
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
                bool? answer = m_t7.AskYesNoCancel == null ? true : await m_t7.AskYesNoCancel("Keep adjusted fuel map?");
                if (answer == true) keep = await m_t7.Ecu.ReadMapAsync(tune.FuelMap);
                else if (answer == false) await m_t7.Ecu.WriteMapAsync(tune.FuelMap, tune.Original);
            }
            else if (m_t7.AcceptAutotune is { } accept && await accept(tune.Differences) is { Count: > 0 } cells)
            {
                keep = Autotune.Accept(tune.Original, tune.Differences, cells);
                await m_t7.Ecu.WriteMapAsync(tune.FuelMap, keep);
            }
            if (keep != null)
            {
                int before = m_t7.TransactionLog?.TransCollection.Count ?? 0;
                m_t7bin.WriteSymbol(m_t7bin.FileAddress(tune.FuelMap), keep, m_t7.Settings.AutoFixFooter, m_t7.TransactionLog, "Autotune");
                m_t7.TransactionsAdded(before);
                m_t7.RefreshViewers(m_t7bin.FileName);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            m_t7.ShowInfo(e.Message);
        }
        finally
        {
            AutotuneCaption = "AutoTune";
            m_t7.ProgressText = "Autotune stopped.";
        }
    }
}
