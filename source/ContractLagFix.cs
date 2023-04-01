using System.Collections.Generic;
using System.Linq;
using BattleTech;
using BattleTech.Framework;
using System.Reflection;
using System.Diagnostics;
using static System.Reflection.Emit.OpCodes;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix;

class ContractLagFix : Feature
{
    public void Activate()
    {
        Main.harmony.PatchAll(typeof(ContractLagFix));
    }

    static Stopwatch sw = new();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(EncounterLayerData), MethodType.Constructor)]
    public static void EncounterLayerData_Constructor(EncounterLayerData __instance)
    {
        eld_cache = eld_cache.Where(c => c != null).ToList();
        eld_cache.Add(__instance);
    }

    static List<EncounterLayerData> eld_cache = new();

    public static EncounterLayerData CachedEncounterLayerData()
    {
        return Trap(() =>
        {
            var cached = eld_cache.FirstOrDefault(c => c != null && c.isActiveAndEnabled);

            if (Main.settings.WantContractsLagFixVerify) {
                Log.Main.Trace?.Log("Verify ELD");
                var wants = UnityEngine.Object.FindObjectOfType<EncounterLayerData>();

                Trap(() => {
                    if (cached != wants)
                    {
                        var inscene = UnityEngine.Object.FindObjectsOfType<EncounterLayerData>();
                        Log.Main.Error?.Log($"eld_cache is out of sync, wants: {wants?.GUID ?? "null"}");
                        Log.Main.Error?.Log($"scene contains ({string.Join(" ", inscene.Select(c => c == null ? "null" : $"(:contractDefId {c.contractDefId} :contractDefIndex {c.contractDefIndex} :GUID {c.GUID})").ToArray())})");
                        Log.Main.Error?.Log($"current EncounterLayerData ({string.Join(" ", eld_cache.Select(c => c == null ? "null" : $"(:contractDefId {c.contractDefId} :contractDefIndex {c.contractDefIndex} :GUID {c.GUID})").ToArray())})");
                        AlertUser( "ContractsLagFix: Verify error"
                            , "Please report this to the BT Modding group, and include logs");
                    }
                });
                return wants;
            }

            return cached;
        });
    }

    [HarmonyTranspiler]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(EncounterObjectRef), nameof(EncounterObjectRef.UpdateEncounterObjectRef))]
    public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> ins)
    {
        return ins.SelectMany(i =>
        {
            if (i.opcode == Call && (i.operand as MethodInfo).Name.StartsWith("FindObjectOfType"))
            {
                i.operand = AccessTools.Method(typeof(ContractLagFix), nameof(CachedEncounterLayerData));
                return Sequence(i);
            } else
            {
                return Sequence(i);
            }
        });
    }
}