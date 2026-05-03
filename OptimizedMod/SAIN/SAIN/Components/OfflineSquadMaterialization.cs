using EFT;
using UnityEngine;

namespace SAIN.Components;

/// <summary>
/// Placeholder for offline → online squad transition (see docs/PERFORMANCE_PLAN.md Phase 2.5).
/// When implemented: spawn or repurpose bots near <paramref name="zoneCenter"/> before the player
/// enters visual range, then call <see cref="AIFrameBudgetScheduler.UnregisterOfflineSquad"/>.
/// </summary>
public static class OfflineSquadMaterialization
{
    /// <summary>Recommended pre-materialization distance vs nominal player hearing range (doc: ~1.5×).</summary>
    public const float RecommendedRadiusVsHearing = 1.5f;

    /// <summary>
    /// Reserved hook — no-op until statistical offline combat + spoofed audio are validated in-raid.
    /// </summary>
    public static bool TryBeginMaterialize(
        OfflineSquad squad,
        Vector3 zoneCenter,
        float hearingRangeMeters,
        IPlayer humanPlayer)
    {
        _ = squad;
        _ = zoneCenter;
        _ = hearingRangeMeters;
        _ = humanPlayer;
        return false;
    }
}
