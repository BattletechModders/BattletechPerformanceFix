using BattleTech;
using System;

namespace BattletechPerformanceFix;

class LocalizationPatches : Feature
{
    // If enabled, runs both vanilla & bfix versions, checking for desyncs.
    private static bool Verify = false;
    public void Activate()
    {
        Main.harmony.PatchAll(typeof(LocalizationPatches));
    }

    // String with non number text between {} is rare, so only do the repair if format fails.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(StringsProviderBase<object>), nameof(StringsProviderBase<object>.StringForKeyFormat))]
    [HarmonyWrapSafe]
    public static void StringForKeyFormat_Pre(ref bool __runOriginal, StringsProviderBase<object> __instance, string key, ref string __result, ref (bool,string)? __state, params object[] args)
    {
        if (!__runOriginal)
        {
            return;
        }

        var text = __instance.StringForKey(key);
        var formatted = key == null ? "" : (string.Format(text, args) ?? "");
        __state = (true, __result = formatted);
        __runOriginal = Verify;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(StringsProviderBase<object>), nameof(StringsProviderBase<object>.StringForKeyFormat))]
    public static void StringForKeyFormat_Post(StringsProviderBase<object> __instance, string key, ref string __result, ref (bool,string)? __state, params object[] args)
    {
        if (Verify && __state != null && __result != __state?.Item2)
            Log.Main.Error?.Log($"StringForKeyFormat.Assertion failed: \ncompare: {__result ?? "null"} == {__state?.Item2 ?? "null"} \nkey: {key ?? "null"} \nstring-for-key: {__instance.StringForKey(key) ?? "null"}\nargs: {args.Dump()}");
    }
}