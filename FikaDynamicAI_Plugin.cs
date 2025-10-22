using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using FikaDynamicAI.Patches;
using FikaDynamicAI.Scripts;
using System;

namespace FikaDynamicAI;

[BepInPlugin("com.lacyway.fda", "FikaDynamicAI", "1.0.0")]
internal class FikaDynamicAI_Plugin : BaseUnityPlugin
{
    internal static ManualLogSource PluginLogger;

    public static ConfigEntry<float> DynamicAIRange { get; set; }
    public static ConfigEntry<EDynamicAIRates> DynamicAIRate { get; set; }
    public static ConfigEntry<bool> DynamicAIIgnoreSnipers { get; set; }

    internal static void DynamicAIRate_SettingChanged(object sender, EventArgs e)
    {
        if (FikaDynamicAIManager.Instance != null)
        {
            FikaDynamicAIManager.Instance.RateChanged(DynamicAIRate.Value);
        }
    }

    protected void Awake()
    {
        PluginLogger = Logger;
        PluginLogger.LogInfo($"{nameof(FikaDynamicAI_Plugin)} has been loaded.");

        const string header = "Fika - Dynamic AI";

        DynamicAIRange = Config.Bind(header, "Dynamic AI Range", 100f,
            new ConfigDescription("The range at which AI will be disabled if no player is within said range.",
            new AcceptableValueRange<float>(50f, 1000f)));
        DynamicAIRate = Config.Bind(header, "Dynamic AI Rate", EDynamicAIRates.Medium,
            new ConfigDescription("How often DynamicAI should scan for the range from all players."));
        DynamicAIIgnoreSnipers = Config.Bind(header, "Ignore Sniper", false,
            new ConfigDescription("Whether Dynamic AI should ignore sniper scavs."));

        new BotsController_SetSettings_Postfix().Enable();
        new BotsEventsController_SpawnAction_Postfix().Enable();
        new HostGameController_StopBotsSystem_Postfix().Enable();
    }

    public enum EDynamicAIRates
    {
        Low,
        Medium,
        High
    }
}
