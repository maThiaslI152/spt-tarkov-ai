using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using SAIN.Components.BotController;
using SAIN.Components.PlayerComponentSpace;
using SAIN.Interop;
using SAIN.Plugin;
using UnityEngine;

namespace SAIN.Components;

/// <summary>Who requested parking a bot in the dematerialization controller.</summary>
[Flags]
public enum DematParkReason
{
    None = 0,
    Ailimit = 1,
    SmartLos = 2,
}

/// <summary>Result of releasing one park holder.</summary>
public enum DematReleaseResult
{
    NotTracked = 0,
    PartialStillHeld = 1,
    FullyRematerialized = 2,
}

/// <summary>Cooldown and raid reset for SMART dematerialize / rematerialize arbitration.</summary>
public static class SainDematPolicy
{
    private const float RematDematSuppressSeconds = 2.5f;

    private static readonly Dictionary<string, float> NextSmartDematEligibleTime = new(StringComparer.Ordinal);

    /// <summary>Raid reset: call from <see cref="BotManagerComponent.Activate"/>.</summary>
    public static void ResetForNewRaid()
    {
        NextSmartDematEligibleTime.Clear();
    }

    public static void NotifyFullRematerialized(string profileId)
    {
        if (string.IsNullOrEmpty(profileId))
        {
            return;
        }

        NextSmartDematEligibleTime[profileId] = Time.time + RematDematSuppressSeconds;
    }

    public static bool CanApplySmartDemat(string profileId)
    {
        if (string.IsNullOrEmpty(profileId))
        {
            return true;
        }

        return !NextSmartDematEligibleTime.TryGetValue(profileId, out float t) || Time.time >= t;
    }
}

/// <summary>Lightweight main-camera LOS check (single-player SPT host); mirrors <see cref="SAINAILimit"/> approach.</summary>
internal static class SmartDematLosCheck
{
    private const float MaxRayMeters = 500f;

    internal static bool MainCameraHasLosTo(Vector3 botWorldPosition)
    {
        var cam = Camera.main;
        if (cam == null)
        {
            return false;
        }

        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        var bounds = new Bounds(botWorldPosition, Vector3.one * 0.5f);
        if (!GeometryUtility.TestPlanesAABB(planes, bounds))
        {
            return false;
        }

        Vector3 camPos = cam.transform.position;
        Vector3 dir = botWorldPosition - camPos;
        float dist = dir.magnitude;
        if (dist < 0.01f || dist > MaxRayMeters)
        {
            return false;
        }

        return !Physics.Raycast(
            camPos,
            dir.normalized,
            dist,
            LayerMaskClass.HighPolyWithTerrainNoGrassMask,
            QueryTriggerInteraction.Ignore);
    }
}

/// <summary>Telemetry counters for SMART demat/remat (read by SAINPerfLog).</summary>
public static class SmartDematTelemetry
{
    public static long SmartDematCandidates;
    public static long SmartDematApplied;
    public static long SmartRematLos;
    public static long SmartRematNear;
    public static long AutoSpawnAttempts;
    public static long AutoSpawnFailures;

    /// <summary>Successful <c>auto_*</c> offline-casualty application ticks (at least one live bot killed).</summary>
    public static long AutoMatApplied;

    public static void ResetForNewRaid()
    {
        SmartDematCandidates = 0;
        SmartDematApplied = 0;
        SmartRematLos = 0;
        SmartRematNear = 0;
        AutoSpawnAttempts = 0;
        AutoSpawnFailures = 0;
        AutoMatApplied = 0;
    }
}

/// <summary>
/// Balanced SMART gate: dematerialize bots that are far from humans, have had no main-camera LOS
/// for a sustained window, and pass gameplay guards.
/// </summary>
public static class SmartDematerializeGate
{
    private const float MinDistanceMeters = 180f;
    private const float NoLosSecondsRequired = 8f;

    private static readonly Dictionary<string, float> NoLosAccumSeconds = new(StringComparer.Ordinal);

    /// <summary>Canonical tick order: after <see cref="OfflineSquadWorldSync.TrySync"/> and remat passes, before <see cref="AIFrameBudgetScheduler.ProcessFrame"/>.</summary>
    public static void TryApply(BotManagerComponent manager, float currentTime, float deltaTime)
    {
        if (manager?.BotSpawnController?.SAINBots == null || manager.Dematerialization == null)
        {
            return;
        }

        PlayerSpawnTracker tracker = manager.SAINGameWorld?.PlayerTracker;
        if (tracker?.AlivePlayerArray == null || tracker.AlivePlayerArray.Count == 0)
        {
            return;
        }

        var humans = new List<PlayerComponent>();
        foreach (PlayerComponent pc in tracker.AlivePlayerArray)
        {
            if (pc != null && !pc.IsAI && pc.Player?.HealthController?.IsAlive == true)
            {
                humans.Add(pc);
            }
        }

        if (humans.Count == 0)
        {
            return;
        }

        float minDistSqr = MinDistanceMeters * MinDistanceMeters;

        foreach (BotComponent bot in manager.BotSpawnController.SAINBots.ToList())
        {
            if (bot == null || bot.IsDead || !bot.BotActive || string.IsNullOrEmpty(bot.ProfileId))
            {
                continue;
            }

            if (manager.Dematerialization.IsDematerialized(bot.ProfileId))
            {
                NoLosAccumSeconds.Remove(bot.ProfileId);
                continue;
            }

            if (!SainDematPolicy.CanApplySmartDemat(bot.ProfileId))
            {
                continue;
            }

            WildSpawnType wt = bot.Info?.Profile?.WildSpawnType ?? WildSpawnType.assault;
            if (BotSpawnController.IsWildSpawnStrictlyExcluded(wt))
            {
                continue;
            }

            BotOwner owner = bot.BotOwner;
            if (owner == null || owner.IsDead)
            {
                continue;
            }

            if (SAINExternal.IsBotUnderCombatPressure(owner))
            {
                NoLosAccumSeconds.Remove(bot.ProfileId);
                continue;
            }

            Vector3 botPos = owner.Position;
            float bestDistSqr = float.MaxValue;
            for (int i = 0; i < humans.Count; i++)
            {
                Player hp = humans[i].Player;
                if (hp == null)
                {
                    continue;
                }

                float d = (hp.Position - botPos).sqrMagnitude;
                if (d < bestDistSqr)
                {
                    bestDistSqr = d;
                }
            }

            if (bestDistSqr < minDistSqr)
            {
                NoLosAccumSeconds.Remove(bot.ProfileId);
                continue;
            }

            bool hasLos = SmartDematLosCheck.MainCameraHasLosTo(bot.Transform?.EyePosition ?? botPos);
            if (hasLos)
            {
                NoLosAccumSeconds[bot.ProfileId] = 0f;
                continue;
            }

            NoLosAccumSeconds.TryGetValue(bot.ProfileId, out float acc);
            acc += deltaTime;
            NoLosAccumSeconds[bot.ProfileId] = acc;

            if (acc < NoLosSecondsRequired)
            {
                continue;
            }

            SmartDematTelemetry.SmartDematCandidates++;
            if (manager.Dematerialization.RequestDematerialize(bot, "smart-los-distance"))
            {
                SmartDematTelemetry.SmartDematApplied++;
                NoLosAccumSeconds.Remove(bot.ProfileId);
            }
        }
    }
}
