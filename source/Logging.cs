using System;

namespace BattletechPerformanceFix;

public static partial class Extensions
{
    internal static void LogException(Exception e)
    {
        Main.HBSLogger.LogException(e);
    }

    internal static void LogError(string message)
    {
        Main.HBSLogger.LogError(message);
    }

    internal static void LogWarning(string message)
    {
        Main.HBSLogger.LogWarning(message);
    }

    internal static void LogInfo(string message)
    {
        Main.HBSLogger.Log(message);
    }

    internal static void LogDebug(string message)
    {
        Main.HBSLogger.LogDebug(message);
    }

    internal static bool SpamEnabled
    {
        set => Spam = value ? new PerformanceLogger() : null;
    }

    // see BetterLevelLogger from ME for concept
    internal static PerformanceLogger Spam { get; private set; }
    internal class PerformanceLogger
    {
        internal void Log(string message)
        {
            Main.HBSLogger.LogDebug(message);
        }
    }
}