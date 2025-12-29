using BepInEx.Logging;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using Fika.Core;
using Fika.Core.Main.Components;
using Fika.Core.Main.GameMode;
using Fika.Core.Main.Players;
using System.Collections.Generic;
using UnityEngine;

namespace FikaDynamicAI.Scripts;

public class FikaDynamicAIManager : MonoBehaviour
{
    public static FikaDynamicAIManager Instance { get; private set; }

    private readonly ManualLogSource _logger = BepInEx.Logging.Logger.CreateLogSource("DynamicAI");
    private CoopHandler _coopHandler;
    private float _rateMultiplier = 1.0f;
    private readonly List<FikaPlayer> _humanPlayers = [];
    private readonly List<FikaBot> _bots = [];
    private readonly HashSet<FikaBot> _disabledBots = [];
    private BotSpawner _spawner;
    
    // Cached config entries for the current map
    private ConfigEntry<bool> _currentMapEnabledConfig;
    private ConfigEntry<float> _currentMapRangeConfig;

    protected void Awake()
    {
        if (FikaPlugin.Instance.ModHandler.QuestingBotsLoaded)
        {
            _logger.LogWarning("QuestingBots detected, destroying DynamicAI component. Use QuestingBots AI limiter instead!");
            Destroy(this);
        }

        if (!CoopHandler.TryGetCoopHandler(out _coopHandler))
        {
            _logger.LogError("Could not find CoopHandler! Destroying self");
            Destroy(this);
            return;
        }

        // Cache the config entries for the current map ONCE.
        // This avoids string allocations and switch logic in the Update loop.
        SetupMapConfigs();

        // Initialize multiplier
        RateChanged(FikaDynamicAI_Plugin.DynamicAIRate.Value);

        _spawner = Singleton<IBotGame>.Instance.BotsController.BotSpawner;
        if (_spawner == null)
        {
            _logger.LogError("Could not find BotSpawner! Destroying self");
            Destroy(this);
            return;
        }

        _spawner.OnBotCreated += Spawner_OnBotCreated;
        _spawner.OnBotRemoved += Spawner_OnBotRemoved;

        if (Instance != null)
        {
            Instance.DestroyComponent();
        }
        Instance = this;

        FikaDynamicAI_Plugin.DynamicAIRate.SettingChanged += FikaDynamicAI_Plugin.DynamicAIRate_SettingChanged;
    }

    private void SetupMapConfigs()
    {
        string locationId = Singleton<GameWorld>.Instance.LocationId.ToLower();

        switch (locationId)
        {
            case "factory4_day":
            case "factory4_night":
                _currentMapEnabledConfig = FikaDynamicAI_Plugin.EnableFactory;
                _currentMapRangeConfig = FikaDynamicAI_Plugin.RangeFactory;
                break;
            case "bigmap":
                _currentMapEnabledConfig = FikaDynamicAI_Plugin.EnableCustoms;
                _currentMapRangeConfig = FikaDynamicAI_Plugin.RangeCustoms;
                break;
            case "woods":
                _currentMapEnabledConfig = FikaDynamicAI_Plugin.EnableWoods;
                _currentMapRangeConfig = FikaDynamicAI_Plugin.RangeWoods;
                break;
            case "shoreline":
                _currentMapEnabledConfig = FikaDynamicAI_Plugin.EnableShoreline;
                _currentMapRangeConfig = FikaDynamicAI_Plugin.RangeShoreline;
                break;
            case "interchange":
                _currentMapEnabledConfig = FikaDynamicAI_Plugin.EnableInterchange;
                _currentMapRangeConfig = FikaDynamicAI_Plugin.RangeInterchange;
                break;
            case "rezervbase":
                _currentMapEnabledConfig = FikaDynamicAI_Plugin.EnableReserve;
                _currentMapRangeConfig = FikaDynamicAI_Plugin.RangeReserve;
                break;
            case "lighthouse":
                _currentMapEnabledConfig = FikaDynamicAI_Plugin.EnableLighthouse;
                _currentMapRangeConfig = FikaDynamicAI_Plugin.RangeLighthouse;
                break;
            case "tarkovstreets":
                _currentMapEnabledConfig = FikaDynamicAI_Plugin.EnableStreets;
                _currentMapRangeConfig = FikaDynamicAI_Plugin.RangeStreets;
                break;
            case "sandbox":
                _currentMapEnabledConfig = FikaDynamicAI_Plugin.EnableGroundZero;
                _currentMapRangeConfig = FikaDynamicAI_Plugin.RangeGroundZero;
                break;
            case "laboratory":
                _currentMapEnabledConfig = FikaDynamicAI_Plugin.EnableLabs;
                _currentMapRangeConfig = FikaDynamicAI_Plugin.RangeLabs;
                break;
            default:
                // Fallback for modded maps - use generic configs if we had them, 
                // or just rely on the fallback logic. Here we null them to indicate fallback?
                // Or better: Use generic Fallback entries if we create them. 
                // For now, let's just use null and handle it.
                _currentMapEnabledConfig = null;
                _currentMapRangeConfig = null;
                break;
        }
    }

    internal void DestroyComponent()
    {
        FikaDynamicAI_Plugin.DynamicAIRate.SettingChanged -= FikaDynamicAI_Plugin.DynamicAIRate_SettingChanged;
        Instance = null;
        Destroy(this);
    }


    // Track when each bot should be checked next. Key: Bot InstanceID
    private readonly Dictionary<int, float> _botNextCheckTime = [];

    protected void Update()
    {
        // If map is disabled via config, ensure all bots are active and do nothing else
        if (!IsMapEnabled())
        {
            if (_disabledBots.Count > 0)
            {
                EnabledChange(false); // Activates all disabled bots
            }
            return;
        }

        float time = Time.time;
        float currentMapRange = GetRangeForCurrentMap();
        float currentMapRangeSqr = currentMapRange * currentMapRange;

        // Iterate backwards so we can safely handle potential removals if needed,
        // though we generally modify _bots in event handlers. Use for-loop for performance.
        for (int i = _bots.Count - 1; i >= 0; i--)
        {
            FikaBot bot = _bots[i];
            
            if (bot == null) 
            {
                _bots.RemoveAt(i);
                continue;
            }

            int botId = bot.Id;
            // Initialize check time if new
            if (!_botNextCheckTime.TryGetValue(botId, out float nextCheck))
            {
                nextCheck = time + UnityEngine.Random.Range(0f, 0.5f); // Initial jitter to stagger
                _botNextCheckTime[botId] = nextCheck;
            }

            if (time < nextCheck)
            {
                continue;
            }

            // Perform check
            float distanceSqr = CheckForPlayers(bot, currentMapRangeSqr);

            // Determine next check interval based on distance
            // "Smart LOD": Far bots update less frequently
            // Distances are Squared! 
            // 400m^2 = 160,000
            // 150m^2 = 22,500
            
            float interval;
            if (distanceSqr > 250000) // > 500m
            {
                interval = 3.0f;
            }
            else if (distanceSqr > 40000) // > 200m
            {
                interval = 1.0f;
            }
            else if (distanceSqr > 4900) // > 70m
            {
                interval = 0.5f;
            }
            else // < 70m (Very close)
            {
                interval = 0.25f;
            }

            // Apply global rate multiplier
            interval *= _rateMultiplier;

             // Add small jitter to prevent clumping
            _botNextCheckTime[botId] = time + interval + UnityEngine.Random.Range(0f, 0.1f);
        }
    }

    private void Spawner_OnBotCreated(BotOwner botOwner)
    {
        if (botOwner.IsYourPlayer || !botOwner.IsAI)
        {
            return;
        }

        // Check if this bot type should be affected by Dynamic AI
        if (!ShouldTrackBot(botOwner))
        {
            return;
        }

        _bots.Add((FikaBot)botOwner.GetPlayer);
    }

    /// <summary>
    /// Determines if a bot should be tracked and affected by Dynamic AI based on its role and config settings.
    /// </summary>
    private bool ShouldTrackBot(BotOwner botOwner)
    {
        WildSpawnType role = botOwner.Profile.Info.Settings.Role;

        // BTR shooter is never affected - would break BTR mechanics
        if (role == WildSpawnType.shooterBTR)
        {
            return false;
        }

        return role switch
        {
            // Regular Scavs
            WildSpawnType.assault or
            WildSpawnType.cursedAssault or
            WildSpawnType.assaultGroup => FikaDynamicAI_Plugin.AffectScavs.Value,

            // Sniper Scavs
            WildSpawnType.marksman => FikaDynamicAI_Plugin.AffectSnipers.Value,

            // Rogues (Lighthouse)
            WildSpawnType.exUsec => FikaDynamicAI_Plugin.AffectRogues.Value,

            // Raiders (Labs, Reserve)
            WildSpawnType.pmcBot => FikaDynamicAI_Plugin.AffectRaiders.Value,

            // PMCs
            WildSpawnType.pmcUSEC or
            WildSpawnType.pmcBEAR => FikaDynamicAI_Plugin.AffectPMCs.Value,

            // Cultists
            WildSpawnType.sectantPriest or
            WildSpawnType.sectantWarrior => FikaDynamicAI_Plugin.AffectCultists.Value,

            // Bosses
            WildSpawnType.bossKnight or
            WildSpawnType.bossBully or
            WildSpawnType.bossKilla or
            WildSpawnType.bossKojaniy or
            WildSpawnType.bossSanitar or
            WildSpawnType.bossTagilla or
            WildSpawnType.bossGluhar or
            WildSpawnType.bossZryachiy or
            WildSpawnType.bossKolontay or
            WildSpawnType.bossPartisan or
            WildSpawnType.bossBoar or
            WildSpawnType.bossBoarSniper => FikaDynamicAI_Plugin.AffectBosses.Value,

            // Boss followers/guards
            WildSpawnType.followerBully or
            WildSpawnType.followerKojaniy or
            WildSpawnType.followerSanitar or
            WildSpawnType.followerTagilla or
            WildSpawnType.followerGluharAssault or
            WildSpawnType.followerGluharScout or
            WildSpawnType.followerGluharSecurity or
            WildSpawnType.followerGluharSnipe or
            WildSpawnType.followerBigPipe or
            WildSpawnType.followerBirdEye or
            WildSpawnType.followerZryachiy or
            WildSpawnType.followerBoar or
            WildSpawnType.followerBoarClose1 or
            WildSpawnType.followerBoarClose2 or
            WildSpawnType.followerKolontayAssault or
            WildSpawnType.followerKolontaySecurity => FikaDynamicAI_Plugin.AffectFollowers.Value,

            // Default: track anything else
            _ => true
        };
    }

    private bool IsMapEnabled()
    {
        // Use cached config if available, otherwise default to true for modded maps
        return _currentMapEnabledConfig?.Value ?? true;
    }

    private float GetRangeForCurrentMap()
    {
        // Use cached config if available, otherwise default to global fallback
        return _currentMapRangeConfig?.Value ?? FikaDynamicAI_Plugin.DynamicAIRange.Value;
    }

    public void AddHumans()
    {
        _humanPlayers.AddRange(_coopHandler.HumanPlayers);
    }

    private void DeactivateBot(FikaBot bot)
    {
        if (!bot.HealthController.IsAlive)
        {
            return;
        }

#if DEBUG
        _logger.LogWarning($"Disabling {bot.gameObject.name}");
#endif
        bot.AIData.BotOwner.DecisionQueue.Clear();
        bot.AIData.BotOwner.Memory.GoalEnemy = null;
        bot.AIData.BotOwner.PatrollingData.Pause();
        bot.AIData.BotOwner.ShootData.EndShoot();
        bot.AIData.BotOwner.ShootData.CanShootByState = false;
        bot.ActiveHealthController.PauseAllEffects();
        bot.AIData.BotOwner.StandBy.StandByType = BotStandByType.paused;
        bot.AIData.BotOwner.StandBy.CanDoStandBy = false;
        bot.gameObject.SetActive(false);

        if (!_disabledBots.Add(bot))
        {
            _logger.LogError($"{bot.gameObject.name} was already in the disabled bots list when adding!");
        }

        IBotGame botGame = Singleton<IBotGame>.Instance;
        if (botGame == null)
        {
            return;
        }

        if (botGame is not HostGameController hostGameController)
        {
            return;
        }

        foreach (var otherBot in hostGameController.Bots.Values)
        {
            if (otherBot == bot)
            {
                continue;
            }

            if (!otherBot.gameObject.activeSelf)
            {
                continue;
            }

            if (otherBot.AIData.BotOwner?.Memory.GoalEnemy?.ProfileId == bot.ProfileId)
            {
                otherBot.AIData.BotOwner.Memory.GoalEnemy = null;
            }
        }
    }

    private void ActivateBot(FikaBot bot)
    {
#if DEBUG
        _logger.LogWarning($"Enabling {bot.gameObject.name}");
#endif
        bot.gameObject.SetActive(true);
        bot.AIData.BotOwner.PatrollingData.Unpause();
        bot.ActiveHealthController.UnpauseAllEffects();
        bot.AIData.BotOwner.StandBy.Activate();
        bot.AIData.BotOwner.StandBy.CanDoStandBy = true;
        bot.AIData.BotOwner.ShootData.CanShootByState = true;
        bot.AIData.BotOwner.ShootData.BlockFor(1f);
        _disabledBots.Remove(bot);
    }

    private void Spawner_OnBotRemoved(BotOwner botOwner)
    {
        FikaBot bot = (FikaBot)botOwner.GetPlayer;
        if (!_bots.Remove(bot))
        {
             // Log warning only if verified bot exists... 
             // actually standard remove is fine.
        }
        
        // Clean up tracker
        _botNextCheckTime.Remove(bot.Id);

        if (_disabledBots.Contains(bot))
        {
            _disabledBots.Remove(bot);
        }
    }
    
    // Returns the closest distance squared to a human player
    private float CheckForPlayers(FikaBot bot, float rangeSqr)
    {
        // Do not run on bots that have no initialized yet
        if (bot.AIData.BotOwner.BotState != EBotState.Active)
        {
            return 0f;
        }

        int notInRange = 0;
        // Start with a very large distance
        float minDistanceSqr = float.MaxValue;

        foreach (var humanPlayer in _humanPlayers)
        {
            if (humanPlayer == null || !humanPlayer.HealthController.IsAlive)
            {
                notInRange++;
                continue;
            }

            float distanceSqr = Vector3.SqrMagnitude(bot.Position - humanPlayer.Position);
            
            if (distanceSqr < minDistanceSqr)
            {
                minDistanceSqr = distanceSqr;
            }

            if (distanceSqr > rangeSqr)
            {
                notInRange++;
            }
        }

        if (notInRange >= _humanPlayers.Count && bot.gameObject.activeSelf)
        {
            DeactivateBot(bot);
        }
        else if (notInRange < _humanPlayers.Count && !bot.gameObject.activeSelf)
        {
            ActivateBot(bot);
        }
        
        return minDistanceSqr;
    }



    public void EnabledChange(bool value)
    {
        if (!value)
        {
            FikaBot[] disabledBotsArray = [.. _disabledBots];
            for (int i = 0; i < disabledBotsArray.Length; i++)
            {
                ActivateBot(disabledBotsArray[i]);
            }

            _disabledBots.Clear();
        }
    }

    internal void RefreshBotTracking()
    {
        IBotGame botGame = Singleton<IBotGame>.Instance;
        if (botGame?.BotsController?.Bots?.BotOwners == null)
        {
            return;
        }

        // Iterate over all bots currently in the game
        // We iterate backwards or use a copy if we were modifying the source collection, 
        // but here we modify our local _bots list, so iterating the source is fine.
        foreach (var botOwner in botGame.BotsController.Bots.BotOwners)
        {
            if (botOwner == null || botOwner.IsYourPlayer || !botOwner.IsAI)
            {
                continue;
            }

            FikaBot fikaBot = botOwner.GetPlayer as FikaBot;
            if (fikaBot == null)
            {
                continue;
            }

            bool shouldTrack = ShouldTrackBot(botOwner);
            bool isTracked = _bots.Contains(fikaBot);

            if (shouldTrack && !isTracked)
            {
                // New bot type enabled - Start tracking it
                _bots.Add(fikaBot);
#if DEBUG
                _logger.LogWarning($"[Refresh] Started tracking {fikaBot.name} ({botOwner.Profile.Info.Settings.Role})");
#endif
            }
            else if (!shouldTrack && isTracked)
            {
                // Bot type disabled - Stop tracking it
                if (_disabledBots.Contains(fikaBot))
                {
                    ActivateBot(fikaBot);
                }
                _bots.Remove(fikaBot);
#if DEBUG
                _logger.LogWarning($"[Refresh] Stopped tracking {fikaBot.name} ({botOwner.Profile.Info.Settings.Role})");
#endif
            }
        }
    }

    internal void RateChanged(FikaDynamicAI_Plugin.EDynamicAIRates value)
    {
        // Low rate = Higher multiplier (Less frequent checks)
        // High rate = Lower multiplier (More frequent checks)
        _rateMultiplier = value switch
        {
            FikaDynamicAI_Plugin.EDynamicAIRates.Low => 1.5f,
            FikaDynamicAI_Plugin.EDynamicAIRates.Medium => 1.0f,
            FikaDynamicAI_Plugin.EDynamicAIRates.High => 0.5f,
            _ => 1.0f,
        };
    }
}