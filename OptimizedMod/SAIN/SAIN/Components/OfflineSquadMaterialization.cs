using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using SAIN.Components.BotController;
using SAIN.Components.PlayerComponentSpace;
using SAIN.Models.Enums;
using SAIN.Plugin;
using UnityEngine;

namespace SAIN.Components;

/// <summary>
/// Offline → online handoff for SAIN-parked bots (<c>demat_*</c> squads). Releases <see cref="DematParkReason.SmartLos"/>
/// on proximity or main-camera LOS to squad center. <c>auto_*</c> pending offline casualties → live reconcile: <see cref="AutoSquadMaterialization"/>.
/// </summary>
public static class OfflineSquadMaterialization
{
    /// <summary>Recommended pre-materialization distance vs nominal player hearing range (doc: ~1.5×).</summary>
    public const float RecommendedRadiusVsHearing = 1.5f;

    private const float ProximityPassIntervalSeconds = 0.25f;
    private static float _nextProximityPassTime;

    private const float LosPassIntervalSeconds = 0.25f;
    private static float _nextLosPassTime;

    private const float LosHumanMaxDistanceSq = 400f * 400f;

    /// <summary>
    /// Throttled pass: rematerialize any <c>demat_*</c> offline row when a human enters ~1.5× nominal hearing
    /// of the squad center (so bots are not stuck in the pool when outside AILimit top-<c>N</c> ordering).
    /// </summary>
    public static void TryRematerializeDematSquadsNearHumans(BotManagerComponent manager, float currentTime)
    {
        if (manager == null || currentTime < _nextProximityPassTime)
        {
            return;
        }

        _nextProximityPassTime = currentTime + ProximityPassIntervalSeconds;

        AIFrameBudgetScheduler scheduler = manager.BudgetScheduler;
        BotDematerializationController demat = manager.Dematerialization;
        BotSpawnController spawn = manager.BotSpawnController;
        PlayerSpawnTracker tracker = manager.SAINGameWorld?.PlayerTracker;

        if (scheduler == null || demat == null || spawn == null || tracker?.AlivePlayerArray == null)
        {
            return;
        }

        var aliveHumans = new List<PlayerComponent>();
        foreach (PlayerComponent pc in tracker.AlivePlayerArray)
        {
            if (pc != null && !pc.IsAI && pc.Player?.HealthController?.IsAlive == true)
            {
                aliveHumans.Add(pc);
            }
        }

        if (aliveHumans.Count == 0 || scheduler.OfflineSquadCount == 0)
        {
            return;
        }

        var snapshot = new List<OfflineSquad>(scheduler.OfflineSquads);
        foreach (OfflineSquad squad in snapshot)
        {
            if (squad?.SquadId == null
                || !squad.SquadId.StartsWith(OfflineSquadWorldSync.DematerializeSquadIdPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (PlayerComponent humanPc in aliveHumans)
            {
                Player human = humanPc.Player;
                if (human == null)
                {
                    continue;
                }

                float hearing = GetNominalHearingMetersForMaterialize();
                if (TryBeginMaterializeProximity(squad, squad.CenterPosition, hearing, human))
                {
                    break;
                }
            }
        }
    }

    /// <summary>LOS-based remat: main camera sees squad center while a human is within LOS pass range.</summary>
    public static void TryRematerializeDematSquadsLosFromHumans(BotManagerComponent manager, float currentTime)
    {
        if (manager == null || currentTime < _nextLosPassTime)
        {
            return;
        }

        _nextLosPassTime = currentTime + LosPassIntervalSeconds;

        AIFrameBudgetScheduler scheduler = manager.BudgetScheduler;
        BotSpawnController spawn = manager.BotSpawnController;
        PlayerSpawnTracker tracker = manager.SAINGameWorld?.PlayerTracker;

        if (scheduler == null || manager.Dematerialization == null || spawn == null || tracker?.AlivePlayerArray == null)
        {
            return;
        }

        var aliveHumans = new List<PlayerComponent>();
        foreach (PlayerComponent pc in tracker.AlivePlayerArray)
        {
            if (pc != null && !pc.IsAI && pc.Player?.HealthController?.IsAlive == true)
            {
                aliveHumans.Add(pc);
            }
        }

        if (aliveHumans.Count == 0 || scheduler.OfflineSquadCount == 0)
        {
            return;
        }

        Vector3 center;
        foreach (OfflineSquad squad in scheduler.OfflineSquads.ToList())
        {
            if (squad?.SquadId == null
                || !squad.SquadId.StartsWith(OfflineSquadWorldSync.DematerializeSquadIdPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            center = squad.CenterPosition;
            bool anyHumanNear = false;
            for (int i = 0; i < aliveHumans.Count; i++)
            {
                Player hp = aliveHumans[i].Player;
                if (hp == null)
                {
                    continue;
                }

                if ((hp.Position - center).sqrMagnitude <= LosHumanMaxDistanceSq)
                {
                    anyHumanNear = true;
                    break;
                }
            }

            if (!anyHumanNear || !SmartDematLosCheck.MainCameraHasLosTo(center))
            {
                continue;
            }

            if (TryReleaseSmartLosForSquad(squad))
            {
                SmartDematTelemetry.SmartRematLos++;
            }
        }
    }

    private static bool TryBeginMaterializeProximity(
        OfflineSquad squad,
        Vector3 zoneCenter,
        float hearingRangeMeters,
        IPlayer humanPlayer)
    {
        if (squad == null || humanPlayer == null || humanPlayer.HealthController?.IsAlive != true)
        {
            return false;
        }

        if (humanPlayer.IsAI)
        {
            return false;
        }

        if (squad.SquadId == null
            || !squad.SquadId.StartsWith(OfflineSquadWorldSync.DematerializeSquadIdPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        float nominalHearing = Mathf.Max(1f, hearingRangeMeters);
        float triggerRadius = nominalHearing * RecommendedRadiusVsHearing;
        Vector3 hp = humanPlayer.Position;
        if ((zoneCenter - hp).sqrMagnitude > triggerRadius * triggerRadius)
        {
            return false;
        }

        string profileId = ResolveDematProfileId(squad);
        if (string.IsNullOrEmpty(profileId))
        {
            return false;
        }

        BotManagerComponent mgr = BotManagerComponent.Instance;
        BotSpawnController spawn = mgr?.BotSpawnController;
        if (spawn == null || mgr.Dematerialization == null)
        {
            return false;
        }

        BotComponent bot = spawn.GetSAIN(profileId);
        if (bot == null || !mgr.Dematerialization.IsDematerialized(profileId))
        {
            return false;
        }

        DematReleaseResult r = mgr.Dematerialization.TryReleaseParkReason(bot, DematParkReason.SmartLos);
        if (r == DematReleaseResult.FullyRematerialized)
        {
            SmartDematTelemetry.SmartRematNear++;
            return true;
        }

        return r == DematReleaseResult.PartialStillHeld;
    }

    private static bool TryReleaseSmartLosForSquad(OfflineSquad squad)
    {
        string profileId = ResolveDematProfileId(squad);
        if (string.IsNullOrEmpty(profileId))
        {
            return false;
        }

        BotManagerComponent mgr = BotManagerComponent.Instance;
        BotSpawnController spawn = mgr?.BotSpawnController;
        if (spawn == null || mgr.Dematerialization == null)
        {
            return false;
        }

        BotComponent bot = spawn.GetSAIN(profileId);
        if (bot == null || !mgr.Dematerialization.IsDematerialized(profileId))
        {
            return false;
        }

        return mgr.Dematerialization.TryReleaseParkReason(bot, DematParkReason.SmartLos)
            == DematReleaseResult.FullyRematerialized;
    }

    /// <summary>
    /// If <paramref name="squad"/> is a single-bot dematerialization row and a human is within the
    /// pre-materialization radius, release <see cref="DematParkReason.SmartLos"/> when it is the last holder.
    /// </summary>
    public static bool TryBeginMaterialize(
        OfflineSquad squad,
        Vector3 zoneCenter,
        float hearingRangeMeters,
        IPlayer humanPlayer)
    {
        return TryBeginMaterializeProximity(squad, zoneCenter, hearingRangeMeters, humanPlayer);
    }

    private static string ResolveDematProfileId(OfflineSquad squad)
    {
        if (squad.Members != null && squad.Members.Count > 0 && !string.IsNullOrEmpty(squad.Members[0].BotId))
        {
            return squad.Members[0].BotId;
        }

        string prefix = OfflineSquadWorldSync.DematerializeSquadIdPrefix;
        if (squad.SquadId.Length > prefix.Length)
        {
            return squad.SquadId.Substring(prefix.Length);
        }

        return null;
    }

    private static float GetNominalHearingMetersForMaterialize()
    {
        float hearingMeters = 100f;
        var limits = SAINPlugin.LoadedPreset?.GlobalSettings?.General?.AILimit?.MaxHearingRanges;
        if (limits != null && limits.TryGetValue(AILimitSetting.Far, out float farH) && farH > 1f)
        {
            hearingMeters = farH;
        }

        return hearingMeters;
    }
}
