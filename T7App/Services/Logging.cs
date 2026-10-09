using NLog;

namespace T7App.Services;

public static class Logging
{
    /// <summary>
    /// canLog*.txt gets every CAN frame at Trace level: only with Settings → Enable CAN logging (T7Suite's option, which its
    /// NLog.config ignored and logged always). Idempotent, as the flasher's ApplyCanLogging.
    /// </summary>
    public static void ApplyCanLogging(bool on)
    {
        if (LogManager.Configuration is not { } config) return;
        foreach (var rule in config.LoggingRules)
            foreach (var target in rule.Targets)
                if (target.Name == "canlogfile")
                {
                    if (on) rule.EnableLoggingForLevel(LogLevel.Trace);
                    else rule.DisableLoggingForLevel(LogLevel.Trace);
                    break;
                }
        LogManager.ReconfigExistingLoggers();
    }
}
