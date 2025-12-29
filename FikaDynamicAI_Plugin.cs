using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using FikaDynamicAI.Patches;
using FikaDynamicAI.Scripts;
using System;

namespace FikaDynamicAI;

[BepInPlugin("com.lacyway.fda", "FikaDynamicAI", "1.1.0")]
[BepInDependency("com.fika.core", BepInDependency.DependencyFlags.HardDependency)]
internal class FikaDynamicAI_Plugin : BaseUnityPlugin
{
    internal static ManualLogSource PluginLogger;

    // General Settings
    public static ConfigEntry<float> DynamicAIRange { get; set; }
    public static ConfigEntry<EDynamicAIRates> DynamicAIRate { get; set; }

    // Bot Type Filters - which types WILL be affected by Dynamic AI
    public static ConfigEntry<bool> AffectScavs { get; set; }
    public static ConfigEntry<bool> AffectPMCs { get; set; }
    public static ConfigEntry<bool> AffectRogues { get; set; }
    public static ConfigEntry<bool> AffectRaiders { get; set; }
    public static ConfigEntry<bool> AffectCultists { get; set; }
    public static ConfigEntry<bool> AffectBosses { get; set; }
    public static ConfigEntry<bool> AffectSnipers { get; set; }
    public static ConfigEntry<bool> AffectFollowers { get; set; }

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
        PluginLogger.LogInfo($"{nameof(FikaDynamicAI_Plugin)} v1.1.0 has been loaded.");

        const string generalHeader = "1. General";
        const string botTypesHeader = "2. Bot Types to Affect";

        // General Settings
        DynamicAIRange = Config.Bind(generalHeader, "Dynamic AI Range", 100f,
            new ConfigDescription("The range at which AI will be disabled if no player is within said range.",
            new AcceptableValueRange<float>(50f, 1000f)));
        DynamicAIRate = Config.Bind(generalHeader, "Dynamic AI Rate", EDynamicAIRates.Medium,
            new ConfigDescription("How often DynamicAI should scan for the range from all players."));

        // Bot Type Filters
        AffectScavs = Config.Bind(botTypesHeader, "Affect Scavs", true,
            new ConfigDescription("Whether Dynamic AI should affect regular Scavs."));
        AffectPMCs = Config.Bind(botTypesHeader, "Affect PMCs", false,
            new ConfigDescription("Whether Dynamic AI should affect PMC bots. Disable to let PMCs roam freely."));
        AffectRogues = Config.Bind(botTypesHeader, "Affect Rogues", false,
            new ConfigDescription("Whether Dynamic AI should affect Rogues (exUsec) at Lighthouse."));
        AffectRaiders = Config.Bind(botTypesHeader, "Affect Raiders", true,
            new ConfigDescription("Whether Dynamic AI should affect Raiders."));
        AffectCultists = Config.Bind(botTypesHeader, "Affect Cultists", true,
            new ConfigDescription("Whether Dynamic AI should affect Cultists."));
        AffectBosses = Config.Bind(botTypesHeader, "Affect Bosses", false,
            new ConfigDescription("Whether Dynamic AI should affect Boss characters."));
        AffectSnipers = Config.Bind(botTypesHeader, "Affect Snipers", false,
            new ConfigDescription("Whether Dynamic AI should affect Sniper Scavs (marksman)."));
        AffectFollowers = Config.Bind(botTypesHeader, "Affect Followers", true,
            new ConfigDescription("Whether Dynamic AI should affect Boss followers/guards."));

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
