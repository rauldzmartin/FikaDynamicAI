using BepInEx.Logging;
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
    private int _frameCounter;
    private int _resetCounter;
    private readonly List<FikaPlayer> _humanPlayers = [];
    private readonly List<FikaBot> _bots = [];
    private readonly HashSet<FikaBot> _disabledBots = [];
    private BotSpawner _spawner;

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

        _resetCounter = FikaDynamicAI_Plugin.DynamicAIRate.Value switch
        {
            FikaDynamicAI_Plugin.EDynamicAIRates.Low => 600,
            FikaDynamicAI_Plugin.EDynamicAIRates.Medium => 300,
            FikaDynamicAI_Plugin.EDynamicAIRates.High => 120,
            _ => 300,
        };

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

    internal void DestroyComponent()
    {
        FikaDynamicAI_Plugin.DynamicAIRate.SettingChanged -= FikaDynamicAI_Plugin.DynamicAIRate_SettingChanged;
        Instance = null;
        Destroy(this);
    }

    private void Spawner_OnBotRemoved(BotOwner botOwner)
    {
        FikaBot bot = (FikaBot)botOwner.GetPlayer;
        if (!_bots.Remove(bot))
        {
            _logger.LogWarning($"Could not remove {botOwner.gameObject.name} from bots list.");
        }

        if (_disabledBots.Contains(bot))
        {
            _disabledBots.Remove(bot);
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

        _frameCounter++;

        if (_frameCounter % _resetCounter == 0)
        {
            _frameCounter = 0;
            foreach (var bot in _bots)
            {
                CheckForPlayers(bot);
            }
        }
    }

    private bool IsMapEnabled()
    {
        // Get current map ID (lowercase for simpler matching)
        string locationId = Singleton<GameWorld>.Instance.LocationId.ToLower();

        return locationId switch
        {
            "factory4_day" or "factory4_night" => FikaDynamicAI_Plugin.EnableFactory.Value,
            "bigmap" => FikaDynamicAI_Plugin.EnableCustoms.Value,
            "woods" => FikaDynamicAI_Plugin.EnableWoods.Value,
            "shoreline" => FikaDynamicAI_Plugin.EnableShoreline.Value,
            "interchange" => FikaDynamicAI_Plugin.EnableInterchange.Value,
            "rezervbase" => FikaDynamicAI_Plugin.EnableReserve.Value,
            "lighthouse" => FikaDynamicAI_Plugin.EnableLighthouse.Value,
            "tarkovstreets" => FikaDynamicAI_Plugin.EnableStreets.Value,
            "sandbox" => FikaDynamicAI_Plugin.EnableGroundZero.Value,
            "laboratory" => FikaDynamicAI_Plugin.EnableLabs.Value,
            _ => true // Default to enabled for modded/unknown maps
        };
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

    private void CheckForPlayers(FikaBot bot)
    {
        // Do not run on bots that have no initialized yet
        if (bot.AIData.BotOwner.BotState != EBotState.Active)
        {
            return;
        }

        int notInRange = 0;
        float range = GetRangeForCurrentMap();

        foreach (var humanPlayer in _humanPlayers)
        {
            if (humanPlayer == null)
            {
                notInRange++;
                continue;
            }

            if (!humanPlayer.HealthController.IsAlive)
            {
                notInRange++;
                continue;
            }

            float distance = Vector3.SqrMagnitude(bot.Position - humanPlayer.Position);

            if (distance > range * range)
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
    }

    private float GetRangeForCurrentMap()
    {
        string locationId = Singleton<GameWorld>.Instance.LocationId.ToLower();

        return locationId switch
        {
            "factory4_day" or "factory4_night" => FikaDynamicAI_Plugin.RangeFactory.Value,
            "bigmap" => FikaDynamicAI_Plugin.RangeCustoms.Value,
            "woods" => FikaDynamicAI_Plugin.RangeWoods.Value,
            "shoreline" => FikaDynamicAI_Plugin.RangeShoreline.Value,
            "interchange" => FikaDynamicAI_Plugin.RangeInterchange.Value,
            "rezervbase" => FikaDynamicAI_Plugin.RangeReserve.Value,
            "lighthouse" => FikaDynamicAI_Plugin.RangeLighthouse.Value,
            "tarkovstreets" => FikaDynamicAI_Plugin.RangeStreets.Value,
            "sandbox" => FikaDynamicAI_Plugin.RangeGroundZero.Value,
            "laboratory" => FikaDynamicAI_Plugin.RangeLabs.Value,
            _ => FikaDynamicAI_Plugin.DynamicAIRange.Value
        };
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
        _resetCounter = value switch
        {
            FikaDynamicAI_Plugin.EDynamicAIRates.Low => 600,
            FikaDynamicAI_Plugin.EDynamicAIRates.Medium => 300,
            FikaDynamicAI_Plugin.EDynamicAIRates.High => 120,
            _ => 300,
        };
    }
}