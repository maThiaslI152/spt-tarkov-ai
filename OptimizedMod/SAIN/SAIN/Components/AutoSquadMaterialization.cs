using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using SAIN;
using EFT;
using EFT.HealthSystem;
using SAIN.Components.BotController;
using UnityEngine;

namespace SAIN.Components;

/// <summary>
/// Phase 2: when the main player nears an <c>auto_*</c> offline squad that has pending statistical casualties,
/// apply kills to matching <strong>live</strong> raid bots (no new spawn / BotCreationData — those bots already exist).
/// </summary>
public static class AutoSquadMaterialization
{
    private const float NearAutoSquadMeters = 120f;
    private static float _nextTickTime;

    public static void TryTick(BotManagerComponent manager, float currentTime, float deltaTime)
    {
        _ = deltaTime;
        if (manager?.BudgetScheduler == null || manager.BotSpawnController == null || currentTime < _nextTickTime)
        {
            return;
        }

        _nextTickTime = currentTime + 1f;

        GameWorld gw = Singleton<GameWorld>.Instance;
        if (gw?.MainPlayer == null)
        {
            return;
        }

        Vector3 mp = gw.MainPlayer.Position;
        float nearSq = NearAutoSquadMeters * NearAutoSquadMeters;
        int maxBots = GetMaxBotsBestEffort();

        foreach (OfflineSquad squad in manager.BudgetScheduler.OfflineSquads.ToList())
        {
            if (squad?.SquadId == null || !squad.SquadId.StartsWith("auto_", StringComparison.Ordinal))
            {
                continue;
            }

            if (squad.PendingWorldCasualties <= 0)
            {
                continue;
            }

            if ((squad.CenterPosition - mp).sqrMagnitude > nearSq)
            {
                continue;
            }

            SmartDematTelemetry.AutoSpawnAttempts++;
            int aiAlive = gw.AllAlivePlayersList.Count(p => p != null && p.IsAI);
            if (maxBots > 0 && aiAlive >= maxBots)
            {
                SmartDematTelemetry.AutoSpawnFailures++;
                continue;
            }

            int pending = squad.PendingWorldCasualties;
            int applied = ApplyCasualtiesToLiveBots(manager.BotSpawnController, squad, pending);
            if (applied > 0)
            {
                squad.PendingWorldCasualties = Mathf.Max(0, pending - applied);
                SmartDematTelemetry.AutoMatApplied++;
            }
            else if (CountAliveEligibleTargets(manager.BotSpawnController, squad) == 0)
            {
                squad.PendingWorldCasualties = 0;
            }
            else
            {
                SmartDematTelemetry.AutoSpawnFailures++;
                squad.PendingWorldCasualties = 0;
            }
        }
    }

    private static int CountAliveEligibleTargets(BotSpawnController spawn, OfflineSquad squad)
    {
        if (spawn == null || squad.Members == null)
        {
            return 0;
        }

        int n = 0;
        foreach (BotCombatStats m in squad.Members)
        {
            if (string.IsNullOrEmpty(m.BotId))
            {
                continue;
            }

            BotComponent bot = spawn.GetSAIN(m.BotId);
            if (bot == null || bot.IsDead || !bot.BotActive)
            {
                continue;
            }

            WildSpawnType wt = bot.Info?.Profile?.WildSpawnType ?? WildSpawnType.assault;
            if (BotSpawnController.IsWildSpawnStrictlyExcluded(wt))
            {
                continue;
            }

            n++;
        }

        return n;
    }

    private static int ApplyCasualtiesToLiveBots(BotSpawnController spawn, OfflineSquad squad, int pending)
    {
        if (spawn == null || pending <= 0 || squad.Members == null || squad.Members.Count == 0)
        {
            return 0;
        }

        var profileIds = squad.Members
            .Select(m => m.BotId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToList();

        for (int i = profileIds.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (profileIds[i], profileIds[j]) = (profileIds[j], profileIds[i]);
        }

        int applied = 0;
        foreach (string profileId in profileIds)
        {
            if (applied >= pending)
            {
                break;
            }

            BotComponent bot = spawn.GetSAIN(profileId);
            if (bot == null || bot.IsDead || !bot.BotActive)
            {
                continue;
            }

            WildSpawnType wt = bot.Info?.Profile?.WildSpawnType ?? WildSpawnType.assault;
            if (BotSpawnController.IsWildSpawnStrictlyExcluded(wt))
            {
                continue;
            }

            if (TryOfflineStatKill(bot))
            {
                applied++;
            }
        }

        return applied;
    }

    private static bool TryOfflineStatKill(BotComponent bot)
    {
        if (bot?.BotOwner == null || bot.IsDead)
        {
            return false;
        }

        try
        {
            Player player = bot.BotOwner.GetPlayer;
            if (player?.HealthController is ActiveHealthController hc)
            {
                hc.IsAlive = false;
                return true;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[SAIN] Auto offline casualty apply failed for {bot.ProfileId}: {ex.Message}");
        }

        return false;
    }

    private static int GetMaxBotsBestEffort()
    {
        try
        {
            if (!Singleton<IBotGame>.Instantiated)
            {
                return 0;
            }

            return Singleton<IBotGame>.Instance.BotsController?.BotSpawner?.MaxBots ?? 0;
        }
        catch
        {
            return 0;
        }
    }
}
