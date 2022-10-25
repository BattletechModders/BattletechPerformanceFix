using BattleTech;
using BattleTech.Save;

namespace BattletechPerformanceFix;

class RemovedFlashpointFix : Feature
{
    public void Activate()
    {
        "Rehydrate".Pre<SimGameState>();
    }


    static void Rehydrate_Pre(SimGameState __instance, GameInstanceSave gameInstanceSave)
    {
        gameInstanceSave.SimGameSave.AvailableFlashpointList.RemoveAll(item => item.Def == null);
    }

}