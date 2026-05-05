# BUGFIX — SAINPerfLog distance + engagement telemetry (introduced in schema v4)

> **Canonical schema today:** BigBrain snapshot **`SchemaVersion = 8`** in `OptimizedMod/SAINPerfLog/Components/RaidPerfCsvLogger.cs`.  
> This document describes the **v4 milestone** (distance + engagement columns). Later schemas add decision CPU, vision ray counters, `VisionRayEffective*`, etc. — see [SAIN_PERFLOG.md](SAIN_PERFLOG.md).

## Why this change was needed

Previous `sain_bigbrain_*.csv` snapshots (Schema v3) exposed combat-pressure and mismatch signals, but not explicit **player-distance context**.  
For this project, distance is a core principle: we need to answer "what AI does up close vs far away" and "how AI reacts to player engagement at each distance band."

Without distance-banded counters, audits could confirm layer mismatch but could not prove whether bots near the player were engaging, shooting, or staying in non-combat layers.

## What was implemented (schema v4)

Updated `OptimizedMod/SAINPerfLog/Components/RaidPerfCsvLogger.cs`:

- BigBrain snapshot schema version bumped:
  - `SchemaVersion`: `3 -> 4`

- Added distance buckets (relative to `GameWorld.MainPlayer`):
  - `DistNearCount` (`< 30m`)
  - `DistMidCount` (`30m - < 80m`)
  - `DistFarCount` (`>= 80m`)

- Added engagement-at-distance counters:
  - `EngagedNearCount`
  - `EngagedMidCount`
  - `EngagedFarCount`
  - Engaged condition: `GoalEnemy || CombatDecision != None || SAINExternal.IsBotUnderCombatPressure(...)`

- Added ExUsec-specific engagement-at-distance counters:
  - `ExUsecEngagedNearCount`
  - `ExUsecEngagedMidCount`
  - `ExUsecEngagedFarCount`

- Added immediate firing-opportunity counters by distance:
  - `CanShootNowNearCount`
  - `CanShootNowMidCount`
  - `CanShootNowFarCount`
  - Condition: `GoalEnemy != null && GoalEnemy.IsVisible && GoalEnemy.CanShoot`

- Added safe main-player position probe:
  - `TryGetMainPlayerPosition(out Vector3 position)`
  - If unavailable, distance counters remain `0` for that sample instead of crashing telemetry.

## CSV impact (still present in v8)

`sain_bigbrain_*.csv` includes these columns (among others added in v5–v8):

- `DistNearCount,DistMidCount,DistFarCount`
- `EngagedNearCount,EngagedMidCount,EngagedFarCount`
- `ExUsecEngagedNearCount,ExUsecEngagedMidCount,ExUsecEngagedFarCount`
- `CanShootNowNearCount,CanShootNowMidCount,CanShootNowFarCount`

## Audit value

This enables direct runtime answers to:

- At this sample, how many AI are near/mid/far from player?
- Are near AI actually engaged?
- Are ExUsec engaged near player or stuck in non-combat patterns?
- Do near/mid/far AI have immediate shot opportunities?

## Build/deploy

```bash
dotnet build OptimizedMod/SAINPerfLog/SAINPerfLog.csproj -c Release
```

Install `SAINPerfLog.dll` per [MOD_BUILD_AND_DEPLOY.md](MOD_BUILD_AND_DEPLOY.md).
