using BattleTech;
using BattleTech.UI;

namespace BattletechPerformanceFix;

class DisableSimAnimations : Feature
{
    public void Activate()
    {
        Main.harmony.PatchAll(typeof(DisableSimAnimations));
    }

    // disables scene transition animation
    [HarmonyPrefix]
    [HarmonyPatch(typeof(SimGameCameraController), nameof(SimGameCameraController.Init))]
    public static void Init_Pre(ref bool __runOriginal, ref float ___betweenRoomTransitionTime, ref float ___inRoomTransitionTime)
    {
        if (!__runOriginal)
        {
            return;
        }

        ___betweenRoomTransitionTime = ___inRoomTransitionTime = 0f;
    }

    // disables mech transition animation
    [HarmonyPrefix]
    [HarmonyPatch(typeof(SGRoomController_MechBay), nameof(SGRoomController_MechBay.TransitionMech))]
    public static void TransitionMech_Pre(ref bool __runOriginal, ref float fadeDuration)
    {
        if (!__runOriginal)
        {
            return;
        }

        UnityGameInstance.BattleTechGame.Simulation.CameraController.mechLabSpin = null;
        fadeDuration = 0f;
    }
}