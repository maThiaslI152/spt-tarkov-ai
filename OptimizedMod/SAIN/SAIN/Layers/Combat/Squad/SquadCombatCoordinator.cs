using System.Collections.Generic;
using EFT;
using LootingBots;
using SAIN.Components;
using SAIN.Interop;
using SAIN.Models.Enums;
using SAIN.Plugin;
using SAIN.Preset.GlobalSettings;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace SAIN.Layers.Combat.Squad;

/// <summary>
/// Phase 3.2: Squad Combat Coordinator — runs once per squad (on the leader)
/// and distributes target assignments, flanking directions, and suppression orders
/// to individual squad members. This replaces per-bot independent combat evaluation
/// with centralized coordination, following the Bannerlord/CoD pattern.
/// </summary>
public static class SquadCombatCoordinator
{
    private const float CoordinatorUpdateInterval = 0.5f; // Run coordination every 500ms
    private static readonly Dictionary<string, SquadCoordState> SquadStates = new();

    /// <summary>Clears per-squad throttle state — call on raid / GameWorld teardown so IDs and memory do not leak across sessions.</summary>
    public static void ResetCoordinationThrottle()
    {
        SquadStates.Clear();
    }

    /// <summary>
    /// Coordinate squad combat: distribute targets, assign flanks, coordinate suppression.
    /// Called by the squad leader's CombatSquadLayer each frame (throttled internally).
    /// </summary>
    public static void CoordinateSquad(BotComponent leader, SAIN.SAINComponent.Classes.Decision.SAINDecisionClass decision)
    {
        if (leader == null)
            return;

        var squad = leader.Squad.SquadInfo;
        if (squad == null || squad.Members.Count <= 1)
            return;

        var state = GetState(squad.Id);

        // Throttle coordination
        if (Time.time - state.LastCoordinationTime < CoordinatorUpdateInterval)
            return;
        state.LastCoordinationTime = Time.time;

        RogueBaseDefenseSettings settings = SAINPlugin.LoadedPreset?.GlobalSettings?.General?.RogueBaseDefense;
        bool rogueDefense = IsRogueDefenseContext(leader, settings);

        if (state.OrderExpireTime > 0f && Time.time >= state.OrderExpireTime)
        {
            ClearSquadOrders(squad, state, "ttl-expired");
        }

        // Collect all enemies visible to any squad member
        var allEnemies = CollectAllVisibleEnemies(squad);

        if (rogueDefense)
        {
            BotComponent electedLeader = ElectRogueLeader(squad, state, settings, allEnemies);
            if (electedLeader == null)
            {
                return;
            }

            if (state.LeaderProfileId != electedLeader.ProfileId)
            {
                ClearSquadOrders(squad, state, "leader-changed");
                state.LeaderProfileId = electedLeader.ProfileId;
                state.LeaderHoldUntil = Time.time + settings.RogueLeaderHoldSeconds;
                LogDiag($"[SAIN DIAG][RogueCoord] Squad={squad.Id} elected leader={electedLeader.name}");
            }

            if (settings.DisableRogueLootingOnBase)
            {
                SuppressRogueLooting(squad, state, settings);
            }

            if (allEnemies.Count == 0)
            {
                ApplyRogueDefenseOrders(squad, electedLeader, state, settings);
                return;
            }

            // Distribute targets and flanking around elected leader
            DistributeTargets(squad, allEnemies, electedLeader, state, settings);
            AssignFlankingPositions(squad, allEnemies, electedLeader, state, settings);
            return;
        }

        if (allEnemies.Count == 0)
            return;

        // Distribute targets to squad members
        DistributeTargets(squad, allEnemies, leader, state, null);

        // Assign flanking positions
        AssignFlankingPositions(squad, allEnemies, leader, state, null);
    }

    /// <summary>
    /// Collect all enemies currently visible to any squad member.
    /// </summary>
    private static List<Enemy> CollectAllVisibleEnemies(SAIN.BotController.Classes.Squad squad)
    {
        var allEnemies = new List<Enemy>();
        var seenProfiles = new HashSet<string>();

        foreach (var member in squad.Members.Values)
        {
            if (member == null || !member.BotActive)
                continue;

            foreach (var enemy in member.EnemyController.VisibleEnemies)
            {
                if (enemy != null && seenProfiles.Add(enemy.EnemyProfileId))
                {
                    allEnemies.Add(enemy);
                }
            }
        }

        return allEnemies;
    }

    /// <summary>
    /// Distribute targets across squad members to avoid everyone shooting the same target.
    /// Each member gets assigned a primary target based on proximity and threat.
    /// </summary>
    private static void DistributeTargets(
        SAIN.BotController.Classes.Squad squad,
        List<Enemy> allEnemies,
        BotComponent electedLeader,
        SquadCoordState state,
        RogueBaseDefenseSettings settings
    )
    {
        if (allEnemies.Count == 0 || squad.Members.Count == 0)
            return;

        foreach (var member in squad.Members.Values)
        {
            if (member == null || !member.BotActive || member.IsDead)
                continue;

            // Do not call SetSquadDecision — it clears solo combat to None and lets squad layer outrank solo.
            // Preserve EnemyDecisions output while this bot already has an active combat decision.
            if (member.Decision.CurrentCombatDecision != ECombatDecision.None)
                continue;

            // Find the closest enemy to this member
            Enemy closest = null;
            float closestDist = float.MaxValue;

            foreach (var enemy in allEnemies)
            {
                float dist = (member.Position - enemy.EnemyPosition).sqrMagnitude;
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = enemy;
                }
            }

            // Set the squad decision based on distributed target
            if (closest != null)
            {
                ESquadDecision squadDecision = ESquadDecision.None;
                // Suppress if enemy is within effective range but behind cover
                if (closestDist < 1600f && !closest.IsVisible) // ~40m
                {
                    squadDecision = ESquadDecision.Suppress;
                }
                else if (closestDist < 400f) // ~20m — close quarters
                {
                    squadDecision = ESquadDecision.PushSuppressedEnemy;
                }
                else if (closestDist > 6400f) // ~80m — spread out and search
                {
                    squadDecision = ESquadDecision.Search;
                }
                else
                {
                    squadDecision = ESquadDecision.Suppress;
                }

                if (squadDecision != ESquadDecision.None)
                {
                    member.Decision.DecisionManager.SetSquadDecision(squadDecision);
                    state.LastOrder = squadDecision;
                    state.OrderExpireTime = Time.time + (settings?.RogueOrderTtlSeconds ?? 4f);
                }
            }
        }
    }

    /// <summary>
    /// Assign flanking positions to squad members around the enemy centroid.
    /// </summary>
    private static void AssignFlankingPositions(
        SAIN.BotController.Classes.Squad squad,
        List<Enemy> allEnemies,
        BotComponent electedLeader,
        SquadCoordState state,
        RogueBaseDefenseSettings settings
    )
    {
        if (allEnemies.Count == 0 || squad.Members.Count < 2)
            return;

        // Calculate enemy centroid
        Vector3 enemyCentroid = Vector3.zero;
        foreach (var enemy in allEnemies)
        {
            enemyCentroid += enemy.EnemyPosition;
        }
        enemyCentroid /= allEnemies.Count;

        // Sort members by distance to enemy centroid
        var sortedMembers = new List<BotComponent>();
        foreach (var member in squad.Members.Values)
        {
            if (member != null && member.BotActive && !member.IsDead)
                sortedMembers.Add(member);
        }
        sortedMembers.Sort((a, b) =>
            (a.Position - enemyCentroid).sqrMagnitude.CompareTo(
                (b.Position - enemyCentroid).sqrMagnitude));

        // Assign flanking directions: closest approaches directly, others flank at angles
        // This is a simplified version — each member gets a different engagement angle
        if (sortedMembers.Count >= 2)
        {
            // The closest member engages directly
            // Others spread out for flanking (handled via search/regroup decisions)
            for (int i = 1; i < sortedMembers.Count; i++)
            {
                var member = sortedMembers[i];
                if (member.Decision.CurrentCombatDecision != ECombatDecision.None)
                    continue;
                if (member.HasEnemy && member.GoalEnemy != null)
                {
                    member.Decision.DecisionManager.SetSquadDecision(ESquadDecision.Search);
                    state.LastOrder = ESquadDecision.Search;
                    state.OrderExpireTime = Time.time + (settings?.RogueOrderTtlSeconds ?? 4f);
                }
            }
        }
    }

    private static void ApplyRogueDefenseOrders(
        SAIN.BotController.Classes.Squad squad,
        BotComponent electedLeader,
        SquadCoordState state,
        RogueBaseDefenseSettings settings
    )
    {
        ESquadDecision leaderOrder = ESquadDecision.Search; // Patrol
        foreach (var member in squad.Members.Values)
        {
            if (!IsRogueMemberEligible(member))
            {
                continue;
            }
            if (member.Decision.CurrentCombatDecision != ECombatDecision.None)
            {
                continue;
            }

            ESquadDecision order;
            if (member.ProfileId == electedLeader.ProfileId)
            {
                order = leaderOrder;
            }
            else
            {
                float distFromLeader = (member.Position - electedLeader.Position).sqrMagnitude;
                order = distFromLeader > 20f * 20f ? ESquadDecision.Regroup : ESquadDecision.Search;
            }

            member.Decision.DecisionManager.SetSquadDecision(order);
            state.LastOrder = order;
            state.OrderExpireTime = Time.time + settings.RogueOrderTtlSeconds;
        }
    }

    private static BotComponent ElectRogueLeader(
        SAIN.BotController.Classes.Squad squad,
        SquadCoordState state,
        RogueBaseDefenseSettings settings,
        List<Enemy> allEnemies
    )
    {
        if (state.LeaderProfileId != null
            && Time.time < state.LeaderHoldUntil
            && squad.Members.TryGetValue(state.LeaderProfileId, out BotComponent heldLeader)
            && IsRogueMemberEligible(heldLeader))
        {
            return heldLeader;
        }

        Vector3 squadCenter = Vector3.zero;
        int count = 0;
        foreach (BotComponent member in squad.Members.Values)
        {
            if (!IsRogueMemberEligible(member))
            {
                continue;
            }
            squadCenter += member.Position;
            count++;
        }
        if (count == 0)
        {
            return null;
        }
        squadCenter /= count;

        BotComponent best = null;
        float bestScore = float.MinValue;
        foreach (BotComponent member in squad.Members.Values)
        {
            if (!IsRogueMemberEligible(member))
            {
                continue;
            }

            float score = ScoreRogueLeader(member, squadCenter);
            if (score > bestScore
                || (Mathf.Abs(score - bestScore) < 0.001f
                    && string.CompareOrdinal(member.ProfileId, best?.ProfileId) < 0))
            {
                bestScore = score;
                best = member;
            }
        }

        return best;
    }

    private static float ScoreRogueLeader(BotComponent member, Vector3 squadCenter)
    {
        float score = 0f;
        if (member.EnemyController.HumanEnemyInLineofSight)
        {
            score += 10f;
        }
        if (member.EnemyController.ActiveHumanEnemy)
        {
            score += 5f;
        }
        if (member.GoalEnemy != null)
        {
            score += 4f;
        }
        if (member.Decision.CurrentSelfDecision == ESelfActionType.None)
        {
            score += 2f;
        }

        float centerDist = (member.Position - squadCenter).sqrMagnitude;
        score += Mathf.Clamp01(1f - centerDist / (40f * 40f)) * 3f;
        return score;
    }

    private static bool IsRogueDefenseContext(BotComponent leader, RogueBaseDefenseSettings settings)
    {
        if (settings == null || !settings.EnableRogueBaseDefensePolicy)
        {
            return false;
        }
        if (leader?.Info?.Profile?.WildSpawnType != WildSpawnType.exUsec)
        {
            return false;
        }
        if (!settings.OnlyOnLighthouse)
        {
            return true;
        }

        string locationId = GameWorldComponent.Instance?.GameWorld?.LocationId;
        return !string.IsNullOrEmpty(locationId)
            && locationId.IndexOf("lighthouse", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsRogueMemberEligible(BotComponent member)
    {
        return member != null
            && member.BotActive
            && !member.IsDead
            && member.Info?.Profile?.WildSpawnType == WildSpawnType.exUsec;
    }

    private static SquadCoordState GetState(string squadId)
    {
        if (!SquadStates.TryGetValue(squadId, out SquadCoordState state))
        {
            state = new SquadCoordState();
            SquadStates[squadId] = state;
        }
        return state;
    }

    private static void SuppressRogueLooting(
        SAIN.BotController.Classes.Squad squad,
        SquadCoordState state,
        RogueBaseDefenseSettings settings
    )
    {
        if (!ModDetection.LootingBotsLoaded)
        {
            return;
        }
        if (Time.time - state.LastLootSuppressTime < 2f)
        {
            return;
        }
        state.LastLootSuppressTime = Time.time;

        foreach (BotComponent member in squad.Members.Values)
        {
            if (!IsRogueMemberEligible(member))
            {
                continue;
            }
            LootingBotsInterop.TryPreventBotFromLooting(member.BotOwner, settings.RogueOrderTtlSeconds);
        }
    }

    private static void ClearSquadOrders(SAIN.BotController.Classes.Squad squad, SquadCoordState state, string reason)
    {
        foreach (var member in squad.Members.Values)
        {
            if (member == null || member.IsDead || !member.BotActive)
            {
                continue;
            }
            member.Decision.DecisionManager.SetSquadDecision(ESquadDecision.None);
        }

        state.LastOrder = ESquadDecision.None;
        state.OrderExpireTime = 0f;
        LogDiag($"[SAIN DIAG][RogueCoord] Squad={squad.Id} cleared orders ({reason})");
    }

    private static void LogDiag(string message)
    {
        if (SainPerfLogInterop.IsDiagnosticLoggingEnabled)
        {
            Logger.LogInfo(message);
        }
    }

    private sealed class SquadCoordState
    {
        public float LastCoordinationTime;
        public string LeaderProfileId;
        public float LeaderHoldUntil;
        public ESquadDecision LastOrder = ESquadDecision.None;
        public float OrderExpireTime;
        public float LastLootSuppressTime;
    }
}
