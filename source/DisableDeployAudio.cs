using BattleTech.UI;
using BattleTech;

namespace BattletechPerformanceFix;

/* 4, maybe 5 second faster load times.
 * The audio clip delays the load, and can't (easily) be played in parallel
 *   due to how aggressive Btech is about unloading assets.
 */
class DisableDeployAudio : Feature
{
    public void Activate()
    {
        Main.harmony.PatchAll(typeof(DisableDeployAudio));
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(LanceConfiguratorPanel), nameof(LanceConfiguratorPanel.ContinueConfirmClicked))]
    public static void ContinueConfirmClicked_Pre(ref bool __runOriginal, ref float __state)
    {
        if (!__runOriginal)
        {
            return;
        }

        __state = AudioEventManager.VoiceVolume;
        AudioEventManager.VoiceVolume = 0;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(LanceConfiguratorPanel), nameof(LanceConfiguratorPanel.ContinueConfirmClicked))]
    public static void ContinueConfirmClicked_Post(ref float __state)
    {
        AudioEventManager.VoiceVolume = __state;
    }
}