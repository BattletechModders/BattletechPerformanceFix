using BattleTech.UI;

namespace BattletechPerformanceFix;

public class NoSalvageSoftlock : Feature
{
    public void Activate()
    {
        Main.harmony.PatchAll(typeof(NoSalvageSoftlock));
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AAR_SalvageChosen), nameof(AAR_SalvageChosen.HasAllPriority))]
    [HarmonyWrapSafe]
    public static void HasAllPriority(ref bool __runOriginal, AAR_SalvageChosen __instance, ref bool __result)
    {
        if (!__runOriginal)
        {
            return;
        }

        var negotiated = __instance.contract.FinalPrioritySalvageCount;
        var totalSalvageMadeAvailable = __instance.parent.TotalSalvageMadeAvailable;
        var count = __instance.PriorityInventory.Count;
        var num = negotiated;
        if (num > totalSalvageMadeAvailable)
        {
            num = totalSalvageMadeAvailable;
        }
        if (num > 7)
        {
            num = 7;
        }
        Log.Main.Debug?.Log($"HasAllPriority :negotiated {negotiated} :available {totalSalvageMadeAvailable} :selected {count} :clamped {num}");
        __result = count >= num;
        __runOriginal = false;
    }
}