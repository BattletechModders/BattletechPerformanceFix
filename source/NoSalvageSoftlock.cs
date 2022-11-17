using Harmony;
using System;
using BattleTech.UI;

namespace BattletechPerformanceFix;

public class NoSalvageSoftlock : Feature
{
    public void Activate()
    {
        var hap = Main.CheckPatch(AccessTools.Method(typeof(AAR_SalvageChosen), nameof(AAR_SalvageChosen.HasAllPriority))
            , "80d43f27b8537a10099fd1ebceb4b6961549f30518c00de53fcf38c27623f7ec");
        Main.harmony.Patch(hap
            , new(typeof(NoSalvageSoftlock), nameof(NoSalvageSoftlock.HasAllPriority)), null);
    }

    public static bool HasAllPriority(AAR_SalvageChosen __instance, ref bool __result)
    {
        try
        {
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
            return false;
        } catch (Exception e)
        {
            Log.Main.Error?.Log("Encountered exception", e);
            return true;
        }
    }
}