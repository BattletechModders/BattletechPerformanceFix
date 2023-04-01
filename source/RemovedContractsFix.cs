using BattleTech;
using BattleTech.Save;

namespace BattletechPerformanceFix;

class RemovedContractsFix : Feature
{
    public void Activate()
    {
        Main.harmony.PatchAll(typeof(RemovedContractsFix));
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(SimGameState), nameof(SimGameState.Rehydrate))]
    static void Rehydrate_Pre(ref bool __runOriginal, SimGameState __instance, GameInstanceSave gameInstanceSave)
    {
        if (!__runOriginal)
        {
            return;
        }

        var contractOverrides = __instance.DataManager.ContractOverrides;
        gameInstanceSave.SimGameSave.ContractBits.RemoveAll(item => !contractOverrides.Exists(item.conName));
    }
}