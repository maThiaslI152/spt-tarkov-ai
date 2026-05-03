using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using SAIN.Components.PlayerComponentSpace;
using SAIN.Models.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace SAIN.Components;

/// <summary>
/// Registers <see cref="OfflineSquad"/> instances with <see cref="AIFrameBudgetScheduler"/> from live raid state:
/// occluded bots that are engaged with another AI at moderate range (SMART-style distant combat seed).
/// </summary>
internal static class OfflineSquadWorldSync
{
    private const float SyncIntervalSeconds = 5f;
    private const float MaxEngagementDistance = 400f;
    private const float MinHumanDistanceSq = 70f * 70f;
    private const string AutoSquadPrefix = "auto_";

    private static float _nextSyncTime;
    private static readonly List<string> _registeredAutoSquadIds = new();

    /// <summary>Clears auto-squad bookkeeping when a new <see cref="BotManagerComponent"/> activates.</summary>
    public static void ResetForNewRaid()
    {
        _registeredAutoSquadIds.Clear();
        _nextSyncTime = 0f;
    }

    public static void TrySync(BotManagerComponent manager, float currentTime)
    {
        if (manager?.BudgetScheduler == null || currentTime < _nextSyncTime)
        {
            return;
        }

        _nextSyncTime = currentTime + SyncIntervalSeconds;

        foreach (string id in _registeredAutoSquadIds)
        {
            manager.BudgetScheduler.UnregisterOfflineSquad(id);
        }

        _registeredAutoSquadIds.Clear();

        HashSet<BotComponent> sainBots = manager.BotSpawnController?.SAINBots;
        PlayerSpawnTracker tracker = manager.SAINGameWorld?.PlayerTracker;
        if (sainBots == null || sainBots.Count == 0 || tracker?.AlivePlayerArray == null)
        {
            return;
        }

        var aliveHumans = tracker.AlivePlayerArray
            .Where(p => p != null && !p.IsAI && p.Player?.HealthController?.IsAlive == true)
            .ToList();

        if (aliveHumans.Count == 0)
        {
            return;
        }

        var profileToBot = new Dictionary<string, BotComponent>(StringComparer.Ordinal);
        foreach (BotComponent bot in sainBots)
        {
            if (bot == null || bot.IsDead || !bot.BotActive || string.IsNullOrEmpty(bot.ProfileId))
            {
                continue;
            }

            profileToBot[bot.ProfileId] = bot;
        }

        var candidates = new List<BotComponent>();
        foreach (BotComponent bot in sainBots)
        {
            if (bot == null || bot.IsDead || !bot.BotActive)
            {
                continue;
            }

            if (bot.CurrentPerceptionTier != PerceptionTier.Occluded)
            {
                continue;
            }

            if (!IsFarFromHumans(bot.BotOwner.Position, aliveHumans))
            {
                continue;
            }

            candidates.Add(bot);
        }

        if (candidates.Count < 2)
        {
            return;
        }

        (BotComponent a, BotComponent b, float dist) best = (null, null, float.MaxValue);

        foreach (BotComponent bot in candidates)
        {
            Enemy goal = bot.GoalEnemy;
            if (goal == null || !goal.IsAI || !Enemy.IsEnemyActive(goal))
            {
                continue;
            }

            if (!profileToBot.TryGetValue(goal.EnemyProfileId, out BotComponent other) || other == bot)
            {
                continue;
            }

            if (other.IsDead || !other.BotActive || other.CurrentPerceptionTier != PerceptionTier.Occluded)
            {
                continue;
            }

            if (!IsFarFromHumans(other.BotOwner.Position, aliveHumans))
            {
                continue;
            }

            float d = Vector3.Distance(bot.Position, other.Position);
            if (d > MaxEngagementDistance || d >= best.dist)
            {
                continue;
            }

            best = (bot, other, d);
        }

        if (best.a == null || best.b == null)
        {
            return;
        }

        OfflineSquad squadA = BuildSquadFromBotsGroup(best.a, sainBots);
        OfflineSquad squadB = BuildSquadFromBotsGroup(best.b, sainBots);

        if (squadA?.Members.Count > 0 && squadB?.Members.Count > 0
            && HostileGroups(squadA, squadB, best.a, best.b))
        {
            manager.BudgetScheduler.RegisterOfflineSquad(squadA);
            manager.BudgetScheduler.RegisterOfflineSquad(squadB);
            _registeredAutoSquadIds.Add(squadA.SquadId);
            _registeredAutoSquadIds.Add(squadB.SquadId);
        }
    }

    private static bool HostileGroups(OfflineSquad _, OfflineSquad __, BotComponent a, BotComponent b)
    {
        BotsGroup ga = a.BotOwner?.BotsGroup;
        BotsGroup gb = b.BotOwner?.BotsGroup;
        if (ga == null || gb == null)
        {
            return true;
        }

        if (ReferenceEquals(ga, gb))
        {
            return false;
        }

        try
        {
            if (ga.IsPlayerEnemy(b.Player))
            {
                return true;
            }

            if (gb.IsPlayerEnemy(a.Player))
            {
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool IsFarFromHumans(Vector3 pos, List<PlayerComponent> humans)
    {
        for (int i = 0; i < humans.Count; i++)
        {
            Player p = humans[i]?.Player;
            if (p == null)
            {
                continue;
            }

            if ((p.Position - pos).sqrMagnitude < MinHumanDistanceSq)
            {
                return false;
            }
        }

        return true;
    }

    private static OfflineSquad BuildSquadFromBotsGroup(BotComponent anchor, IEnumerable<BotComponent> allBots)
    {
        BotsGroup group = anchor.BotOwner?.BotsGroup;
        if (group == null)
        {
            return null;
        }

        var members = new List<BotCombatStats>();
        Vector3 sum = Vector3.zero;
        int n = 0;
        string minProfileId = null;

        foreach (BotComponent bot in allBots)
        {
            if (bot == null || bot.IsDead || !bot.BotActive)
            {
                continue;
            }

            if (!ReferenceEquals(bot.BotOwner.BotsGroup, group))
            {
                continue;
            }

            if (bot.CurrentPerceptionTier != PerceptionTier.Occluded)
            {
                continue;
            }

            members.Add(CreateCombatStats(bot));
            sum += bot.Position;
            n++;
            string pid = bot.ProfileId;
            if (minProfileId == null || string.Compare(pid, minProfileId, StringComparison.Ordinal) < 0)
            {
                minProfileId = pid;
            }
        }

        if (n == 0 || string.IsNullOrEmpty(minProfileId))
        {
            return null;
        }

        return new OfflineSquad
        {
            SquadId = AutoSquadPrefix + minProfileId,
            Faction = anchor.Info?.Profile?.WildSpawnType.ToString() ?? "unknown",
            CenterPosition = sum / n,
            Members = members,
            IsHostileToOtherFaction = true,
        };
    }

    private static BotCombatStats CreateCombatStats(BotComponent bot)
    {
        var stats = new BotCombatStats
        {
            BotId = bot.ProfileId,
            BotType = bot.Info?.Profile?.WildSpawnType.ToString() ?? "Unknown",
            Level = bot.Info?.Profile?.PlayerLevel ?? 1,
            BasePower = Mathf.Max(1f, (bot.BotOwner?.AIData.PowerOfEquipment ?? 50f) / 25f),
            WeaponDamageOutput = 55f,
            WeaponTemplateId = string.Empty,
            ArmorClass = 3f,
            HealthPercent = 1f,
        };

        Weapon weapon = bot.PlayerComponent?.CurrentWeapon;
        if (weapon != null)
        {
            stats.WeaponTemplateId = weapon.TemplateId != null ? weapon.TemplateId.ToString() : string.Empty;
            if (weapon.CurrentAmmoTemplate != null)
            {
                stats.WeaponDamageOutput = weapon.CurrentAmmoTemplate.Damage;
            }
        }

        stats.HealthPercent = EstimateAverageHealthNormalized(bot.Player);
        stats.ArmorClass = 3f;

        return stats;
    }

    private static float EstimateAverageHealthNormalized(Player player)
    {
        if (player?.HealthController == null || !player.HealthController.IsAlive)
        {
            return 0f;
        }

        IHealthController hc = player.HealthController;
        float sum = 0f;
        int c = 0;
        foreach (EBodyPart part in new[] { EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach })
        {
            sum += hc.GetBodyPartHealth(part, false).Normalized;
            c++;
        }

        return c > 0 ? Mathf.Clamp01(sum / c) : 1f;
    }

}
