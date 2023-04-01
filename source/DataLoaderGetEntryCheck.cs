using System;
using System.Collections.Generic;
using HBS.Data;
using System.IO;

namespace BattletechPerformanceFix;

// Shaves off about a second of load time due to no file exists check or atime read
public class DataLoaderGetEntryCheck : Feature
{
    public void Activate()
    {
        Main.harmony.PatchAll(typeof(DataLoaderGetEntryCheck));
    }

    static DateTime dummyTime = DateTime.UtcNow;

    public static DateTime GetLastWriteTimeUtcStub(string path) => dummyTime;
    public static bool ExistsStub(string path) => true;

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(DataLoader), nameof(DataLoader.GetEntry))]
    public static IEnumerable<CodeInstruction> GetEntry(IEnumerable<CodeInstruction> ins)
    {
        return ins.MethodReplacer( AccessTools.Method(typeof(File), nameof(File.Exists))
                , AccessTools.Method(typeof(DataLoaderGetEntryCheck), nameof(ExistsStub)))
            .MethodReplacer( AccessTools.Method(typeof(File), nameof(File.GetLastWriteTimeUtc))
                , AccessTools.Method(typeof(DataLoaderGetEntryCheck), nameof(GetLastWriteTimeUtcStub)));
    }
}