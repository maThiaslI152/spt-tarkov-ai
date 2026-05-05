# Vision Raycast A/B Rollback (Detection Pipeline Debug)

> **Schema note:** Evidence in this doc references **schema 6–7** era CSVs. Current `sain_bigbrain_*.csv` uses **`SchemaVersion = 8`** (`VisionRayEffectiveLosTotal`, `VisionRayEffectiveVisionTotal`, `VisionRayEffectiveShootTotal` — same success predicate as `RaycastResult.CountsAsGameplaySuccess`). See [SAIN_PERFLOG.md](SAIN_PERFLOG.md) and [VISION_BLINDNESS_AND_STUTTER.md](VISION_BLINDNESS_AND_STUTTER.md).

**See also:** [VISION_BLINDNESS_AND_STUTTER.md](VISION_BLINDNESS_AND_STUTTER.md) — full stack diagnosis, `ScheduleBatch` buffer alignment fix, and per-raid vision counter reset.

## Goal

Isolate whether the persistent "bots engage but do not visually acquire/shoot player" issue is caused by fork timing changes in `VisionRaycastJob`.

## Trigger Evidence

From a `sain_bigbrain` run at **schema 6**, bots had human goals and combat pressure but zero visual/shoot confirmation:

- `GoalHumanCount > 0`
- `GoalHumanEnemyInfoVisibleCount = 0`
- `GoalHumanSainPartsVisibleCount = 0`
- `GoalHumanSainPartsLineOfSightCount = 0`
- `GoalHumanSainPartsCanShootCount = 0`
- `GoalHumanFinalVisibleCount = 0`
- `GoalHumanFinalCanShootCount = 0`

This indicates a failure before final combat arbitration, inside the vision acquisition pipeline.

## A/B Change Applied

File changed:

- `OptimizedMod/SAIN/SAIN/Classes/BotManager/Jobs/VisionRaycastJob.cs`

Rollback to original cadence behavior for comparison:

1. In `EnemyVisionJob()`:
   - reverted post-schedule yield from `yield return wait;` to `yield return null;`
2. In `UpdateEFTVision()`:
   - removed cached `WaitForSeconds(VisionUpdateInterval)` tick pacing
   - reverted loop yield to `yield return null;`

No other vision logic was changed in this A/B step.

## Backup Snapshot

Pre-change source backup created at:

- `backups/vision_ab_20260504_212844/` (repo-relative)

Backed up files:

- `OptimizedMod/SAIN/SAIN/Classes/BotManager/Jobs/VisionRaycastJob.cs`
- `OptimizedMod/SAINPerfLog/Components/RaidPerfCsvLogger.cs`

## Test Protocol

1. Build and deploy updated `SAIN.dll` (and `SAINPerfLog.dll` if CSV schema changed).
2. Run one controlled raid (prefer Lighthouse for ExUsec reproduction).
3. Collect:
   - `sain_bigbrain_*.csv` — check `SchemaVersion` column (**8** = current)
   - `sain_perf_*.csv`
4. Compare against the previous run:
   - **Human pipeline:** `GoalHumanEnemyInfoVisibleCount`, `GoalHumanSainParts*`, `GoalHumanFinal*`
   - **Low-level rays (v7+):** `VisionRayAttempt*`, `VisionRayTarget*`, `VisionRayBlocked*`
   - **Gameplay-aligned success (v8+):** `VisionRayEffectiveLosTotal`, `VisionRayEffectiveVisionTotal`, `VisionRayEffectiveShootTotal` (non-zero attempts with effective success indicate LOS/vision/shoot path recovering even when strict `VisionRayTarget*` stays low indoors)
5. Decision:
   - If counters rise from zero: timing cadence was a major contributor.
   - If still zero: instrument raycast schedule / clamps / pairing (see canonical vision doc).

## Why This Step Matters

This is a narrow, reversible A/B rollback that tests timing sensitivity without conflating with layer, decision, or AI-limit changes.

## Outcome + Follow-up Implementation

Outcome from the first post-rollback run: **problem persisted**.

- `GoalHumanCount` stayed high (bots had human goals under combat pressure).
- `GoalHumanSainPartsVisibleCount`, `GoalHumanSainPartsLineOfSightCount`, `GoalHumanSainPartsCanShootCount`, and final visible/shoot counters remained at zero.
- This ruled out cadence rollback as a standalone fix.

Follow-up shipped immediately after:

1. **Vision diagnostics expansion (Schema v7)**
   - Added low-level `VisionRaycastJob` cumulative counters (attempt / null / target / blocked for LOS, Vision, Shoot).
   - Exported in `sain_bigbrain` as `VisionRay*Total` columns.

2. **Combat-pressure looting gate**
   - Updated `LootingLayer.IsActive()` to hard-return `false` while SAIN reports combat pressure (`SAINExternal.IsBotUnderCombatPressure` via reflection).
   - Purpose: prevent looting layer takeover during active engagement so vision diagnosis is not masked by arbitration noise.

3. **Schema v8 (effective success totals)**
   - Adds `VisionRayEffective*` columns using `RaycastResult.CountsAsGameplaySuccess` so telemetry matches “did gameplay treat this ray as a success?” vs strict first-hit body collider (`VisionRayTarget*`).

## Factory Run Follow-up (Schema v7) + Clamp Hardening

Example analyzed run (historical path in doc; use your own `sain_perf` folder):

- `sain_bigbrain_*_factory4_day_*.csv`

Observed on that run:

- Bots had active goals/combat pressure and were concentrated in near/mid distance buckets.
- `GoalHumanSainPartsVisibleCount`, `GoalHumanSainPartsLineOfSightCount`, `GoalHumanSainPartsCanShootCount`, `GoalHumanFinalVisibleCount`, and `GoalHumanFinalCanShootCount` remained zero.
- Critically, all `VisionRayAttempt*` counters were also zero.

Interpretation:

- The failure pointed earlier than "hit classification"; ray attempt generation could degenerate when frequency / ray count resolved to invalid/zero ranges.

Follow-up hardening fix (shipped) in `VisionRaycastJob.cs`:

1. Clamp `VisionRaycastFrequency` to `>= 1`
2. Clamp `LookUpdateFrequency` to `>= 1`
3. Clamp `MaxRaycastsPerEnemy` to `1..3`

**Next validation (current stack):** `VisionRayAttempt*` should be non-zero in combat; use **`VisionRayEffective*`** (v8) alongside `GoalHuman*` to judge player visibility pipeline health.
