using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommonSuite;
using SuiteApp.ViewModels;
using Trionic5Tools;

namespace T5App.ViewModels;

/// <summary>
/// T5Suite's realtime panel: the shared panel, the AFR maps fed by every pass and the fuel autotune (onSwitchClosedLoopOnOff,
/// FeedInfoToAFRMaps) on Insp_mat! in SRAM, or on the adaption map Adapt_korr on T5.2.
/// </summary>
public partial class T5RealtimeViewModel : RealtimeViewModel
{
    private readonly T5MainWindowViewModel m_t5;
    private readonly T5Binary m_bin;
    private double? m_afr;
    private double m_lastTps = double.NaN;
    private DateTime m_tpsHold;
    private AFRMaps? m_tuning;

    public T5RealtimeViewModel(T5MainWindowViewModel owner, T5Binary bin) : base(owner, bin, T5Realtime.Rules, new T5RealtimeEngine(owner.Ecu))
    {
        m_t5 = owner;
        m_bin = bin;
        AutotuneCaption = "Autotune fuel";
        foreach (string caption in PgmStatusBits) StatusLeds.Add(new StatusLed(caption));
        foreach (var (caption, _, _, _) in PgmModBits) Toggles.Add(new EcuToggle(caption, ToggleAsync) { IsAvailable = false });
    }

    // ---- Engine status (Pgm_status) and Settings (Pgm_mod!) tabs ----

    /// <summary>ctrlRealtime's 40 Engine status LEDs, Pgm_status bit 0 first.</summary>
    private static readonly string[] PgmStatusBits =
    [
        "Ignition", "Afterstart 2 ok", "Engine stopped", "Engine started", "Engine is warm", "Fuel cut", "Temp. compensation", "RPM limiter",
        "Appl. sync ok", "Fuel knock map", "Throttle closed", "Room temp. start", "Fuel cut cyl 4", "Fuel cut cyl 3", "Fuel cut cyl 2", "Fuel cut cyl 1",
        "Fuel not off (during sync. off ign.)", "Dec.enleanment completed throttledec.", "Acc.enrichment completed throttleinc.", "Decrease of retard enrichment allowed",
        "Start of retard enrichment in progress", "Adaption allowed", "Limp-home mode", "Always active temp.compensation",
        "Restart", "Active lambda control", "Afterstart enrichment completed", "Init during start completed",
        "Cooling water enrichment finished", "Purge control active", "Idle fuel map", "Ignition synchronized",
        "Sond heating second sond", "Sond heating first sond", "ETS error", "Ordinary idle control disable",
        "Fuel cut allowed (Dashpot)", "Enrichment after fuelcut", "Fulload enrichment", "Fuel syncronized",
    ];

    /// <summary>ctrlRealtime's Settings switches: Pgm_mod! byte and mask; Knock control is on while its bit is clear.</summary>
    private static readonly (string Caption, int Byte, int Mask, bool Inverted)[] PgmModBits =
    [
        ("Afterstart enrichment", 0, 0x01, false), ("WOT enrichment", 0, 0x02, false), ("Lambda control", 0, 0x10, false), ("Spot adaption", 0, 0x20, false),
        ("Idle control", 0, 0x40, false), ("Cranking enrichment", 0, 0x80, false), ("Fuel cut in engine braking", 1, 0x04, false),
        ("Acceleration enrichment", 1, 0x10, false), ("Deceleration enleanment", 1, 0x20, false), ("Purge control", 2, 0x20, false),
        ("Adaption of idle control", 2, 0x40, false), ("Lambda during idle", 2, 0x80, false), ("APC control", 3, 0x10, false),
        ("Global adaption", 3, 0x40, false), ("Knock control", 4, 0x20, true),
    ];

    private bool m_pgmModRead;

    private void ShowStatus(RealtimeSample sample)
    {
        if (sample["Pgm_status"] is not { } value) return;
        long status = (long)value;
        for (int i = 0; i < StatusLeds.Count; i++) StatusLeds[i].IsOn = (status & 1L << i) != 0;
        if (!m_pgmModRead)
        {
            m_pgmModRead = true;
            _ = RefreshTogglesAsync();
        }
    }

    // the switches as the ECU's Pgm_mod! has them; a byte the symbol lacks hides its switch's use (Knock control on short tables)
    private async Task RefreshTogglesAsync()
    {
        if (m_bin.Find("Pgm_mod!") is not { Start_address: > 0 } mod || await m_t5.Ecu.ReadMapAsync(mod) is not { } pgm || !Plausible(pgm)) return;
        for (int i = 0; i < PgmModBits.Length; i++)
        {
            var (_, index, mask, inverted) = PgmModBits[i];
            Toggles[i].IsAvailable = index < pgm.Length;
            Toggles[i].IsOn = index < pgm.Length && ((pgm[index] & mask) != 0) != inverted;
        }
    }

    /// <summary>A switch: Pgm_mod! read, the bit flipped, the whole symbol written (forced), then read back for every switch.</summary>
    private async Task ToggleAsync(EcuToggle toggle)
    {
        int i = Toggles.IndexOf(toggle);
        if (i < 0 || !IsRunning || m_bin.Find("Pgm_mod!") is not { Start_address: > 0 } mod) return;
        // a read that came back as zeros would switch everything off when written back
        if (await m_t5.Ecu.ReadMapAsync(mod) is not { } pgm || !Plausible(pgm))
        {
            m_t5.ShowInfo("Could not read Pgm_mod! from the ECU, nothing was changed");
            return;
        }
        var (_, index, mask, _) = PgmModBits[i];
        if (index >= pgm.Length) return;
        pgm[index] ^= (byte)mask;
        await m_t5.Ecu.WriteForcedAsync((int)mod.Start_address, pgm);
        await RefreshTogglesAsync();
    }

    /// <summary>The autotune button shows with Settings → Advanced mode enabled, as in T5Suite.</summary>
    public override bool CanAutotune => m_t5.AdvancedMode;

    protected override void OnAfr(double afr, double fuelcut) => m_afr = afr;

    // the map the autotune changes: Insp_mat! on T5.5, the adaption map on T5.2 (whose name has no "!")
    private SymbolHelper? FuelMap => m_bin.IsTrionic55 ? m_bin.Find("Insp_mat!") : m_bin.Find("Adapt_korr");

    /// <summary>
    /// A pass with a wideband value: into the feedback maps, always when not autotuning (T5Suite had no fuel cut check there),
    /// else only when CheckAutoTuneParameters, the enrichment filter and the throttle allow it, and then into the autotune.
    /// </summary>
    protected override void OnApplied(RealtimeSample sample)
    {
        ShowStatus(sample);
        // a throttle drop of more than 10 holds the autotune for 500 ms (tmrOverruleTPS)
        if (!double.IsNaN(m_lastTps) && m_lastTps - Tps > 10) m_tpsHold = sample.Time.AddMilliseconds(500);
        m_lastTps = Tps;
        // the ignition autotune sees every pass except in the idle map (FeedInfoToAFRMaps)
        if (m_ignition is { } ignition && ((long)(sample["Pgm_status"] ?? 0) & 0x40000000) == 0)
            ignition.HandleRealtimeData(Rpm, Tps, Boost, IgnitionAdvance, (sample["Knock_offset1234"] ?? 0) > 0);
        if (m_afr is not { } afr || m_t5.AfrMaps is not { } maps) return;
        m_afr = null;
        AppSettings s = m_t5.Settings;
        bool allowed = T5Autotune.Allowed((long)(sample["Pgm_status"] ?? 0), s, out bool idle)
            && !T5Autotune.Enriching(sample, s.EnrichmentFilter) && sample.Time >= m_tpsHold;
        if (m_tuning == null) maps.LogWidebandAFR(afr, Rpm, Boost, idle);
        else if (allowed)
        {
            maps.LogWidebandAFR(afr, Rpm, Boost, idle);
            maps.HandleRealtimeData(Rpm, Tps, Boost, afr, idle);
        }
        m_t5.RefreshAfrViewers();
    }

    protected override async Task OnStoppedAsync()
    {
        if (m_tuning != null) await StopAutotuneAsync();
        if (m_ignition != null) await StopIgnitionAutotuneAsync();
        m_t5.AfrMaps?.SaveMaps();
    }

    // ---- autotune ignition (T5.5) ----

    private IgnitionMaps? m_ignition;

    public bool IsIgnitionAutotuning => m_ignition != null;

    /// <summary>
    /// Autotune ignition (onSwitchIgnitionTuningOnOff): T5.5 only. Ign_map_0! from SRAM, capped at the global maximum first when
    /// "Adjust ignition map to global maximum" is set, Knock_press_tab! as the knock limit; each changed cell goes straight into SRAM.
    /// </summary>
    public async Task ToggleIgnitionAutotuneAsync()
    {
        if (m_ignition != null)
        {
            await StopIgnitionAutotuneAsync();
            return;
        }
        if (!m_bin.IsTrionic55)
        {
            m_t5.ShowInfo("T5.2 is currently not supported for Autotuning Ignition");
            return;
        }
        if (!IsRunning || m_t5.IgnitionMaps is not { } maps || m_bin.Find("Ign_map_0!") is not { Start_address: > 0 } ign) return;
        T5AppSettings t5 = m_t5.T5Settings;
        T5Ecu ecu = m_t5.Ecu;
        m_t5.ProgressText = "Starting ignition autotune...";
        maps.InitAutoTuneVars(true);
        if (await ecu.ReadMapAsync(ign) is not { Length: > 1 } map || !Plausible(map))
        {
            m_t5.ShowInfo("Could not read the ignition map from the ECU");
            return;
        }
        if (t5.CapIgnitionMap)
        {
            int max = (int)Math.Round(t5.GlobalMaximumIgnitionAdvance * 10);
            bool capped = false;
            for (int i = 0; i + 1 < map.Length; i += 2)
            {
                int advance = map[i] << 8 | map[i + 1];
                if (advance > 32000) advance -= 65536;
                if (advance <= max) continue;
                map[i] = (byte)(max >> 8);
                map[i + 1] = (byte)max;
                capped = true;
            }
            if (capped) await ecu.WriteForcedAsync((int)ign.Start_address, map);
        }
        if (m_bin.Find("Knock_press_tab!") is { Start_address: > 0 } press && await ecu.ReadMapAsync(press) is { } knock) maps.SetKnockPressTab(Words(knock));
        maps.SetOriginalIgnitionMap(Words(map));
        maps.SetCurrentIgnitionMap(Words(map));
        maps.CellStableTime_ms = t5.IgnitionCellStableTime_ms;
        maps.MinimumEngineSpeedForIgnitionTuning = t5.MinimumEngineSpeedForIgnitionTuning;
        maps.MaxumimIgnitionAdvancePerSession = t5.MaximumIgnitionAdvancePerSession;
        maps.IgnitionAdvancePerCycle = t5.IgnitionAdvancePerCycle;
        maps.IgnitionRetardFirstKnock = t5.IgnitionRetardFirstKnock;
        maps.IgnitionRetardFurtherKnocks = t5.IgnitionRetardFurtherKnocks;
        maps.GlobalMaximumIgnitionAdvance = t5.GlobalMaximumIgnitionAdvance;
        maps.onIgnitionmapCellChanged += OnIgnitionCellChanged;
        maps.IsAutoMappingActive = true;
        m_ignition = maps;
        OnPropertyChanged(nameof(IsIgnitionAutotuning));
        m_t5.ProgressText = "Autotune ignition running...";
    }

    // signed: T5Suite's ByteArrayToIntArray read −1.0° as 65526, which the autotune then "advanced" straight to the global maximum
    private static int[] Words(byte[] d) =>
        Enumerable.Range(0, d.Length / 2).Select(i => d[i * 2] << 8 | d[i * 2 + 1]).Select(v => v > 32767 ? v - 65536 : v).ToArray();

    /// <summary>
    /// A read worth acting on: TrionicCANLib's readRAM gives zeros when the ECU doesn't answer, and no map, Pgm_mod! or fuel trim is
    /// all zeros. ponytail: a zeroed part of a map still passes; a null-on-timeout readRAM in the library would close that.
    /// </summary>
    private static bool Plausible(byte[]? d) => d is { Length: > 0 } && d.Any(b => b != 0);

    private static byte[] Bytes(int[] words) => words.SelectMany(w => new[] { (byte)(w >> 8), (byte)w }).ToArray();

    private void OnIgnitionCellChanged(object sender, IgnitionMaps.IgnitionmapChangedEventArgs e)
    {
        if (m_bin.Find("Ign_map_0!") is { Start_address: > 0 } ign)
            _ = m_t5.Ecu.WriteForcedAsync((int)ign.Start_address + e.Mapindex * 2, [(byte)(e.Cellvalue >> 8), (byte)e.Cellvalue]);
    }

    /// <summary>
    /// Stop: "Keep adjusted ignition map?" No puts the original back into SRAM; Yes writes the tuned map into the file (with a transaction
    /// entry here; T5Suite logged none) and the checksum per Auto update checksum.
    /// </summary>
    private async Task StopIgnitionAutotuneAsync()
    {
        if (m_ignition is not { } maps) return;
        m_ignition = null;
        maps.IsAutoMappingActive = false;
        maps.onIgnitionmapCellChanged -= OnIgnitionCellChanged;
        OnPropertyChanged(nameof(IsIgnitionAutotuning));
        try
        {
            if (m_bin.Find("Ign_map_0!") is not { } ign) return;
            bool keep = m_t5.AskYesNoCancel == null || await m_t5.AskYesNoCancel("Keep adjusted ignition map?") == true;
            if (!keep) await m_t5.Ecu.WriteForcedAsync((int)ign.Start_address, Bytes(maps.GetOriginalIgnitionmap()));
            else if (m_bin.FileAddress(ign) is var address and >= 0)
            {
                int before = m_t5.TransactionLog?.TransCollection.Count ?? 0;
                m_bin.WriteData(address, Bytes(maps.GetCurrentlyMutatedIgnitionMap()), m_t5.TransactionLog, "Autotune ignition");
                if (m_t5.Settings.AutoChecksum) m_bin.UpdateChecksum();
                m_t5.TransactionsAdded(before);
                m_t5.RefreshViewers(m_bin.FileName);
            }
            maps.SaveMaps();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            m_t5.ShowInfo(e.Message);
        }
        finally
        {
            maps.InitAutoTuneVars(false);
            m_t5.ProgressText = "Idle";
        }
    }

    /// <summary>Autotune fuel: start (warm engine and a wideband symbol, as T5Suite enabled the button) or stop.</summary>
    protected override async Task ToggleAutotune()
    {
        if (AutotuneCaption == "Wait...") return;
        if (m_tuning != null)
        {
            await StopAutotuneAsync();
            return;
        }
        AppSettings s = m_t5.Settings;
        if (!IsRunning) return;
        if (Coolant <= 70 || !s.UseWidebandLambda)
        {
            m_t5.ShowInfo("Autotune fuel needs a warm engine (coolant above 70 °C) and the wideband lambda through a symbol (Settings).");
            return;
        }
        if (m_t5.AfrMaps is not { } maps || FuelMap is not { Start_address: > 0 } fuel) return;
        AutotuneCaption = "Wait...";
        T5Ecu ecu = m_t5.Ecu;
        try
        {
            if (await ecu.ReadMapAsync(fuel) is not { } map || !Plausible(map))
            {
                m_t5.ShowInfo("Could not read the fuel map from the ECU");
                return;
            }
            // closed loop off while tuning ("Disable closed loop on starting autotune"); the byte is kept to put back at the end.
            // T5Suite's T5.5 branch also meant to fold Adapt_korr into Insp_mat! first, but looked it up without the "!" and never did.
            if (s.DisableClosedLoopOnStartAutotune && m_bin.Find("Pgm_mod!") is { Start_address: > 0 } mod && await ecu.ReadMapAsync(mod) is { } pgm && Plausible(pgm)
                && (pgm[0] & 0x10) != 0)
            {
                m_closedLoopByte = pgm[0];
                await ecu.WriteForcedAsync((int)mod.Start_address, [(byte)(pgm[0] & ~0x10)]);
            }
            maps.InitAutoTuneVars(false);
            if (m_t5.T5Settings.ResetFuelTrims)
            {
                if (m_bin.Find("Adapt_injfaktor!") is { Start_address: > 0 } ltft) await ecu.WriteForcedAsync((int)ltft.Start_address, Neutral(ltft));
                if (m_bin.IsTrionic55 && s.AllowIdleAutoTune && m_bin.Find("Adapt_inj_imat!") is { Start_address: > 0 } it)
                    await ecu.WriteForcedAsync((int)it.Start_address, Neutral(it));
            }
            maps.SetOriginalFuelMap(map);
            maps.SetCurrentFuelMap(map);
            if (m_bin.Find("Idle_fuel_korr!") is { Start_address: > 0 } idleMap && await ecu.ReadMapAsync(idleMap) is { Length: > 0 } idle)
            {
                maps.SetIdleOriginalFuelMap(idle);
                maps.SetIdleCurrentFuelMap(idle);
            }
            maps.AcceptableTargetErrorPercentage = s.AcceptableTargetErrorPercentage;
            maps.AreaCorrectionPercentage = s.AreaCorrectionPercentage;
            maps.AutoUpdateFuelMap = s.AutoUpdateFuelMap;
            maps.CellStableTime_ms = s.CellStableTime_ms;
            maps.CorrectionPercentage = s.CorrectionPercentage;
            maps.DiscardClosedThrottleMeasurements = s.DiscardClosedThrottleMeasurements;
            maps.DiscardFuelcutMeasurements = s.DiscardFuelcutMeasurements;
            maps.EnrichmentFilter = s.EnrichmentFilter;
            maps.FuelCutDecayTime_ms = s.FuelCutDecayTime_ms;
            maps.MaximumAdjustmentPerCyclePercentage = s.MaximumAdjustmentPerCyclePercentage;
            maps.MaximumAFRDeviance = s.MaximumAFRDeviance;
            maps.MinimumAFRMeasurements = s.MinimumAFRMeasurements;
            maps.WideBandAFRSymbol = s.WideBandSymbol;
            maps.onFuelmapCellChanged += OnCellChanged;
            maps.onIdleFuelmapCellChanged += OnIdleCellChanged;
            maps.IsAutoMappingActive = true;
            m_tuning = maps;
        }
        finally
        {
            // a start that failed after closed loop went off puts it back
            if (m_tuning == null) await RestoreClosedLoopAsync();
            IsAutotuning = m_tuning != null;
            AutotuneCaption = IsAutotuning ? "Tuning..." : "Autotune fuel";
            m_t5.ProgressText = IsAutotuning ? "Autotune fuel running..." : "Autotune fuel not started";
        }
    }

    // Pgm_mod![0] as it was when the autotune switched closed loop off; null when it didn't
    private byte? m_closedLoopByte;

    /// <summary>Closed loop back on, from the byte read at the start (a read now could come back as zeros and switch everything off).</summary>
    private async Task RestoreClosedLoopAsync()
    {
        if (m_closedLoopByte is not { } original || m_bin.Find("Pgm_mod!") is not { Start_address: > 0 } mod) return;
        byte[]? now = await m_t5.Ecu.ReadMapAsync(mod);
        await m_t5.Ecu.WriteForcedAsync((int)mod.Start_address, [Plausible(now) ? (byte)(now![0] | 0x10) : original]);
        m_closedLoopByte = null;
    }

    /// <summary>Stops a running autotune (fuel and ignition) while the ECU is still there: before a disconnect, a flash or closing.</summary>
    public async Task StopAutotunesAsync()
    {
        if (m_tuning != null) await StopAutotuneAsync();
        if (m_ignition != null) await StopIgnitionAutotuneAsync();
    }

    public bool IsAnyAutotuning => m_tuning != null || m_ignition != null;

    // the fuel trims' neutral value
    private static byte[] Neutral(SymbolHelper sh) => Enumerable.Repeat((byte)128, sh.Length).ToArray();

    // "Auto update fuel map": each changed cell straight into SRAM
    private void OnCellChanged(object sender, AFRMaps.FuelmapChangedEventArgs e)
    {
        if (FuelMap is { Start_address: > 0 } fuel) _ = m_t5.Ecu.WriteForcedAsync((int)fuel.Start_address + e.Mapindex, [e.Cellvalue]);
    }

    private void OnIdleCellChanged(object sender, AFRMaps.IdleFuelmapChangedEventArgs e)
    {
        if (m_bin.Find("Idle_fuel_korr!") is { Start_address: > 0 } idle) _ = m_t5.Ecu.WriteForcedAsync((int)idle.Start_address + e.Mapindex, [e.Cellvalue]);
    }

    /// <summary>
    /// Stop: closed loop back on when it was. With auto update "Keep adjusted fuel map?" (yes: SRAM into the file, no: the original
    /// back into SRAM); without, "Select mutations to accept", written to SRAM and the file. T5.2's adaption map stays in SRAM (T5Suite
    /// wrote its values into the file's Insp_mat!). The file gets one transaction entry and the checksum per Auto update checksum.
    /// </summary>
    private async Task StopAutotuneAsync()
    {
        if (m_tuning is not { } maps || FuelMap is not { } fuel) return;
        m_tuning = null;
        maps.IsAutoMappingActive = false;
        maps.onFuelmapCellChanged -= OnCellChanged;
        maps.onIdleFuelmapCellChanged -= OnIdleCellChanged;
        IsAutotuning = false;
        AutotuneCaption = "Wait...";
        AppSettings s = m_t5.Settings;
        T5Ecu ecu = m_t5.Ecu;
        try
        {
            await RestoreClosedLoopAsync();
            byte[] original = maps.GetOriginalFuelmap();
            byte[]? keep = null, keepIdle = null;
            if (s.AutoUpdateFuelMap)
            {
                bool? answer = m_t5.AskYesNoCancel == null ? true : await m_t5.AskYesNoCancel("Keep adjusted fuel map?");
                // the tuned maps as the autotune wrote them into SRAM (T5Suite read SRAM again, which can come back as zeros)
                if (answer == true)
                {
                    keep = maps.GetCurrentlyMutatedFuelMap();
                    if (s.AllowIdleAutoTune) keepIdle = maps.GetIdleCurrentlyMutatedFuelMap();
                }
                else if (answer == false)
                {
                    await ecu.WriteForcedAsync((int)fuel.Start_address, original);
                    if (m_bin.Find("Idle_fuel_korr!") is { Start_address: > 0 } idle) await ecu.WriteForcedAsync((int)idle.Start_address, maps.GetIdleOriginalFuelmap());
                }
            }
            else if (m_t5.AcceptAutotune is { } accept && await accept(maps.GetPercentualDifferences()) is { Count: > 0 } cells)
            {
                double[] percent = maps.GetPercentualDifferences();
                keep = (byte[])original.Clone();
                foreach (int i in cells.Where(i => i < keep.Length && !double.IsNaN(percent[i])))
                    keep[i] = (byte)Math.Clamp(Math.Round(original[i] * (100 + percent[i]) / 100), 0, 255);
                await ecu.WriteForcedAsync((int)fuel.Start_address, keep);
                if (s.AllowIdleAutoTune && m_bin.Find("Idle_fuel_korr!") is { Start_address: > 0 } idle)
                    await ecu.WriteForcedAsync((int)idle.Start_address, maps.GetIdleCurrentlyMutatedFuelMap());
            }
            if (keep != null && m_bin.IsTrionic55 && m_bin.FileAddress(fuel) is var address and >= 0)
            {
                int before = m_t5.TransactionLog?.TransCollection.Count ?? 0;
                m_bin.WriteData(address, keep, m_t5.TransactionLog, "Autotune fuel");
                if (keepIdle is { Length: > 0 } && m_bin.Find("Idle_fuel_korr!") is { } idleMap && m_bin.FileAddress(idleMap) is var idleAddress and >= 0
                    && keepIdle.Length == idleMap.Length)
                    m_bin.WriteData(idleAddress, keepIdle, m_t5.TransactionLog, "Autotune fuel (idle)");
                if (s.AutoChecksum) m_bin.UpdateChecksum();
                m_t5.TransactionsAdded(before);
                m_t5.RefreshViewers(m_bin.FileName);
            }
            maps.SaveMaps();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            m_t5.ShowInfo(e.Message);
        }
        finally
        {
            AutotuneCaption = "Autotune fuel";
            m_t5.ProgressText = "Autotune stopped.";
        }
    }
}
