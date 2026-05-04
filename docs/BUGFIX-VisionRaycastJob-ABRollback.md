# Vision Raycast A/B Rollback (Detection Pipeline Debug)

**See also:** [VISION_BLINDNESS_AND_STUTTER.md](VISION_BLINDNESS_AND_STUTTER.md) — full stack diagnosis, `ScheduleBatch` buffer alignment fix, and per-raid vision counter reset.

## Goal

Isolate whether the persistent "bots engage but do not visually acquire/shoot player" issue is caused by fork timing changes in `VisionRaycastJob`.

## Trigger Evidence

From the latest `sain_bigbrain` run (schema 6), bots had human goals and combat pressure but zero visual/shoot confirmation:

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

- `E:\spt-tarkov-ai\backups\vision_ab_20260504_212844`

Backed up files:

- `OptimizedMod/SAIN/SAIN/Classes/BotManager/Jobs/VisionRaycastJob.cs`
- `OptimizedMod/SAINPerfLog/Components/RaidPerfCsvLogger.cs`

## Test Protocol

1. Build and deploy updated `SAIN.dll`.
2. Run one controlled raid (prefer Lighthouse for ExUsec reproduction).
3. Collect:
   - `sain_bigbrain_*.csv` (schema 6)
   - `sain_perf_*.csv`
4. Compare against the previous run:
   - Any movement from zero in:
     - `GoalHumanEnemyInfoVisibleCount`
     - `GoalHumanSainPartsVisibleCount`
     - `GoalHumanSainPartsLineOfSightCount`
     - `GoalHumanSainPartsCanShootCount`
     - `GoalHumanFinalVisibleCount`
     - `GoalHumanFinalCanShootCount`
5. Decision:
   - If counters rise from zero: timing cadence was a major contributor.
   - If still zero: next instrument raycast attempt/hit classification in `VisionRaycastJob` write-back path.

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

## Factory Run Follow-up (Schema v7) + Clamp Hardening

Latest analyzed run:

- `E:\SPT 4.0 Dev\BepInEx\LogOutput\sain_perf\sain_bigbrain_20260504_152218_factory4_day_5509df05.csv`

Observed on this run:

- Bots had active goals/combat pressure and were concentrated in near/mid distance buckets.
- `GoalHumanSainPartsVisibleCount`, `GoalHumanSainPartsLineOfSightCount`, `GoalHumanSainPartsCanShootCount`, `GoalHumanFinalVisibleCount`, and `GoalHumanFinalCanShootCount` remained zero.
- Critically, all `VisionRayAttempt*` counters were also zero.

Interpretation:

- The failure now points earlier than "hit classification"; ray attempt generation itself can degenerate/starve when runtime frequency/ray count values resolve to invalid/zero ranges.

Follow-up hardening fix (shipped):

File:
- `OptimizedMod/SAIN/SAIN/Classes/BotManager/Jobs/VisionRaycastJob.cs`

Changes:
1. Clamp `VisionRaycastFrequency` to `>= 1`
2. Clamp `LookUpdateFrequency` to `>= 1`
3. Clamp `MaxRaycastsPerEnemy` to `1..3`

Build/deploy:

- Build and deployment completed after applying the clamps.

Next validation criterion:

- In the next comparable run, `VisionRayAttempt*Total` should rise above zero. Once attempts recover, `GoalHumanSainParts*` and `GoalHumanFinal*` should no longer be permanently pinned at zero.
