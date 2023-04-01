namespace BattletechPerformanceFix;

// BattleTech disables logging during load. Not sure why.
//   this re-enables it to give more information about errors in log.
class EnableLoggingDuringLoads : Feature
{
    public void Activate() {
        Main.harmony.PatchAll(typeof(EnableLoggingDuringLoads));
    }

    [HarmonyPatch(typeof(BattleTech.LevelLoader), nameof(BattleTech.LevelLoader.EnableLogging))]
    [HarmonyPrefix]
    public static void EnableLogging(ref bool __runOriginal, ref bool enable)
    {
        if (!__runOriginal)
        {
            return;
        }

        enable = true;
    }
}