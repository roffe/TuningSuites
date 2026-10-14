using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CommonSuite;
using NLog;
using NLog.Config;

namespace SuiteApp.Services;

/// <summary>
/// The program's log files (NLog.config's file targets, a file per log and day in &lt;AppData&gt;/MattiasC/TxSuite): each one on or off
/// at a level of its own, and dated files older than a number of days deleted. Settings → Logging, kept in the program's settings.
/// </summary>
public static class Logging
{
    /// <summary>A log: its NLog target, caption and file name in Settings → Logging, what goes in it, and its level and state before any is set.</summary>
    public sealed record LogFile(string Target, string Caption, string File, string Tip, string Level, bool On);

    /// <summary>A log's levels, the most detailed first; a log at one also gets every level after it.</summary>
    public static string[] Levels { get; } = ["Trace", "Debug", "Info", "Warn", "Error"];

    /// <summary>The logs NLog.config writes, as it set them up (the CAN frame log off, as T7Suite's Enable CAN logging was).</summary>
    public static IReadOnlyList<LogFile> Files { get; } =
    [
        new("uilogfile", "Program", "uiLog",
            "What TxSuite and the T5, T7 and T8 parts do: opening and parsing binaries, symbols, checksums, settings, the realtime panel.", "Trace", true),
        new("apilogfile", "ECU sessions", "apiLog",
            "TrionicCANLib's T5, T7 and T8 sessions: connecting, security access, reading and writing memory, fault codes.", "Debug", true),
        new("kwplogfile", "KWP requests", "kwpLog", "Every KWP2000 request to a T7 and its answer.", "Trace", true),
        new("flasherlogfile", "Flashing", "flasherLog", "Reading and flashing an ECU's flash, block by block.", "Debug", true),
        new("devicelogfile", "CAN adapter", "deviceLog", "The adapter's driver: opening it, its settings, the frames it couldn't send, its errors.", "Debug", true),
        new("canlogfile", "CAN frames", "canLog",
            "Every CAN frame sent and received (at Trace). It grows fast and slows the communication down: for finding communication problems only.", "Trace", false),
    ];

    /// <summary>Where the logs are: the program's settings folder (NLog.config's ${specialfolder:ApplicationData}/MattiasC/TxSuite).</summary>
    public static string Folder => SettingsKey.Folder(SettingsKey.Program);

    // the program's settings: Logging\<target>\Level and \On, Logging\KeepDays
    private const string Key = "Logging";

    /// <summary>A log's level and whether it is on, as set; the log's own before that.</summary>
    public static (string Level, bool On) Get(LogFile log)
    {
        using var key = SettingsKey.Open(SettingsKey.Program, Key + "\\" + log.Target);
        string level = key.GetValue("Level") as string is { } l && Levels.Contains(l) ? l : log.Level;
        bool on = key.GetValue("On") is string o && bool.TryParse(o, out bool b) ? b : log.On;
        return (level, on);
    }

    public static void Set(LogFile log, string level, bool on)
    {
        using var key = SettingsKey.Open(SettingsKey.Program, Key + "\\" + log.Target);
        key.SetValue("Level", level);
        key.SetValue("On", on);
    }

    /// <summary>Days a dated log file is kept (by its last write); 0 keeps them all.</summary>
    public static int KeepDays
    {
        get
        {
            using var key = SettingsKey.Open(SettingsKey.Program, Key);
            return int.TryParse(key.GetValue("KeepDays") as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out int days) ? Math.Max(0, days) : 0;
        }
        set
        {
            using var key = SettingsKey.Open(SettingsKey.Program, Key);
            key.SetValue("KeepDays", Math.Max(0, value));
        }
    }

    /// <summary>
    /// The settings into NLog's rules: every rule that writes to a log gets the log's level and the ones after it, none while it is off.
    /// The other targets (the debugger's output) stay as NLog.config has them.
    /// </summary>
    public static void Apply(LoggingConfiguration? config = null)
    {
        config ??= LogManager.Configuration;
        if (config == null) return;
        foreach (LogFile log in Files)
        {
            var (level, on) = Get(log);
            foreach (LoggingRule rule in config.LoggingRules.Where(r => r.Targets.Any(t => t.Name == log.Target)))
            {
                rule.DisableLoggingForLevels(LogLevel.Trace, LogLevel.Fatal);
                if (on) rule.EnableLoggingForLevels(LogLevel.FromString(level), LogLevel.Fatal);
            }
        }
        LogManager.ReconfigExistingLoggers();
    }

    // a log's dated file, e.g. uiLog2026-10-14.txt; nothing else in the folder is a log
    private static readonly Regex DatedLog = new(@"^(" + string.Join('|', Files.Select(f => Regex.Escape(f.File))) + @")\d{4}-\d{2}-\d{2}\.txt$", RegexOptions.IgnoreCase);

    /// <summary>The logs' dated files last written more than KeepDays days before now deleted (none with 0); how many went.</summary>
    public static int DeleteOld(string? folder = null, DateTime? now = null)
    {
        int days = KeepDays;
        folder ??= Folder;
        if (days == 0 || !Directory.Exists(folder)) return 0;
        DateTime before = (now ?? DateTime.Now).AddDays(-days);
        int deleted = 0;
        foreach (string file in Directory.GetFiles(folder).Where(f => DatedLog.IsMatch(Path.GetFileName(f)) && File.GetLastWriteTime(f) < before))
        {
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // one NLog still has open (Windows): it goes next time
            }
        }
        return deleted;
    }
}
