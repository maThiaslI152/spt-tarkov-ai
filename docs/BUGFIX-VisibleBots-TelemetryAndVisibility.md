# Bugfix: 0 Visible Bots in CSV Telemetry + Unused QueryParameters in Visibility Raycast

> **Date:** 2026-05-04 | **Doc tidied:** 2026-05-06 (line refs + raycast “after” matches code)  
> **Status:** Fixed & Deployed | **Files:** 2 files across SAIN + Scheduler

---

## Symptoms Observed

1. **`VisibleBots=0` in CSV logs for most of the raid:** The `sain_perf_*.csv` column `VisibleBots` consistently showed `0` even during active combat with multiple bots, while `TotalOnlineBots` showed the correct count (e.g., `VisibleBots=0, TotalOnlineBots=10`).
2. **Perception tier histogram in BigBrain CSV also skewed:** The `PerceptionTierHistogram` column in `sain_bigbrain_*.csv` showed counts that did not match observed combat activity.

### Log Evidence

From `sain_perf_*.csv` during a Factory raid with active fighting:
```
VisibleBots=0, AudibleBots=0, OccludedBots=0, TotalOnlineBots=12
```

Despite 12 bots online and engaged in combat, the tier breakdown showed zero bots in any tier. This made the CSV misleading for diagnosing perception system health.

---

## Root Causes

### Root Cause A: Force-Tick Bots Excluded from Tier Counting (PRIMARY)

**Location:** `OptimizedMod/SAIN/SAIN/Components/AIFrameBudgetScheduler.cs` — `ProcessFrame()` tiering (line numbers drift; search `forceTickBots` and `VisibleBotsLastFrame`).

The `ProcessFrame()` method (historical layout) had three phases:
1. **Collect force-tick bots** — bots with `ActiveHumanEnemy`, `GoalEnemy`, `KnownEnemies`, etc. These are the bots most likely to be Visible.
2. **Process force-tick bots** — they always tick regardless of budget.
3. **Classify non-force-tick bots by tier** — `VisibleBots`, `AudibleBots`, `OccludedBots` were counted here only.

**The bug:** Line 161-162 explicitly skips force-tick bots with:
```csharp
if (forceTickBots.Contains(bot))
    continue;
```

Force-tick bots are the ones most likely to be Visible (they have `ActiveHumanEnemy`, are in combat, etc.), but they are excluded from the tier counts entirely. During combat, nearly ALL bots are force-ticked, so the tier breakdown always shows zeros.

```
Combat scenario:
  forceTickBots        = 10 bots (in combat with player)
  tier classification  = 0 bots (all force-ticked and excluded)
  VisibleBotsLastFrame = 0     ← WRONG
  AudibleBotsLastFrame = 0     ← WRONG
  OccludedBotsLastFrame = 0    ← WRONG
  TotalOnlineBots      = 10    ← correct (forceTickBots.Count + tier counts)
```

### Root Cause B: Unused QueryParameters in Visibility Raycast (SECONDARY)

**Location:** `OptimizedMod/SAIN/SAIN/Classes/Bot/SAINAILimit.cs` — `CheckPlayerCanSeeBot()` (historical)

The `CheckPlayerCanSeeBot()` method declared `QueryParameters` with `QueryTriggerInteraction.Ignore` but never passed them to the raycast:

```csharp
// ORIGINAL:
var raycastParams = new QueryParameters(
    LayerMaskClass.HighPolyWithTerrainNoGrassMask,
    false,
    QueryTriggerInteraction.Ignore);
_cachedIsVisible = !Physics.Raycast(
    cameraPos,
    direction.normalized,
    distance,
    LayerMaskClass.HighPolyWithTerrainNoGrassMask);  // bare int layerMask — raycastParams unused!
```

The `Physics.Raycast` overload receiving a bare `int layerMask` uses `QueryTriggerInteraction.UseGlobal`, which depends on `Physics.queriesHitTriggers` (default: `true`). If trigger colliders (zone triggers, extraction zones, quest triggers) exist on the `HighPolyWithTerrainNoGrassMask` layer, they would block the raycast and cause the bot to be incorrectly classified as not visible.

This is a secondary bug that could cause occasional false negatives in visibility detection — the player is looking directly at a bot but a trigger collider in the path causes the raycast to report "hit" (visible = false).

---

## Fixes Applied

### Fix 1: Force-tick bots now contribute to tier counts

**File:** `OptimizedMod/SAIN/SAIN/Components/AIFrameBudgetScheduler.cs`

**Change:** After the force-tick processing loop, **classify each force-tick bot** into `visibleBots` / `audibleBots` / `occludedBots` using `bot.CurrentPerceptionTier` before counting non–force-tick bots:

```csharp
// NEW — classify force-tick bots into their perception tiers so the
// CSV columns are accurate during combat.
foreach (var bot in forceTickBots)
{
    var tier = bot.CurrentPerceptionTier;
    switch (tier)
    {
        case PerceptionTier.Visible:
            visibleBots.Add(bot);
            break;
        case PerceptionTier.Audible:
            audibleBots.Add(bot);
            break;
        default:
            occludedBots.Add(bot);
            break;
    }
}
```

The existing non-force-tick classification loop still skips `forceTickBots` so bots are not double-processed — only tier **counts** were wrong before the extra loop.

**Effect:** After this fix, the combat scenario from above becomes:
```
Combat scenario (after fix):
  forceTickBots        = 10 bots (in combat)
  force-tick tier loop = 8 Visible + 2 Audible
  non-force-tick loop  = 0 bots (all were force-ticked)
  VisibleBotsLastFrame = 8     ← correct
  AudibleBotsLastFrame = 2     ← correct
  OccludedBotsLastFrame = 0    ← correct
  TotalOnlineBots      = 10    ← still correct
```

### Fix 2: Visibility raycast ignores triggers

**File:** `OptimizedMod/SAIN/SAIN/Classes/Bot/SAINAILimit.cs`

**Change:** Use a `Physics.Raycast` overload that passes **`QueryTriggerInteraction.Ignore`** (either via `QueryParameters` or the dedicated overload). The **current** shipping code uses the 5-argument overload:

```csharp
_cachedIsVisible = !Physics.Raycast(
    cameraPos,
    direction.normalized,
    distance,
    LayerMaskClass.HighPolyWithTerrainNoGrassMask,
    QueryTriggerInteraction.Ignore);
```

**Effect:** Zone / quest / extraction **trigger** colliders are less likely to false-block LOS compared to the old bare `layerMask` overload (which followed `Physics.queriesHitTriggers` / global defaults).

---

## Files Modified

| # | File | Change |
|---|------|--------|
| 1 | `OptimizedMod/SAIN/SAIN/Components/AIFrameBudgetScheduler.cs` | Added force-tick bot tier classification loop after force-tick processing |
| 2 | `OptimizedMod/SAIN/SAIN/Classes/Bot/SAINAILimit.cs` | `Physics.Raycast(..., QueryTriggerInteraction.Ignore)` in `CheckPlayerCanSeeBot()` |

---

## Design Rationale

### Force-tick tier counting (Fix 1) — Why not remove the skip entirely?

The `forceTickBots.Contains(bot) continue;` check in the non-force-tick loop serves a legitimate purpose: it avoids processing bots twice (force-tick already processed them). The fix adds a separate counting loop specifically for the force-tick bots, keeping the processing loop and the non-force-tick classification loop untouched. This is the minimal change with zero risk of double-processing.

### Trigger interaction (Fix 2)

An earlier revision built `QueryParameters` but called an overload that ignored them; the fix is to **always** route visibility through an overload that sets **`QueryTriggerInteraction.Ignore`** explicitly.

---

## Verification Guide

### CSV Log Check
After deploying the fix, `sain_perf_*.csv` should show:
- `VisibleBots` > 0 during combat (matching bots fighting the player or in view)
- `AudibleBots` > 0 for bots the player can hear but not see
- `OccludedBots` for bots outside player awareness
- `VisibleBots + AudibleBots + OccludedBots + (offline bots)` should roughly match `TotalOnlineBots`

### Behavior Check
1. **CSV accuracy:** Run a raid with SAINPerfLog enabled. During active combat on Factory or Customs, verify the `VisibleBots` column shows a non-zero count that correlates with visible combat activity.
2. **BigBrain snapshot:** Check `sain_bigbrain_*.csv` `PerceptionTierHistogram` column — it should show `Visible=N` for bots in combat/in-view.
3. **No regression:** Bot combat behavior should be unchanged — force-tick bots still process every frame, and the perception tier assignment logic is untouched.
