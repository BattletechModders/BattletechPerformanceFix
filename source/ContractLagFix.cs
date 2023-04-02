using System.Collections.Generic;
using System.Linq;
using BattleTech;
using BattleTech.Framework;
using System.Reflection;
using static System.Reflection.Emit.OpCodes;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix;

class ContractLagFix : Feature
{
    public void Activate()
    {
        Main.harmony.PatchAll(typeof(ContractLagFix));
        Main.harmony.PatchAll(typeof(EncounterObjectRef_UpdateEncounterObjectRef_Patch));
    }

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

    public static class EncounterObjectRef_UpdateEncounterObjectRef_Patch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods()
        {
            return typeof(ObjectiveRef).Assembly.GetTypes()
                .Select(t => t.BaseType)
                .Where(t => t != null && t.Name.StartsWith(nameof(EncounterObjectRef)))
                .Select(t => AccessTools.Method(t, nameof(EncounterObjectRef.UpdateEncounterObjectRef)));
        }

        [HarmonyTranspiler]
        [HarmonyPriority(Priority.First)]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == Call && instruction.operand is MethodInfo mi && mi.Name.StartsWith("FindObjectOfType"))
                {
                    instruction.operand = AccessTools.Method(typeof(ContractLagFix), nameof(CachedEncounterLayerData));
                }
                yield return instruction;
            }
        }
    }
}