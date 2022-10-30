using System;
using System.IO;
using System.Diagnostics;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix;

public static partial class Extensions
{
    public static bool Spam { get; set; }
    public static void LogException(Exception e)
    {
        Main.HBSLogger.LogException(e);
    }

    public static void LogError(string message)
    {
        Main.HBSLogger.LogError(message);
    }

    public static void LogWarning(string message)
    {
        Main.HBSLogger.LogWarning(message);
    }

    public static void LogInfo(string message)
    {
        Main.HBSLogger.Log(message);
    }

    public static void LogDebug(string message)
    {
        Main.HBSLogger.LogDebug(message);
    }

    public static void LogSpam(string message)
    {
        Main.HBSLogger.LogDebug(message);
    }
}