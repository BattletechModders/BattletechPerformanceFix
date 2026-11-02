using System;
using System.Collections.Generic;
using HBS.Data;
using System.IO;

namespace BattletechPerformanceFix;

// Shaves off significant time from loading a game by replacing DataLoader.GetEntry with a dictionary lookup instead of
// iterating over a list with a string Equals check, which is slow as resource count increases.
// further time is saved by not checking file existence and last modification time read.
public class DataLoaderGetEntryCheck : Feature
{
    public void Activate()
    {
        Main.harmony.PatchAll(typeof(DataLoaderGetEntryCheck));
    }

    static DateTime dummyTime = DateTime.UtcNow;
    private static Dictionary<string,  DataLoader.Entry> _entries = new Dictionary<string, DataLoader.Entry>();
    
    [HarmonyPrefix]
    [HarmonyPatch(typeof(DataLoader), nameof(DataLoader.OnDestroy))]
    public static void OnDestroy(ref bool __runOriginal)
    {
        if (!__runOriginal)
        {
            return;
        }
        
        _entries = null;
    }
    
    [HarmonyPrefix]
    [HarmonyPatch(typeof(DataLoader), nameof(DataLoader.GetEntry))]
    public static void GetEntry(ref bool __runOriginal, string path, bool createIfNecessary, ref DataLoader.Entry __result)
    {
        if (!__runOriginal)
        {
            return;
        }
        
        __runOriginal = false;
        if (_entries.TryGetValue(path, out var existingEntry))
        {
            __result = existingEntry;
            return;
        }

        if (!createIfNecessary)
        {
            __result = null;
            return;
        }
        var entry = new DataLoader.Entry
        {
            path = path,
            lastWriteTimeUtc = dummyTime,
        };
        _entries.Add(path, entry);
        __result = entry;
    }
    
    [HarmonyPrefix]
    [HarmonyPatch(typeof(DataLoader), nameof(DataLoader.CheckFiles))]
    public static void CheckFiles(ref bool __runOriginal, DataLoader __instance, bool forceLoad)
    {
        if (!__runOriginal)
        {
            return;
        }
        
        __runOriginal = false;
        foreach (var entry in _entries.Values)
        {
            DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(entry.path);
            if (forceLoad || lastWriteTimeUtc != entry.lastWriteTimeUtc)
            {
                if (forceLoad && DataLoader.logger.IsDebugEnabled)
                    DataLoader.logger.LogDebug("File has changed, reloading: " + entry.path);
                entry.lastWriteTimeUtc = lastWriteTimeUtc;
                __instance.CallHandler(entry.path, entry.handler);
            }
        }
    }
}