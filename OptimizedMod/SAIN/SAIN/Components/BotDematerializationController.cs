using System;
using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace SAIN.Components;

/// <summary>
/// Phase 1 seam for SMART dematerialize / rematerialize. Phase 2 (AILimit) will call
/// <see cref="RequestDematerialize"/> / <see cref="RequestRematerialize"/> instead of toggling GameObjects directly.
/// </summary>
public sealed class BotDematerializationController
{
    private readonly BotManagerComponent _manager;
    private readonly Dictionary<string, DematerializedEntry> _byProfileId = new(StringComparer.Ordinal);

    private sealed class DematerializedEntry
    {
        public string SquadId;
        public string BotTypeKey;
        public bool Pooled;
    }

    public BotDematerializationController(BotManagerComponent manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
    }

    public void ResetForNewRaid()
    {
        _byProfileId.Clear();
    }

    public bool IsDematerialized(string profileId)
    {
        return !string.IsNullOrEmpty(profileId) && _byProfileId.ContainsKey(profileId);
    }

    /// <summary>
    /// Park one bot: register a single-member <see cref="OfflineSquad"/> for statistical combat,
    /// enqueue the GameObject in <see cref="BotGameObjectPool"/>, and track profile for rematerialize.
    /// </summary>
    public bool RequestDematerialize(BotComponent bot, string reason)
    {
        _ = reason;
        if (bot == null || bot.IsDead || string.IsNullOrEmpty(bot.ProfileId))
        {
            return false;
        }

        if (_byProfileId.ContainsKey(bot.ProfileId))
        {
            return false;
        }

        AIFrameBudgetScheduler scheduler = _manager.BudgetScheduler;
        if (scheduler == null)
        {
            return false;
        }

        OfflineSquad squad = OfflineSquadWorldSync.BuildDematerializeSquadForBot(bot);
        if (squad == null || squad.Members.Count == 0)
        {
            return false;
        }

        scheduler.UnregisterOfflineSquad(squad.SquadId);
        scheduler.RegisterOfflineSquad(squad);

        string botType = bot.Info?.Profile?.WildSpawnType.ToString() ?? "Assault";
        GameObject go = bot.BotOwner.gameObject;
        BotGameObjectPool pool = _manager.Pool;
        if (pool == null || !pool.ReturnToPool(botType, go))
        {
            scheduler.UnregisterOfflineSquad(squad.SquadId);
            return false;
        }

        BotOwner owner = bot.BotOwner;
        if (owner != null && owner.BotState == EBotState.Active)
        {
            owner.BotState = EBotState.NonActive;
        }

        _byProfileId[bot.ProfileId] = new DematerializedEntry
        {
            SquadId = squad.SquadId,
            BotTypeKey = botType,
            Pooled = true,
        };

        return true;
    }

    /// <summary>
    /// Restore a bot parked by <see cref="RequestDematerialize"/>: unregister offline squad, activate GameObject / bot state, re-sync SAIN activation.
    /// </summary>
    public bool RequestRematerialize(BotComponent bot)
    {
        if (bot == null || string.IsNullOrEmpty(bot.ProfileId))
        {
            return false;
        }

        if (!_byProfileId.TryGetValue(bot.ProfileId, out DematerializedEntry entry))
        {
            return false;
        }

        AIFrameBudgetScheduler scheduler = _manager.BudgetScheduler;
        scheduler?.UnregisterOfflineSquad(entry.SquadId);

        BotOwner owner = bot.BotOwner;
        GameObject go = null;
        if (owner != null)
        {
            go = owner.gameObject;
            if (entry.Pooled && _manager.Pool != null)
            {
                _manager.Pool.TryRemoveFromPool(go);
            }

            if (!go.activeSelf)
            {
                go.SetActive(true);
            }

            if (owner.BotState != EBotState.Active)
            {
                owner.BotState = EBotState.Active;
            }

            BotStandBy standBy = owner.StandBy;
            if (standBy != null)
            {
                standBy.Activate();
                standBy.NextCheckTime = Time.time + 10f;
            }
        }

        if (bot.PlayerComponent != null)
        {
            bot.PlayerComponent.ActivationClass.CheckActive(bot.PlayerComponent);
        }

        bot.RecheckActivation();
        _byProfileId.Remove(bot.ProfileId);

        if (_manager.Pool != null && go != null)
        {
            _manager.Pool.RegisterActiveBot(go);
        }

        return true;
    }
}
