using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using SuiteApp.Services;

namespace SuiteApp.ViewModels;

/// <summary>A log in Settings → Logging: on or off, its level.</summary>
public partial class LogRow : ObservableObject
{
    public LogRow(Logging.LogFile log)
    {
        Log = log;
        (_level, _on) = Logging.Get(log);
    }

    public Logging.LogFile Log { get; }

    public string Caption => Log.Caption;

    public string Tip => Log.Tip + $" Written to {Log.File}<date>.txt.";

    public string FileName => Log.File + "<date>.txt";

    public string[] Levels => Logging.Levels;

    [ObservableProperty] private bool _on;
    [ObservableProperty] private string _level;
}

/// <summary>Settings → Logging: every log file on or off at its level, how long the dated files are kept, the folder they're in.</summary>
public partial class LoggingSettingsViewModel : ObservableObject
{
    public IReadOnlyList<LogRow> Logs { get; } = [.. Logging.Files.Select(f => new LogRow(f))];

    /// <summary>Days the log files are kept; 0 keeps them all.</summary>
    [ObservableProperty] private decimal? _keepDays = Logging.KeepDays;

    public const int MaxKeepDays = 365;

    public string Folder => Logging.Folder;

    /// <summary>The settings' Ok: saved, then applied by the main window (MainWindowViewModel.SettingsChanged).</summary>
    public void Apply()
    {
        foreach (LogRow row in Logs) Logging.Set(row.Log, row.Level, row.On);
        Logging.KeepDays = (int)System.Math.Clamp(KeepDays ?? 0, 0, MaxKeepDays);
    }
}
