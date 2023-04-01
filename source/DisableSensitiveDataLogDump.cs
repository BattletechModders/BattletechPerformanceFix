using BattleTech;

namespace BattletechPerformanceFix;

class DisableSensitiveDataLogDump : Feature
{
    public void Activate()
    {
        Main.harmony.PatchAll(typeof(DisableSensitiveDataLogDump));
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(UnityGameInstance), nameof(UnityGameInstance.OnInternetConnectivityResult))]
    public static void OnInternetConnectivityResult(ref bool __runOriginal, UnityGameInstance __instance, bool success)
    {
        if (!__runOriginal)
        {
            return;
        }

        __instance.InternetAvailable = success;
        __runOriginal = false;
    }
}