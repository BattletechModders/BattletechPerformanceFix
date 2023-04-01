using HBS.DebugConsole;
using UnityEngine;

namespace BattletechPerformanceFix;

class EnableConsole : Feature
{
    public void Activate()
    {
        Main.harmony.PatchAll(typeof(EnableConsole));
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(DebugConsole), nameof(DebugConsole.DebugCommandsUnlocked), MethodType.Getter)]
    public static void get_DebugCommandsUnlocked_Post(ref bool __result)
        => __result = true;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(DebugConsoleHelper), nameof(DebugConsoleHelper.LateUpdate))]
    public static void LateUpdate_Post(DebugConsoleHelper __instance)
    {
        var console = __instance.console;
        if (Input.GetKeyDown(KeyCode.BackQuote))
            __instance.console.SetMode(console.mode == DebugConsole.WindowMode.LogWindow 
                ? DebugConsole.WindowMode.Hidden 
                : DebugConsole.WindowMode.LogWindow);
    }
}