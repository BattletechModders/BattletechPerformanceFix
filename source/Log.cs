#nullable enable
using HBS.Logging;
using NullableLogging;

namespace BattletechPerformanceFix;

internal static class Log
{
    private const string Name = nameof(BattletechPerformanceFix);
    internal static readonly NullableLogger Main = NullableLogger.GetLogger(Name, LogLevel.Debug);
}
