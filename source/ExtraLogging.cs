using BattleTech;
using BattleTech.Framework;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix;

class ExtraLogging : Feature
{
    public void Activate() {
        Main.harmony.PatchAll(typeof(ExtraLogging));
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(LanceOverride), nameof(LanceOverride.RequestLance))]
    public static void RequestLance_Pre(ref bool __runOriginal, int requestedDifficulty, Contract contract, int ___lanceDifficultyAdjustment) {
        if (!__runOriginal)
        {
            return;
        }

        Log.Main.Info?.Log($"(CL) LanceOverride::RequestLance :contract.Name {contract?.Name} :requestedDifficulty {requestedDifficulty} :lanceDifficultyAdjustment {___lanceDifficultyAdjustment} :contract.Difficulty {contract?.Difficulty}");
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(SimGameState), nameof(SimGameState.PrepContract))]
    public static void PrepContract_Pre(ref bool __runOriginal, SimGameState __instance, Contract contract, StarSystem system) {
        if (!__runOriginal)
        {
            return;
        }

        var GD = system.Def.GetDifficulty(__instance.SimGameMode);
        Log.Main.Info?.Log($"(CL) SimGameState::PrepContract(pre) :contract.Name {contract?.Name} :contract.Difficulty {contract?.Difficulty} :GetDifficulty {GD} :GlobalDifficulty {__instance.GlobalDifficulty} :ContractDifficultyVariance {__instance.Constants.Story.ContractDifficultyVariance}");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SimGameState), nameof(SimGameState.PrepContract))]
    public static void PrepContract_Post(ref bool __runOriginal, SimGameState __instance, Contract contract, StarSystem system) {
        if (!__runOriginal)
        {
            return;
        }

        var fd = Trap(() => contract?.Override.finalDifficulty);
        Log.Main.Info?.Log($"(CL) SimGameState::PrepContract(post) :contract.Name {contract?.Name} :contract.Difficulty {contract?.Difficulty} :contract.Override.finalDifficulty {fd} :UIDifficulty {contract?.Override?.GetUIDifficulty()}");
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(LanceSpawnerGameLogic), nameof(LanceSpawnerGameLogic.InitializeTaggedLance))]
    public static void InitializeTaggedLance_Pre(ref bool __runOriginal) {
        if (!__runOriginal)
        {
            return;
        }

        Log.Main.Info?.Log($"(CL) Initialize tagged lance (hardcoded difficulty 5)");
    }
}