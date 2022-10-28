using Harmony;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using BattletechPerformanceFix.MechLabFix;
using Newtonsoft.Json;
using HBS.Logging;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix;

public static class Main
{
    public static HarmonyInstance harmony;

    public static readonly string ModName = "BattletechPerformanceFix";
    public static readonly string ModPack = "com.github.m22spencer";
    public static readonly string ModFullName = $"{ModPack}.{ModName}";
    public static string ModDir;
    public static string SettingsPath { get => Path.Combine(ModDir, "Settings.json"); }

    public static Settings settings = new();

    public static ILog HBSLogger;

    public static void Start(string modDirectory, string json)
    {
        HBSLogger = Logger.GetLogger(ModName, LogLevel.Debug);

        ModDir = modDirectory;
        try { settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(SettingsPath)); }
        catch { LogWarning("Settings file is invalid or missing, regenerating with defaults"); }
        Logger.SetLoggerLevel(ModName, Enum.TryParse<LogLevel>(settings.logLevel, out var level) ? level : LogLevel.Debug);

        harmony = HarmonyInstance.Create(ModFullName);

        var allFeatures = new Dictionary<Type, bool> {
            { typeof(LocalizationPatches), true },
            { typeof(MechLabFixFeature), true },
            { typeof(LoadFixes), true },
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

        LogInfo("Features ----------");
        foreach (var feature in allwant)
        {
            LogInfo($"Feature {feature.Key.Name} is {(feature.Value ? "ON" : "OFF")}");
        }
        LogInfo("Patches ----------");
        foreach (var feature in allwant)
        {
            if (feature.Value) {
                try
                {
                    LogInfo($"Feature {feature.Key.Name}:");
                    var f = (Feature)AccessTools.CreateInstance(feature.Key);
                    f.Activate();
                } catch (Exception e)
                {
                    LogError($"Failed to activate feature {feature.Key} with:\n {e}\n");
                }
            }
        }
        LogInfo("Runtime ----------");

        harmony.PatchAll(Assembly.GetExecutingAssembly());

        LogInfo("Patch out sensitive data log dumps");
        new DisableSensitiveDataLogDump().Activate();
    }

    public static MethodBase CheckPatch(MethodBase meth, params string[] sha256s)
    {
        LogSpam("CheckPatch is NYI");
        if (meth == null)
        {
            LogError("A CheckPatch recieved a null method, this is fatal");
        }

        return meth;
    }
}

public interface Feature
{
    void Activate();
}