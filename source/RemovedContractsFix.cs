using BattleTech;
using BattleTech.Save;

namespace BattletechPerformanceFix;

class RemovedContractsFix : Feature
{
    public void Activate()
    {
        "Rehydrate".Pre<SimGameState>();
    }

    static void Rehydrate_Pre(SimGameState __instance, GameInstanceSave gameInstanceSave)
    {
        var contractOverrides = __instance.DataManager.ContractOverrides;
        gameInstanceSave.SimGameSave.ContractBits.RemoveAll(item => !contractOverrides.Exists(item.conName));
    }
}