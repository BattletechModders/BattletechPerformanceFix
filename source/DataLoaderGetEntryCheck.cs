using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HBS.Data;

namespace BattletechPerformanceFix;

// GetEntry previously checked each file against every entry loaded so far, leading to quadratic performance getting really shit with a large amount of files.
// An indexed lookup saves about 10% total load time, and is only rebuilt if needed (diff in file count).
//
// Also shaves off about a second of load time due to no file exists check or atime read
public class DataLoaderGetEntryCheck : Feature
{
    public void Activate()
    {
        Main.harmony.PatchAll(typeof(DataLoaderGetEntryCheck));
    }

    private static readonly DateTime dummyTime = DateTime.UtcNow;
    private static readonly ConditionalWeakTable<DataLoader, Dictionary<string, DataLoader.Entry>> Indexes = new();

    [HarmonyPrefix]
    [HarmonyPatch(typeof(DataLoader), nameof(DataLoader.GetEntry))]
    public static void GetEntry(ref bool __runOriginal, DataLoader __instance, string path, bool createIfNecessary, ref DataLoader.Entry __result)
    {
        if (!__runOriginal || path == null || __instance.monitoringEntries == null)
        {
            return;
        }

        List<DataLoader.Entry> entries = __instance.monitoringEntries;
        Dictionary<string, DataLoader.Entry> index = Indexes.GetOrCreateValue(__instance);
        if (index.Count != entries.Count)
        {
            index.Clear();
            foreach (DataLoader.Entry existing in entries)
            {
                if (!index.ContainsKey(existing.path))
                {
                    index[existing.path] = existing;
                }
            }
        }

        __runOriginal = false;
        if (index.TryGetValue(path, out DataLoader.Entry entry))
        {
            __result = entry;
            return;
        }
        if (!createIfNecessary)
        {
            __result = null;
            return;
        }
        entry = new DataLoader.Entry { path = path, lastWriteTimeUtc = dummyTime };
        entries.Add(entry);
        index[path] = entry;
        __result = entry;
    }
}
