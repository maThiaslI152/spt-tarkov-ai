# SAINPerfLog — standalone raid telemetry (implemented)

> **Last updated:** 2026-05-05  
> **Status:** Shipped in-repo under `OptimizedMod/SAINPerfLog/`. This document is the **canonical summary** of what was built and how it relates to **SAIN**. For original design rationale and migration notes, see [SAIN_PERFLOG_STANDALONE_PLAN.md](SAIN_PERFLOG_STANDALONE_PLAN.md).

**Agents:** [INDEX.md](../INDEX.md) · [AGENTS.md](AGENTS.md) (builds + read order).

---

## Summary

**SAINPerfLog** is a **separate BepInEx plugin** from **SAIN**. It owns:

| Concern | Owner |
|--------|--------|
| Per-raid performance CSV (timestamped files, no cross-raid overwrite) | **SAINPerfLog** (`RaidPerfCsvLogger`) |
| Optional sparse BigBrain aggregate snapshot CSV | **SAINPerfLog** |
| **F12** read-only lines (FPS rolling average, `AIFrameBudgetScheduler` stats, active CSV paths) | **SAINPerfLog** (`PerfLogPlugin`, category **`SAINPerfLog (F12)`**) |
| **Diagnostic logging** toggle for spammy `[SAIN DIAG]` traces | **SAINPerfLog** (same F12 category) |
| `AIFrameBudgetScheduler`, perception tiers, bot ticks | **SAIN** (unchanged responsibility) |
| Gating diagnostic spam inside SAIN when the perf plugin is present | **SAIN** via [`SainPerfLogInterop`](../OptimizedMod/SAIN/SAIN/Interop/SainPerfLogInterop.cs) |

**SAIN** no longer ships a **“SAIN Performance”** BepInEx config section, `SAINPerformanceMonitor`, or in-SAIN CSV ownership.

---

## Plugin identity

| | |
|---|---|
| **Project** | `OptimizedMod/SAINPerfLog/SAINPerfLog.csproj` |
| **Assembly** | `SAINPerfLog.dll` |
| **BepInPlugin GUID** | `me.sol.sain.perflog` |
| **SAIN dependency** | `[BepInDependency("me.sol.sain", SoftDependency)]` — logger can load after SAIN; raid hook reads live scheduler state |

---

## Deploy layout (typical SPT client)

Install **both** DLLs for full behavior:

- `BepInEx/plugins/SAIN/SAIN.dll` (or your pack layout)
- `BepInEx/plugins/SAINPerfLog/SAINPerfLog.dll`

You may update **`SAINPerfLog.dll` alone** when only telemetry/F12 changes, as long as the **interop contract** below stays compatible.

---

## BepInEx configuration

| Category | Purpose |
|----------|---------|
| **SAINPerfLog** | Auto logging on/off, perf CSV interval, BigBrain snapshot options, output directory relative to BepInEx root, optional `*_latest.csv` aliases |
| **SAINPerfLog (F12)** | **F12 Status Lines** (read-only refresh), **Diagnostic Logging** (spam to `LogOutput.log`), read-only FPS / budget / bot / active path lines |

Open with **F12 → Configuration Manager → SAINPerfLog** (plugin list).

---

## Output files

Default directory (configurable): `BepInEx/LogOutput/sain_perf/` (relative to BepInEx root unless overridden).

| Artifact | Pattern / notes |
|----------|-----------------|
| Per-raid perf CSV | `sain_perf_{UtcTimestamp}_{LocationId}_{SessionToken}.csv` — **`LocationId`** is taken from `GameWorld.LocationId` (or `MainPlayer.Location`) **after** raid init, so filenames match EFT map ids (e.g. `lighthouse`, `bigmap`). Writers open on the first frame where the id is non-empty, or after **30 s** fallback to `unknown` if still unset. |
| Per-raid BigBrain snapshot CSV | `sain_bigbrain_{UtcTimestamp}_{LocationId}_{SessionToken}.csv` (when snapshots enabled) — same naming rules. |
| Optional aliases | `sain_perf_latest.csv`, `sain_bigbrain_latest.csv` when **Write latest aliases** is enabled |

Writers **open shortly after** raid world creation (deferred from `GameWorldUnityTickListener.Create` so `LocationId` is populated) and **close on `GameWorld.OnDispose`** (hideout skipped; Fika client path skipped when applicable). Files are **UTF-8 with BOM** for easier Excel import.

### CSV schemas (current)

**Perf row** (header includes): existing budget/tier/pool columns plus trailing **`RaidElapsedSec`**, **`LocationId`**, **`SessionId`** (same short token as in the filename) for joins and dashboards.

**BigBrain snapshot** — **`SchemaVersion` = 8**: v7 columns plus **`VisionRayEffectiveLosTotal`**, **`VisionRayEffectiveVisionTotal`**, **`VisionRayEffectiveShootTotal`** (cumulative raid totals; same success rule as `RaycastResult.CountsAsGameplaySuccess`: **null hit OR body collider/root match**).
- Distance buckets to main player: `DistNearCount` (<30m), `DistMidCount` (30-80m), `DistFarCount` (>=80m).
- Engagement-at-distance counters: `EngagedNearCount`, `EngagedMidCount`, `EngagedFarCount` (engaged = goal enemy OR combat decision OR combat pressure).
- ExUsec engagement counters: `ExUsecEngagedNearCount`, `ExUsecEngagedMidCount`, `ExUsecEngagedFarCount`.
- Immediate firing opportunity counters: `CanShootNowNearCount`, `CanShootNowMidCount`, `CanShootNowFarCount` (`GoalEnemy.IsVisible && GoalEnemy.CanShoot`).
- Squad-command utilization now: `SquadCommandedNowCount`, `SquadCommandUtilNowPct`.
- Local decision collapse deltas (between snapshots): `DecisionTicksDelta`, `DecisionSkipsDelta`, `DecisionSkipRatePct`, `DecisionPreemptionsDelta`, `SquadOrdersReceivedDelta`.
- Decision CPU impact deltas (between snapshots): `DecisionCpuExecutedDeltaMs`, `DecisionCpuSavedDeltaMs`, `DecisionCpuDeltaMs` (`saved - executed`), `DecisionCpuSavedPerSkipMs`.
- Vision raycast cumulative counters (from `VisionRaycastJob`) — **reset at raid start** (`VisionRaycastJob.ResetDiagnosticsForNewRaid` from `BotManagerComponent.Activate`). Semantics: [VISION_BLINDNESS_AND_STUTTER.md](VISION_BLINDNESS_AND_STUTTER.md) §3.6.
  - Attempts: `VisionRayAttemptLosTotal`, `VisionRayAttemptVisionTotal`, `VisionRayAttemptShootTotal`
  - Null-hit outcomes: `VisionRayNullLosTotal`, `VisionRayNullVisionTotal`, `VisionRayNullShootTotal`
  - **Strict** target-collider first hit: `VisionRayTargetLosTotal`, `VisionRayTargetVisionTotal`, `VisionRayTargetShootTotal` (often **0** indoors while `Blocked*` is high — **not** proof of blindness)
  - Blocked outcomes: `VisionRayBlockedLosTotal`, `VisionRayBlockedVisionTotal`, `VisionRayBlockedShootTotal`
  - **Gameplay-aligned** success (schema **8+**): `VisionRayEffectiveLosTotal`, `VisionRayEffectiveVisionTotal`, `VisionRayEffectiveShootTotal`

This keeps the existing threat signal counts (`SignalGoalEnemy`, `SignalCombatNonNone`, `SignalSquadNonNone`, `SignalPressure`, `SignalAnyBots`), mismatch histograms (`MismatchLayerHistogram`, `MismatchReasonHistogram`), `PerceptionTierHistogram`, and `MismatchExemplars` (up to **6** bots, `||`-separated; each segment is `~`-separated: nickname, spawn type, BigBrain layer, G/C/S/P flags, `SL` = SAIN layers active, SAIN `ActiveLayer`, BigBrain custom active, short goal).

### How collapse metrics are produced

- Source per-bot counters are tracked in `SAIN/SAIN/Classes/Bot/Decision/BotDecisionManager.cs`.
- `DecisionSkips*` increments when active squad order short-circuits local decision recomputation.
- `DecisionPreemptions*` increments when direct threat (`IsUnderFire` / human LOS / active human enemy) bypasses squad short-circuit.
- `SquadOrdersReceived*` increments when coordinator sets a squad decision via `SetSquadDecision`.
- Decision CPU uses a hybrid model:
  - **Executed CPU** = measured wall time of executed decision loops (Stopwatch).
  - **Saved CPU** = estimated skipped-loop cost via per-bot EMA decision cost.
- Snapshot CSV writes **deltas since last snapshot** for decision and CPU counters, not lifetime totals.

### Interpretation quick guide

- High `DecisionSkipRatePct` with stable behavior = hierarchy collapse is active and effective.
- Rising `DecisionPreemptionsDelta` during contact spikes is expected (member-local reflexes).
- `DecisionCpuDeltaMs > 0` means estimated decision savings exceeded executed decision cost in that interval.
- `DecisionCpuSavedPerSkipMs` gives average estimated skip value per collapsed local decision tick.
- If `GoalHuman*` visibility remains zero while `VisionRayAttempt*` rises: compare **`VisionRayEffective*`** to **`VisionRayAttempt*`** (gameplay LOS/vision/shoot success). Use **Null/Target/Blocked** for strict first-hit classification — not for “does AI see” alone.
  - low **`VisionRayEffectiveLosTotal`** vs **`VisionRayAttemptLosTotal`** => LOS rarely clear/on-body (geometry, masks, or distance single-part mode).
  - high **`VisionRayEffective*`** but zero **`GoalHumanSainParts*`** => `EnemyParts` / success window / decision path.
  - low **`VisionRayAttempt*`** => scheduling/selection starvation.

---

## SAIN ↔ SAINPerfLog contract (interop)

SAIN must **not** reference `SAINPerfLog.dll` (avoids circular project references). Instead:

1. **SAINPerfLog** exposes a **public static bool** field on the plugin type: **`DiagnosticLoggingEnabled`**, updated every frame from the BepInEx config entry **Diagnostic Logging** (`SAINPerfLog (F12)`).
2. **SAIN** reads it through **`SainPerfLogInterop.IsDiagnosticLoggingEnabled`**, which resolves plugin `me.sol.sain.perflog` via `Chainloader.PluginInfos` and reflects that field by name.

**If you rename or remove that field**, diagnostic gating in SAIN falls back to **off** until SAIN’s interop is updated.

Call sites (representative): `AIFrameBudgetScheduler`, `SAINAILimit`, `BotManagerComponent`, `SquadCombatCoordinator`.

---

## Raid lifecycle hook

Harmony postfix on **`GameWorldUnityTickListener.Create`** (type resolved with `AccessTools.TypeByName` for visibility across references). Adds or reuses **`RaidPerfCsvLogger`** on the tick listener `GameObject` and calls `Initialize(gameWorld)`.

Load order: `[HarmonyAfter("me.sol.sain")]` on the patch class so SAIN’s world/bot wiring exists before sampling.

---

## Build

From repo root (also rebuilds **SAIN** because the project references it):

```bash
dotnet build OptimizedMod/SAINPerfLog/SAINPerfLog.csproj -c Release
```

Artifacts:

- `OptimizedMod/SAIN/SAIN/bin/Release/netstandard2.1/SAIN.dll`
- `OptimizedMod/SAINPerfLog/bin/Release/netstandard2.1/SAINPerfLog.dll`

(post-build copy targets depend on your `SPTRoot` / csproj props.)

---

## Migration from legacy behavior

Older in-SAIN flows used a single **`BepInEx/LogOutput/sain_perf.csv`** path with overwrite/truncation risk across raids. **Do not** expect that fixed path anymore. Use **`LogOutput/sain_perf/`** timestamped files (and optional latest aliases).

---

## Related documentation

| Doc | Role |
|-----|------|
| [INDEX.md](../INDEX.md) | Workspace entry; “Fix a performance issue” task lists F12 location |
| [SAIN_PERFLOG_STANDALONE_PLAN.md](SAIN_PERFLOG_STANDALONE_PLAN.md) | Original plan, mermaid, validation checklist |
| [PROGRESS.md](PROGRESS.md) | Session history + checklist row for perf logging |
| [PERFORMANCE_ARCHITECTURE.md](PERFORMANCE_ARCHITECTURE.md) | Budget scheduler + logging section (SAINPerfLog-oriented) |
| [OPTIMIZED_MOD_README.md](OPTIMIZED_MOD_README.md) | Quick start, F12 section |
| [STATUS_BIGBRAIN_AND_ROGUE.md](STATUS_BIGBRAIN_AND_ROGUE.md) | BigBrain diagnostics vs **SAINPerfLog** toggles (onboarding) |
