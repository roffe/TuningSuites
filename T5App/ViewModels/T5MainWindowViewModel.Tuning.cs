using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommonSuite;
using CommunityToolkit.Mvvm.Input;
using Trionic5Tools;

namespace T5App.ViewModels;

/// <summary>The "Tuning wizards" page: the windows ask, T5Tuning writes, then the viewers, status bar and checksum are refreshed.</summary>
public partial class T5MainWindowViewModel
{
    private T5Binary? T5 => Binary as T5Binary;

    // the tuner wrote the file behind every open viewer's back (T5Suite didn't refresh them)
    private async Task AfterTuningAsync()
    {
        if (T5 is not { } bin) return;
        RefreshViewers(bin.FileName);
        ShowFirmwareStatus();
        await CheckChecksumAsync();
    }

    /// <summary>Settings "Advanced mode enabled": the advanced tuning wizards are shown (SetModeAndFilters).</summary>
    public bool AdvancedMode => T5Settings.EnableAdvancedMode;

    /// <summary>After Settings Ok: the advanced wizards and the open file's sensor detection follow T5Suite's settings.</summary>
    public void T5SettingsChanged()
    {
        OnPropertyChanged(nameof(AdvancedMode));
        if (T5 is { } bin) bin.AutoDetectMapSensor = T5Settings.AutoDetectMapsensorType;
    }

    /// <summary>Tune me up ®: the wizard's presets; null (with T5Suite's message) when the file is tuned beyond stage 3.</summary>
    public TuneMeUpViewModel? TuneMeUp()
    {
        if (T5 is not { } bin) return null;
        if (T5Tuning.TuneMeUpDefaults(bin) is { } d) return new TuneMeUpViewModel(d);
        ShowInfo("This file has already been tuned to a higher stage, the tuning wizard will not be started");
        return null;
    }

    /// <summary>The wizard's OK: stage 1-3 or the free tune; the report lines, or null after a message.</summary>
    public async Task<List<string>?> RunTuneMeUpAsync(TuneMeUpViewModel w)
    {
        if (T5 is not { } bin) return null;
        var (result, report) = w.IsFreeTune
            ? T5Tuning.FreeTune(bin, (double)(w.PeakTorque ?? 400), (double)(w.PeakBoost ?? 1.4m), w.ByTorque, w.MapSensor, w.Turbo, w.Injectors, w.Valve,
                (int)(w.RpmLimit ?? 6000), (int)(w.KnockTime ?? 1000), Settings.AutoChecksum)
            : T5Tuning.TuneToStage(bin, w.Stage, Settings.AutoChecksum);
        await AfterTuningAsync();
        switch (result)
        {
            case TuningResult.TuningSuccess:
                return report;
            case TuningResult.TuningFailedAlreadyTuned:
                ShowInfo("Your binary file was already tuned!");
                break;
            case TuningResult.TuningFailedThreebarSensor:
                ShowInfo("Your binary file was already tuned (3 bar sensor)!");
                break;
            case TuningResult.TuningCancelled:
                ShowInfo("Tuning process cancelled by user");
                break;
            default:
                ShowInfo("Tuning of the binary file failed!");
                break;
        }
        return null;
    }

    /// <summary>The MAP sensor the file is set up for (the marker, or detected when the marker says stock and the setting is on).</summary>
    public MapSensorType CurrentMapSensor => T5?.File.GetMapSensorType(T5Settings.AutoDetectMapsensorType) ?? MapSensorType.MapSensor25;

    /// <summary>Convert to a different MAP sensor (the wizard confirmed): the report lines.</summary>
    public async Task<List<string>> ConvertMapSensorAsync(MapSensorType to)
    {
        if (T5 is not { } bin) return [];
        List<string> report = T5Tuning.ConvertMapSensor(bin, CurrentMapSensor, to, Settings.AutoChecksum);
        await AfterTuningAsync();
        return report;
    }

    public InjectorWizardViewModel? InjectorWizard() => T5 is { } bin ? new InjectorWizardViewModel(T5Tuning.Injectors(bin)) : null;

    public async Task ApplyInjectorsAsync(InjectorWizardViewModel w)
    {
        if (T5 is not { } bin) return;
        int before = TransactionLog?.TransCollection.Count ?? 0;
        T5Tuning.ApplyInjectors(bin, w.Proposed, TransactionLog);
        TransactionsAdded(before);
        await AfterTuningAsync();
    }

    /// <summary>Convert to E85 (confirmed): the backup goes into the project's Backups folder when a project is open.</summary>
    public async Task<List<string>> ConvertToE85Async()
    {
        if (T5 is not { } bin) return [];
        string? backups = Project is { } project ? Path.Combine(project.Dir, "Backups") : null;
        List<string> report = T5Tuning.ConvertToE85(bin, backups, Settings.AutoChecksum);
        await AfterTuningAsync();
        return report;
    }

    public T5Tuning.BoostAdaption? BoostAdaption() => T5 is { } bin ? T5Tuning.ReadBoostAdaption(bin) : null;

    public async Task SetBoostAdaptionAsync(T5Tuning.BoostAdaption to)
    {
        if (T5 is not { } bin) return;
        // T5Suite stayed silent when the code wasn't found
        ShowInfo(T5Tuning.SetBoostAdaption(bin, to, Settings.AutoChecksum) ? "Boost adaption ranges were set" : "The boost adaption code was not found in this file, nothing was changed");
        await AfterTuningAsync();
    }

    /// <summary>Change boost bias range: only for the 16-bit boost bias map; the current axis step.</summary>
    public int? BoostBiasStep() => T5 is { } bin && bin.Info.Has2DRegKonMat() ? bin.File.GetRegulationDivisorValue() : null;

    public async Task SetBoostBiasStepAsync(int step)
    {
        if (T5 is not { } bin) return;
        ShowInfo(T5Tuning.SetBoostBiasStep(bin, step, Settings.AutoChecksum) ? "Boost bias range has been changed" : "The boost bias code was not found in this file, nothing was changed");
        await AfterTuningAsync();
    }

    /// <summary>Change RPM limit: T5.5 only, with T5Suite's messages.</summary>
    public (int hardcoded, int software)? RpmLimits()
    {
        if (T5 is not { } bin) return null;
        if (!bin.IsTrionic55)
        {
            ShowInfo("Trionic 5.2 files are not supported by this wizard");
            return null;
        }
        if (T5Tuning.ReadRpmLimits(bin) is { } limits) return limits;
        ShowInfo("This file is not supported by this wizard");
        return null;
    }

    public async Task SetRpmLimitsAsync(int hardcoded, int software)
    {
        if (T5 is not { } bin) return;
        int before = TransactionLog?.TransCollection.Count ?? 0;
        T5Tuning.SetRpmLimits(bin, hardcoded, software, TransactionLog);
        TransactionsAdded(before);
        ShowInfo("RPM limiters have been changed");
        await AfterTuningAsync();
    }

    // ---- Actions page ----

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(Binary)) OnPropertyChanged(nameof(OriginalFile));
        // the SRAM symbols matter once the ECU or a snapshot is there: T5Suite switched its filter off
        if (e.PropertyName is nameof(IsConnected) or nameof(SramFile) && (IsConnected || SramFile != null) && Binary != null) SymbolFilter = SymbolFilters[0];
    }

    /// <summary>
    /// "Compare to original file": Binaries/&lt;partnumber&gt;-&lt;software id&gt;.bin, else Binaries/&lt;partnumber&gt;.bin. T5Suite looked for the
    /// first and compared the second, so the item never lit up.
    /// </summary>
    public string? OriginalFile => T5?.File.GetTrionicProperties() is { } p
        ? PartInfo.StockBinary($"{p.Partnumber.Trim()}-{p.SoftwareID.Trim()}") ?? PartInfo.StockBinary(p.Partnumber.Trim())
        : null;

    [RelayCommand]
    private Task CompareToOriginal() => OriginalFile is { } file ? CompareToFileAsync(file) : Task.CompletedTask;

    /// <summary>Import SRAM snapshot into binary (merge adaption data); "Data was imported".</summary>
    public async Task MergeAdaptionAsync(string ramFile, T5Tuning.AdaptionMerge options)
    {
        if (T5 is not { } bin) return;
        int before = TransactionLog?.TransCollection.Count ?? 0;
        try
        {
            byte[] ram = await File.ReadAllBytesAsync(ramFile);
            if (ram.Length != 0x8000)
            {
                ShowInfo("This is not a Trionic 5 SRAM snapshot (32 KB), nothing was imported");
                return;
            }
            T5Tuning.MergeAdaption(bin, ram, options, TransactionLog, Settings.AutoChecksum);
            ShowInfo("Data was imported");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowInfo("Failed to write to binary. Is it read-only? Details: " + e.Message);
        }
        TransactionsAdded(before);
        await AfterTuningAsync();
    }

    public List<string> Examine() => T5 is { } bin ? T5Reports.Examine(bin) : [];

    public List<string> Anomalies() => T5 is { } bin ? T5Reports.Anomalies(bin) : [];
}
