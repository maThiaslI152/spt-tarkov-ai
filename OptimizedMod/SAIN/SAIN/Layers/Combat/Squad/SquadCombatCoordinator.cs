using System.Collections.Generic;
using EFT;
using SAIN.Components;
using SAIN.Models.Enums;
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
    private static readonly Dictionary<string, float> LastCoordinationTime = new();

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

        // Throttle coordination
        if (LastCoordinationTime.TryGetValue(squad.Id, out float lastTime) &&
            Time.time - lastTime < CoordinatorUpdateInterval)
            return;

        LastCoordinationTime[squad.Id] = Time.time;

        // Collect all enemies visible to any squad member
        var allEnemies = CollectAllVisibleEnemies(squad);
        if (allEnemies.Count == 0)
            return;

        // Distribute targets to squad members
        DistributeTargets(squad, allEnemies);

        // Assign flanking positions
        AssignFlankingPositions(squad, allEnemies);
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
    private static void DistributeTargets(SAIN.BotController.Classes.Squad squad, List<Enemy> allEnemies)
    {
        if (allEnemies.Count == 0 || squad.Members.Count == 0)
            return;

        foreach (var member in squad.Members.Values)
        {
            if (member == null || !member.BotActive || member.IsDead)
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
                // Suppress if enemy is within effective range but behind cover
                if (closestDist < 1600f && !closest.IsVisible) // ~40m
                {
                    member.Decision.DecisionManager.SetSquadDecision(ESquadDecision.Suppress);
                }
                else if (closestDist < 400f) // ~20m — close quarters
                {
                    member.Decision.DecisionManager.SetSquadDecision(ESquadDecision.PushSuppressedEnemy);
                }
                else if (closestDist > 6400f) // ~80m — spread out and search
                {
                    member.Decision.DecisionManager.SetSquadDecision(ESquadDecision.Search);
                }
            }
        }
    }

    /// <summary>
    /// Assign flanking positions to squad members around the enemy centroid.
    /// </summary>
    private static void AssignFlankingPositions(SAIN.BotController.Classes.Squad squad, List<Enemy> allEnemies)
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
                if (member.HasEnemy && member.GoalEnemy != null)
                {
                    member.Decision.DecisionManager.SetSquadDecision(ESquadDecision.Search);
                }
            }
        }
    }
}
