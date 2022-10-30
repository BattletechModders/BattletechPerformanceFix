using System;
using HBS.Logging;

namespace BattletechPerformanceFix;

// see Better(Level)Logger from ME for concept
internal static class Logging
{
    private static ILog _logger;

    internal static PerformanceLogger Error { get; private set; }
    internal static PerformanceLogger Warn { get; private set; }
    internal static PerformanceLogger Info { get; private set; }
    internal static PerformanceLogger Debug { get; private set; }
    internal static PerformanceLogger Spam { get; private set; }

    internal static void Setup(string settingsLogLevel)
    {
        LogLevel logLevel;
        if (string.Equals(settingsLogLevel, "spam", StringComparison.OrdinalIgnoreCase))
        {
            Spam = new(LogLevel.Debug);
            logLevel = LogLevel.Debug;
        }
        else if (string.Equals(settingsLogLevel, "info", StringComparison.OrdinalIgnoreCase))
        {
            logLevel = LogLevel.Log;
        }
        else if (Enum.TryParse<LogLevel>(settingsLogLevel, out var level))
        {
            logLevel = level;
        }
        else
        {
            logLevel = LogLevel.Debug;
        }

        if (logLevel <= LogLevel.Error)
        {
            Error = new(LogLevel.Error);
        }
        if (logLevel <= LogLevel.Warning)
        {
            Warn = new(LogLevel.Warning);
        }
        if (logLevel <= LogLevel.Log)
        {
            Info = new(LogLevel.Log);
        }
        if (logLevel <= LogLevel.Debug)
        {
            Debug = new(LogLevel.Debug);
        }

        _logger = Logger.GetLogger(Main.ModName, logLevel);
    }

    internal class PerformanceLogger
    {
        private readonly LogLevel _level;

        internal PerformanceLogger(LogLevel level)
        {
            _level = level;
        }

        internal void Log(object message)
        {
            _logger.LogAtLevel(_level, message);
        }

        internal void Log(object message, Exception e)
        {
            _logger.LogAtLevel(_level, message, e);
        }
    }
}