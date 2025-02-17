using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using BattletechPerformanceFix.MechLabFix;
using Newtonsoft.Json;

namespace BattletechPerformanceFix;

public static class Main
{
    public static Harmony harmony;

    public static readonly string ModName = "BattletechPerformanceFix";
    public static readonly string ModPack = "com.github.m22spencer";
    public static readonly string ModFullName = $"{ModPack}.{ModName}";
    public static string ModDir;
    public static string SettingsPath { get => Path.Combine(ModDir, "Settings.json"); }

    public static Settings settings = new();

    public static void Start(string modDirectory, string json)
    {
        ModDir = modDirectory;

        LoadSettingsAndSetupLogger();

        harmony = new Harmony(ModFullName);

        var allFeatures = new Dictionary<Type, bool> {
            { typeof(LocalizationPatches), true },
            { typeof(MechLabFixFeature), false },
            { typeof(NoSalvageSoftlock), true },
            { typeof(DataLoaderGetEntryCheck), true },
            { typeof(ShopTabLagFix), true },
            { typeof(ContractLagFix), true },
            { typeof(EnableLoggingDuringLoads), true },
            { typeof(ExtraLogging), true },
            { typeof(ShaderDependencyOverride), true },
            { typeof(DisableDeployAudio), false },
            { typeof(RemovedFlashpointFix), true },
            { typeof(DisableSimAnimations), false },
            { typeof(RemovedContractsFix), true },
            { typeof(EnableConsole), false },
        };

        var want = allFeatures.ToDictionary(f => f.Key, f => settings.features.TryGetValue(f.Key.Name, out var userBool) ? userBool : f.Value);
        settings.features = want.ToDictionary(kv => kv.Key.Name, kv => kv.Value);
        File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(settings, Formatting.Indented));

        var alwaysOn = new Dictionary<Type, bool>();

        var allwant = alwaysOn.Concat(want);

        Log.Main.Info?.Log("Features ----------");
        foreach (var feature in allwant)
        {
            Log.Main.Info?.Log($"Feature {feature.Key.Name} is {(feature.Value ? "ON" : "OFF")}");
        }
        Log.Main.Info?.Log("Patches ----------");
        foreach (var feature in allwant)
        {
            if (feature.Value) {
                try
                {
                    Log.Main.Info?.Log($"Feature {feature.Key.Name}:");
                    var f = (Feature)AccessTools.CreateInstance(feature.Key);
                    f.Activate();
                } catch (Exception e)
                {
                    Log.Main.Error?.Log($"Failed to activate feature {feature.Key} with:\n {e}\n");
                }
            }
        }
        Log.Main.Info?.Log("Runtime ----------");

        harmony.PatchAll(Assembly.GetExecutingAssembly());

        Log.Main.Info?.Log("Patch out sensitive data log dumps");
        new DisableSensitiveDataLogDump().Activate();
    }

    public static MethodBase CheckPatch(MethodBase meth, params string[] sha256s)
    {
        Log.Main.Trace?.Log("CheckPatch is NYI");
        if (meth == null)
        {
            Log.Main.Error?.Log("A CheckPatch recieved a null method, this is fatal");
        }

        return meth;
    }

    private static void LoadSettingsAndSetupLogger()
    {
        Exception settingsEx;
        try
        {
            settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(SettingsPath));
            settingsEx = null;
        }
        catch (Exception e)
        {
            settingsEx = e;
        }

        if (settingsEx != null)
        {
            Log.Main.Warning?.Log("Settings file is invalid or missing, using defaults", settingsEx);
        }
    }
}

public interface Feature
{
    void Activate();
}