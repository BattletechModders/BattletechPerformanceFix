using System;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using BattleTech.Data;
using Dapper;
using Mono.Data.Sqlite;

namespace BattletechPerformanceFix;

internal class MDDQueryFix : Feature
{
    
    /// Not necessary patch for smaller lists
    private const int MinListItems = 100;
    
    /// Guard to prevent SQL queries longer than the API allows
    private const int MaxSqlLength = 900000;

    /// The options Dapper uses with its in-list regex
    private const RegexOptions InListOptions = RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant;
    
    private static string Quote(string value) => $"'{value.Replace("'", "''")}'";

    public void Activate()
    {
        Main.harmony.PatchAll(typeof(MDDQueryFix));
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(MetadataDatabase), MethodType.Constructor)]
    public static void MetadataDatabase_ctor_Post(MetadataDatabase __instance)
    {
        try
        {
            __instance.Execute("CREATE INDEX IF NOT EXISTS TagSetTag_NoCase ON TagSetTag (TagSetID COLLATE NOCASE)");
            Log.Main.Info?.Log("MDDQueryFix: TagSetTag has a case-insensitive TagSetID index");
        }
        catch (Exception e)
        {
            Log.Main.Error?.Log("MDDQueryFix: failed to create the TagSetTag index", e);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(SqlMapper), nameof(SqlMapper.PackListParameters))]
    public static void PackListParameters_Pre(ref bool __runOriginal, IDbCommand command, string namePrefix, object value)
    {
        if (!__runOriginal || !CanParametersBeInlineOptimized(command, value, out var ids))
        {
            return;
        }
        string parameterList = $"({string.Join(",", ids.Select(Quote))})";
        string inListRegex = SqlMapper.GetInListRegex(namePrefix, false);
        string sql = Regex.Replace(command.CommandText, inListRegex, _ => parameterList, InListOptions);
        if (sql == command.CommandText || sql.Length > MaxSqlLength)
        {
            return;
        }
        command.CommandText = sql;
        __runOriginal = false;
    }

    /// Lists Dapper would expand into one parameter per item at command level which makes it bloody slow, minutes vs seconds in worst cases.
    private static bool CanParametersBeInlineOptimized(IDbCommand command, object value, out string[] ids)
    {
        ids = value as string[] ?? [];
        return ids.Length >= MinListItems
            && !ids.Contains(null) // Nulls break stuff later, so fall back to standard behaviour
            && command is SqliteCommand
            && !SqlMapper.ShouldPassByPosition(command.CommandText); // What Dapper uses internally to classify it
    }
}
