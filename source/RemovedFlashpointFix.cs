using BattleTech;
using BattleTech.Save;

namespace BattletechPerformanceFix;

class RemovedFlashpointFix : Feature
{
    public void Activate()
    {
        Main.harmony.PatchAll(typeof(RemovedFlashpointFix));
    }


    [HarmonyPrefix]
    [HarmonyPatch(typeof(SimGameState), nameof(SimGameState.Rehydrate))]
    static void Rehydrate_Pre(ref bool __runOriginal, SimGameState __instance, GameInstanceSave gameInstanceSave)
    {
        if (!__runOriginal)
        {
            return;
        }

        gameInstanceSave.SimGameSave.AvailableFlashpointList.RemoveAll(item => item.Def == null);
    }

}