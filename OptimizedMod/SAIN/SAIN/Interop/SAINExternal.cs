using System.Collections.Generic;
using EFT;
using SAIN.Components;
using SAIN.Plugin;
using SAIN.SAINComponent.Classes;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;
using UnityEngine.AI;

namespace SAIN.Interop;

/// <summary>
/// A class containing various static methods that can be called with reflection
/// </summary>
public static class SAINExternal
{
    /// <summary>Minimum squared length for direction vectors used in quest-vs-threat dot checks (avoids NaN from Normalize).</summary>
    private const float QuestDirectionMinSqr = 0.04f; // 20cm

    private const float QuestAlignSeenSec = 10f;
    private const float QuestAlignHeardSec = 5f;
    public static bool IgnoreHearing(BotOwner bot, bool value, bool ignoreUnderFire, float duration)
    {
        var component = GetBotComponent(bot);
        if (component == null)
        {
            return false;
        }

        bool result = component.Hearing.SoundInput.SetIgnoreHearingExternal(value, ignoreUnderFire, duration, out string reason);
        return result;
    }

    public static string GetPersonality(BotOwner bot)
    {
        var component = GetBotComponent(bot);
        if (component == null)
        {
            return string.Empty;
        }
        return component.Info.Personality.ToString();
    }

    private static BotComponent GetBotComponent(BotOwner bot)
    {
        if (BotManagerComponent.Instance?.GetSAIN(bot, out BotComponent botComponent) == true)
        {
            return botComponent;
        }
        return bot.GetComponent<BotComponent>();
    }

    public static bool ExtractBot(BotOwner bot)
    {
        var component = GetBotComponent(bot);
        if (component == null)
        {
            return false;
        }

        component.Info.ForceExtract = true;

        return true;
    }

    public static void GetExtractedBots(List<string> list)
    {
        var botController = BotManagerComponent.Instance;
        if (botController == null)
        {
#if DEBUG
            Logger.LogWarning("SAIN Bot Controller is Null, cannot retrieve Extracted Bots List.");
#endif
            return;
        }
        var extractedBots = botController.BotExtractManager?.ExtractedBots;
        if (extractedBots == null)
        {
#if DEBUG
            Logger.LogWarning("List of extracted bots is null! Cannot copy list.");
#endif
            return;
        }
        list.Clear();
        list.AddRange(extractedBots);
    }

    public static void GetExtractionInfos(List<ExtractionInfo> list)
    {
        var botController = BotManagerComponent.Instance;
        if (botController == null)
        {
#if DEBUG
            Logger.LogWarning("SAIN Bot Controller is Null, cannot retrieve Extracted Bots List.");
#endif
            return;
        }
        var extractedBots = botController.BotExtractManager?.BotExtractionInfos;
        if (extractedBots == null)
        {
#if DEBUG
            Logger.LogWarning("List of extracted bots is null! Cannot copy list.");
#endif
            return;
        }
        list.Clear();
        list.AddRange(extractedBots);
    }

    public static bool TrySetExfilForBot(BotOwner bot)
    {
        var component = GetBotComponent(bot);
        if (component == null)
        {
            return false;
        }

#if DEBUG
        if (!Components.BotController.BotExtractManager.IsBotAllowedToExfil(component))
        {
            Logger.LogWarning($"{bot.name} is not allowed to use extracting logic.");
        }
#endif

        if (!BotManagerComponent.Instance.BotExtractManager.TryFindExfilForBot(component))
        {
            return false;
        }

        return true;
    }

    private static bool DebugExternal
    {
        get { return SAINPlugin.DebugSettings.Logs.DebugExternal; }
    }

    public static float TimeSinceSenseEnemy(BotOwner botOwner)
    {
        var component = GetBotComponent(botOwner);
        if (component == null)
        {
            return float.MaxValue;
        }

        Enemy enemy = component.GoalEnemy;
        if (enemy == null)
        {
            return float.MaxValue;
        }

        return enemy.TimeSinceLastKnownUpdated;
    }

    public static bool IsPathTowardEnemy(NavMeshPath path, BotOwner botOwner, float ratioSameOverAll = 0.25f, float sqrDistCheck = 0.05f)
    {
        var component = GetBotComponent(botOwner);
        if (component == null)
        {
            return false;
        }

        Enemy enemy = component.GoalEnemy;
        if (enemy == null)
        {
            return false;
        }

        // Compare the corners in both paths, and check if the nodes used in each are the same.
        if (SAINBotSpaceAwareness.ArePathsDifferent(path, enemy.Path.PathToEnemy, ratioSameOverAll, sqrDistCheck))
        {
            return false;
        }

        return true;
    }

    public static bool CanBotQuest(BotOwner botOwner, Vector3 questPosition, float dotProductThresh = 0.33f)
    {
        var component = GetBotComponent(botOwner);
        if (component == null)
        {
            return false;
        }
        if (IsBotInCombat(component, out var reason))
        {
            if (DebugExternal)
            {
                Logger.LogInfo($"{botOwner.name} is currently engaging an enemy, cannot quest. Reason: [{reason}]");
            }

            return false;
        }
        if (IsBotSearching(component))
        {
            if (DebugExternal)
            {
                Logger.LogInfo($"{botOwner.name} is currently searching and hasn't cleared last known position, cannot quest.");
            }

            return false;
        }

        dotProductThresh = Mathf.Clamp(dotProductThresh, -1f, 1f);
        if (IsQuestHeadingTowardThreat(component, questPosition, dotProductThresh))
        {
            if (DebugExternal)
            {
                Logger.LogInfo($"{botOwner.name} cannot quest: objective direction aligns with an active threat (dot > {dotProductThresh}).");
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Same predicate as quest-blocking combat checks: under-fire, recent under-fire, QB threat hints (if loaded), or goal-enemy visibility / recent seen / heard windows.
    /// </summary>
    public static bool IsBotUnderCombatPressure(BotOwner botOwner)
    {
        var component = GetBotComponent(botOwner);
        return component != null && IsBotInCombat(component, out _);
    }

    /// <summary>
    /// True if the horizontal direction from the bot to <paramref name="questPosition"/> aligns with
    /// the direction to the current <see cref="BotComponent.GoalEnemy"/> last known position (dot product test).
    /// </summary>
    public static bool IsQuestTowardTarget(BotComponent component, Vector3 questPosition, float dotProductThresh)
    {
        if (component == null)
        {
            return false;
        }

        Vector3? currentTarget = component.GoalEnemy?.LastKnownPosition;
        if (currentTarget == null)
        {
            return false;
        }

        dotProductThresh = Mathf.Clamp(dotProductThresh, -1f, 1f);
        return IsQuestDirectionAlignedWithThreatOnPlane(
            component.Position,
            questPosition,
            currentTarget.Value,
            dotProductThresh
        );
    }

    /// <summary>
    /// Quest objective lies roughly toward a threat (goal enemy last known, else closest recently-sensed human PMC/Player).
    /// Used by <see cref="CanBotQuest"/> so QuestingBots does not march through a contact vector.
    /// </summary>
    private static bool IsQuestHeadingTowardThreat(BotComponent component, Vector3 questPosition, float dotProductThresh)
    {
        Enemy enemy = component.GoalEnemy;
        if (enemy?.LastKnownPosition != null)
        {
            return IsQuestDirectionAlignedWithThreatOnPlane(
                component.Position,
                questPosition,
                enemy.LastKnownPosition.Value,
                dotProductThresh
            );
        }

        enemy = FindClosestRecentlySensedHumanForQuestAlignment(component, QuestAlignSeenSec, QuestAlignHeardSec);
        if (enemy?.LastKnownPosition == null)
        {
            return false;
        }

        return IsQuestDirectionAlignedWithThreatOnPlane(
            component.Position,
            questPosition,
            enemy.LastKnownPosition.Value,
            dotProductThresh
        );
    }

    private static Enemy FindClosestRecentlySensedHumanForQuestAlignment(
        BotComponent component,
        float seenThreshold,
        float heardThreshold
    )
    {
        EnemyList knownEnemies = component?.EnemyController?.KnownEnemies;
        if (knownEnemies == null || knownEnemies.Count == 0)
        {
            return null;
        }

        Enemy best = null;
        float bestSqr = float.MaxValue;
        Vector3 botPos = component.Position;
        for (int i = 0; i < knownEnemies.Count; i++)
        {
            Enemy e = knownEnemies[i];
            if (e == null || e.IsAI)
            {
                continue;
            }

            Vector3? lk = e.LastKnownPosition;
            if (lk == null)
            {
                continue;
            }

            if (!e.IsVisible && e.TimeSinceSeen >= seenThreshold && e.TimeSinceHeard >= heardThreshold)
            {
                continue;
            }

            float d2 = (lk.Value - botPos).sqrMagnitude;
            if (d2 < bestSqr)
            {
                bestSqr = d2;
                best = e;
            }
        }

        return best;
    }

    /// <summary>X/Z only so vertical map offsets do not dominate the dot.</summary>
    private static bool IsQuestDirectionAlignedWithThreatOnPlane(
        Vector3 botPosition,
        Vector3 questPosition,
        Vector3 threatLastKnown,
        float dotProductThresh
    )
    {
        Vector3 toThreat = threatLastKnown - botPosition;
        Vector3 toQuest = questPosition - botPosition;
        toThreat.y = 0f;
        toQuest.y = 0f;
        if (toThreat.sqrMagnitude < QuestDirectionMinSqr || toQuest.sqrMagnitude < QuestDirectionMinSqr)
        {
            return false;
        }

        float dot = Vector3.Dot(toThreat.normalized, toQuest.normalized);
        return dot > dotProductThresh;
    }

    private static bool IsBotSearching(BotComponent component)
    {
        if (
            component.Decision.CurrentCombatDecision == ECombatDecision.Search
            || component.Decision.CurrentSquadDecision == ESquadDecision.Search
        )
        {
            return !component.Search.PathFinder.SearchedTargetPosition;
        }
        return false;
    }

    private static bool IsBotInCombat(BotComponent component, out ECombatReason reason)
    {
        const float TimeSinceSeenThreshold = 10f;
        const float TimeSinceHeardThreshold = 5f;
        const float TimeSinceUnderFireThreshold = 10f;

        reason = ECombatReason.None;
        if (component?.BotOwner?.Memory == null)
        {
            return false;
        }
        BotMemoryClass memory = component.BotOwner.Memory;
        if (memory.IsUnderFire)
        {
            reason = ECombatReason.UnderFireNow;
            return true;
        }
        float underFireTime = memory.UnderFireTime;
        if (underFireTime > 0f && Time.time - underFireTime <= TimeSinceUnderFireThreshold)
        {
            reason = ECombatReason.UnderFireRecently;
            return true;
        }
        if (
            ModDetection.QuestingBotsLoaded
            && HasQuestingBotsCombatSignals(component, TimeSinceSeenThreshold, TimeSinceHeardThreshold)
        )
        {
            reason = ECombatReason.QuestingBotsThreatSignal;
            return true;
        }

        Enemy enemy = component?.GoalEnemy;
        if (enemy == null)
        {
            return false;
        }
        if (enemy.IsVisible)
        {
            reason = ECombatReason.EnemyVisible;
            return true;
        }
        if (enemy.TimeSinceSeen < TimeSinceSeenThreshold)
        {
            reason = ECombatReason.EnemySeenRecently;
            return true;
        }
        if (enemy.TimeSinceHeard < TimeSinceHeardThreshold)
        {
            reason = ECombatReason.EnemyHeardRecently;
            return true;
        }
        return false;
    }

    private static bool HasQuestingBotsCombatSignals(BotComponent component, float seenThreshold, float heardThreshold)
    {
        SAINEnemyController enemyController = component?.EnemyController;
        if (enemyController == null)
        {
            return false;
        }
        if (enemyController.HumanEnemyInLineofSight)
        {
            return true;
        }
        if (enemyController.ActiveHumanEnemy && !enemyController.AtPeace)
        {
            return true;
        }
        if (!enemyController.AtPeace && HasRecentKnownHumanThreat(enemyController, seenThreshold, heardThreshold))
        {
            return true;
        }
        return false;
    }

    private static bool HasRecentKnownHumanThreat(SAINEnemyController enemyController, float seenThreshold, float heardThreshold)
    {
        EnemyList knownEnemies = enemyController.KnownEnemies;
        if (knownEnemies == null || knownEnemies.Humans <= 0)
        {
            return false;
        }
        for (int i = 0; i < knownEnemies.Count; i++)
        {
            Enemy enemy = knownEnemies[i];
            if (enemy == null || enemy.IsAI)
            {
                continue;
            }
            if (enemy.IsVisible || enemy.TimeSinceSeen < seenThreshold || enemy.TimeSinceHeard < heardThreshold)
            {
                return true;
            }
        }
        return false;
    }

    public enum ECombatReason
    {
        None = 0,
        EnemyVisible = 1,
        EnemyHeardRecently = 2,
        EnemySeenRecently = 3,
        UnderFireNow = 4,
        UnderFireRecently = 5,
        QuestingBotsThreatSignal = 6,
    }
}
