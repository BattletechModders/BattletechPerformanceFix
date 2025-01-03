﻿using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BattleTech.Assetbundles;
using UnityEngine;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix;

/* 1.6 Has separated the shaders into a separate bundle
 * But the modded mechs aren't in the manifest, and therefor do not have dependencies.
 * This is a quickfix that ensures shaders are loaded if any prefab starting with chr
 * is requested. Asset bundles that do not stick to naming conventions will have pink
 * mechs.
 */
class ShaderDependencyOverride : Feature
{
    public void Activate()
    {
        Main.harmony.PatchAll(typeof(ShaderDependencyOverride));
    }

    private static AssetBundleManager manager;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AssetBundleManager), nameof(AssetBundleManager.IsBundleLoaded), typeof(string))]
    [HarmonyPatch(typeof(AssetBundleManager), nameof(AssetBundleManager.GenerateWebRequest))]
    public static void GetManager(ref bool __runOriginal, AssetBundleManager __instance)
    {
        if (!__runOriginal)
        {
            return;
        }

        if (manager == null)
        {
            Log.Main.Debug?.Log("Found bundle manager");
            manager = __instance;
        }
    }
        
    public static string[] GetAllDependenciesOverride(AssetBundleManifest manifest, string bundleName)
    {
        var deps = manifest.GetAllDependencies(bundleName);
        Log.Main.Debug?.Log($":load " + bundleName.Dump(false) + " :deps " + deps.Dump(false));
        if (bundleName.StartsWith("chr") && !manager.IsBundleLoaded("shaders"))
        {
            return Sequence("shaders").Concat(deps).ToArray();
        }
        else return deps;
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(AssetBundleManager), nameof(AssetBundleManager.IsBundleLoaded), typeof(string))]
    [HarmonyPatch(typeof(AssetBundleManager), nameof(AssetBundleManager.GenerateWebRequest))]
    public static IEnumerable<CodeInstruction> WithShaderDeps(IEnumerable<CodeInstruction> ins)
    {
        return ins.Select(i =>
        {
            if (i.opcode == OpCodes.Callvirt && (i.operand as MethodBase)?.Name == "GetAllDependencies")
            {
                i.opcode = OpCodes.Call;
                i.operand = AccessTools.Method(typeof(ShaderDependencyOverride), nameof(GetAllDependenciesOverride));
            }
            return i;
        });
    }
}