using System;
using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace SAIN.Components;

/// <summary>
/// SMART dematerialize / rematerialize: pool GameObjects, register <c>demat_*</c> offline rows,
/// and arbitrate multiple park reasons (AILimit + SMART LOS gate).
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
        public DematParkReason Holders;
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

    private static DematParkReason ReasonFromString(string reason)
    {
        if (string.IsNullOrEmpty(reason))
        {
            return DematParkReason.SmartLos;
        }

        if (reason.IndexOf("ailimit", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return DematParkReason.Ailimit;
        }

        return DematParkReason.SmartLos;
    }

    /// <summary>
    /// Park one bot: register a single-member <see cref="OfflineSquad"/> for statistical combat,
    /// enqueue the GameObject in <see cref="BotGameObjectPool"/>, and track profile for rematerialize.
    /// If already parked, merges <paramref name="reason"/> into <see cref="DematParkReason"/> without re-pooling.
    /// </summary>
    public bool RequestDematerialize(BotComponent bot, string reason)
    {
        if (bot == null || bot.IsDead || string.IsNullOrEmpty(bot.ProfileId))
        {
            return false;
        }

        DematParkReason flag = ReasonFromString(reason);

        if (_byProfileId.TryGetValue(bot.ProfileId, out DematerializedEntry existing))
        {
            existing.Holders |= flag;
            return true;
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
            Holders = flag,
        };

        return true;
    }

    /// <summary>Release one park holder. Full rematerialize only when no holders remain.</summary>
    public DematReleaseResult TryReleaseParkReason(BotComponent bot, DematParkReason toClear)
    {
        if (bot == null || string.IsNullOrEmpty(bot.ProfileId))
        {
            return DematReleaseResult.NotTracked;
        }

        if (!_byProfileId.TryGetValue(bot.ProfileId, out DematerializedEntry entry))
        {
            return DematReleaseResult.NotTracked;
        }

        entry.Holders &= ~toClear;
        if (entry.Holders != DematParkReason.None)
        {
            return DematReleaseResult.PartialStillHeld;
        }

        PerformFullRematerialize(entry, bot);
        return DematReleaseResult.FullyRematerialized;
    }

    /// <summary>Force full rematerialize (e.g. AILimit plugin disabled path).</summary>
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

        entry.Holders = DematParkReason.None;
        PerformFullRematerialize(entry, bot);
        return true;
    }

    private void PerformFullRematerialize(DematerializedEntry entry, BotComponent bot)
    {
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

        SainDematPolicy.NotifyFullRematerialized(bot.ProfileId);
    }
}
