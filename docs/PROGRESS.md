# SPT Tarkov AI — Progress Report

> Last updated: 2026-05-06 | State: Single-tree SAIN under `OptimizedMod/SAIN/SAIN/`; raid perf CSV + F12 telemetry live in `**SAINPerfLog**`; SMART **offline slice** in **[SMART_OFFLINE_COMBAT.md](SMART_OFFLINE_COMBAT.md)**; BigBrain + Rogue **scope/status** in **[STATUS_BIGBRAIN_AND_ROGUE.md](STATUS_BIGBRAIN_AND_ROGUE.md)**

**Navigation (agents):** [INDEX.md](../INDEX.md) (full map) · [AGENTS.md](AGENTS.md) (read order, builds, hot paths)

## Pre-build checklist (2026-05-03)


| Area                                                 | Status                       | Notes                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| ---------------------------------------------------- | ---------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Squad coordinator vs solo combat                     | Implemented                  | `[SquadCombatCoordinator](SAIN/SAIN/Layers/Combat/Squad/SquadCombatCoordinator.cs)`: skip `SetSquadDecision` when `CurrentCombatDecision != None`. `[CombatSquadLayer](SAIN/SAIN/Layers/Combat/Squad/CombatSquadLayer.cs)`: active only when `CurrentCombatDecision == None`. Reduces passive suppress/follow and run-stop jitter from coordinator wiping solo decisions.                                                                                                                                                                                                                                                   |
| AI frame budget preset                               | Implemented                  | `[PerformanceSettings.MaxAiBudgetMilliseconds](SAIN/SAIN/Preset/GlobalSettings/Categories/General/PerformanceSettings.cs)`; `[BotManagerComponent.SyncAiFrameBudgetFromPreset](SAIN/SAIN/Components/BotManagerComponent.cs)` syncs `**AIFrameBudgetScheduler`**. Tune **2–6 ms** in preset if needed.                                                                                                                                                                                                                                                                                                                       |
| Perf CSV interpretation                              | Documented                   | Low `**BudgetUtil%`** / `**BudgetExhausted%**` does not rule out **BigBrain priority** issues (e.g. QuestingBots). See [AI_BUDGET_LOD_PLAN.md](AI_BUDGET_LOD_PLAN.md).                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| QuestingBots + combat                                | Implemented + documented     | `SAINExternal.IsBotInCombat` hardened (under-fire recency, QB threat signals, null guards) so `CanBotQuest` blocks better during combat pressure. See [INTEGRATION.md](INTEGRATION.md), [SPTQuestingBots.md](SPTQuestingBots.md), and [BUGFIX-BigBrainPriority-QuestingBots.md](BUGFIX-BigBrainPriority-QuestingBots.md).                                                                                                                                                                                                                                                                                                   |
| BigBrain audit (layer matrix + diagnostics + strips) | Implemented                  | `[BIGBRAIN_LAYER_MATRIX.md](BIGBRAIN_LAYER_MATRIX.md)` inventories registration/strip lists. `[SAIN DIAG][BigBrain]` expanded: `brain=`, `reason=`, `pressure=`, `SAINActiveLayer=`; optional **SAINPerfLog F12 → `3. BigBrain verbose sample`**; mismatch heuristics cover quest/loot/nav/patrol/stationary substrings; no longer requires QuestingBots loaded. `SAINExternal.IsBotUnderCombatPressure` for shared threat predicate. Extra vanilla strip names (`StationaryWS`, …). See [BUGFIX-BigBrainPriority-QuestingBots.md](BUGFIX-BigBrainPriority-QuestingBots.md), [INTEGRATION.md](INTEGRATION.md) restart note. |
| Build                                                | Run before deploy            | `dotnet build OptimizedMod/SAIN/SAIN.csproj -c Release`                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| Perf logging quality                                 | Implemented (standalone)     | `**SAINPerfLog**` owns per-raid CSV under `BepInEx/LogOutput/sain_perf/` (no overwrite), optional BigBrain aggregate snapshots, **F12 read-only scheduler lines**, and the **diagnostic logging** toggle. SAIN exposes no perf/F12 config; spammy traces read the toggle via `SainPerfLogInterop` (`OptimizedMod/SAIN/SAIN/Interop/SainPerfLogInterop.cs`, reflection, no circular reference). **Canonical doc:** [SAIN_PERFLOG.md](SAIN_PERFLOG.md).                                                                                                                                                                       |
| Rogue base-defense coordination (`ExUsec`)           | Implemented (build-verified) | Added Rogue-only squad leader election (hysteresis + deterministic tie-break), order TTL/cancel lifecycle, Lighthouse scope guard, and LootingBots anti-loot interop suppression while in base-defense mode. Configurable via `General -> Rogue Base Defense` settings. See [ROGUE_BASE_DEFENSE_PLAN.md](ROGUE_BASE_DEFENSE_PLAN.md).                                                                                                                                                                                                                                                                                       |
| SMART offline combat (slice)                         | Implemented (code) + doc     | Auto `OfflineSquad` sync, statistical Phase 0, procedural audio; **not** full dematerialize/materialize. See [SMART_OFFLINE_COMBAT.md](SMART_OFFLINE_COMBAT.md), [INDEX.md](../INDEX.md) doc map.                                                                                                                                                                                                                                                                                                                                                                                                                           |
| AILimit ↔ SAIN dematerialization + pool              | Implemented + doc            | Scheduler self-heal, `BotDematerializationController`, AILimit SAIN hooks, `**demat_*` proximity rematerialize**. **Canonical record:** [SAIN_AILIMIT_DEMATERIALIZATION.md](SAIN_AILIMIT_DEMATERIALIZATION.md); symptom deep-dive [BUGFIX-AILimitSAIN-Deadlock.md](BUGFIX-AILimitSAIN-Deadlock.md).                                                                                                                                                                                                                                                                                                                         |


---

## Overall Status: 11/13 tasks complete (85%)

All Phases 1–4 have been implemented in the `OptimizedMod/` forked source files.
**Build verified today** — 9 of 10 client mods compile with 0 errors and are deployed to SPT.
Two items remain blocked pending SPT runtime on Windows.

---

## 2026-05-05 Session: Vision job buffer alignment + blindness/stutter doc

### Delivered

| Area | Notes |
| ---- | ----- |
| **`VisionRaycastJob`** | Native `RaycastCommand` / `RaycastHit` arrays sized to **exact** scheduled command count (`CountScheduledRayCommands` + `TryGetEnemyRaycastSchedule`); skips zero-work batches; DEBUG assert written vs counted; LOS gizmo block null-safe for preset. |
| **Per-raid vision telemetry** | `VisionRaycastJob.ResetDiagnosticsForNewRaid()` from `BotManagerComponent.Activate` so BigBrain `VisionRay*Total` columns are raid-scoped (schema **8** adds `VisionRayEffective*`). |
| **Documentation** | [VISION_BLINDNESS_AND_STUTTER.md](VISION_BLINDNESS_AND_STUTTER.md) (canonical stack write-up); INDEX + AGENTS + SAIN_PERFLOG cross-links; [BUGFIX-VisionRaycastJob-ABRollback.md](BUGFIX-VisionRaycastJob-ABRollback.md) “see also”. |

---

## 2026-05-06 Session: BigBrain schema 8 + vision preset + telemetry semantics

### Delivered

| Area | Notes |
| ---- | ----- |
| **BigBrain CSV schema 8** | `RaidPerfCsvLogger`: `VisionRayEffectiveLosTotal`, `VisionRayEffectiveVisionTotal`, `VisionRayEffectiveShootTotal` (match `RaycastResult.CountsAsGameplaySuccess`). |
| **`VisionRaycastJob`** | Counters + `RaycastResult.CountsAsGameplaySuccess` in `ApplyRaycastAndRecord`; preset-driven **`VisionSinglePartBeyondDistanceMeters`** (50–500 clamp) + **`VisionUseFullPartsForHumanBeyondDistance`**. |
| **`RaycastResult`** | Shared `CountsAsGameplaySuccess` used by `Update` and job telemetry. |
| **Docs / INDEX** | `VISION_BLINDNESS_AND_STUTTER.md` §3.6–3.7; `SAIN_PERFLOG.md` interpretation; `SAIN_FORK_PRESET.md` performance table; `EnemyPartDataClass` XML note (GetRaycast vs batch pairing). |

---

## 2026-05-05 Session: Hierarchical squad preemption + BigBrain priority hardening

### Delivered

| Area | Notes |
| ---- | ----- |
| **Member-level threat preemption** | `BotDecisionManager`: active squad orders no longer hard-block local decisions when the member is under direct threat (`Memory.IsUnderFire`, `HumanEnemyInLineofSight`, `ActiveHumanEnemy`). |
| **Preempt hold window** | Added short local hold window (`~1.5s`) to avoid flip-flop between local reflex and leader-issued squad orders during immediate contact transitions. |
| **Immediate recoordination hook** | `SquadCombatCoordinator.RequestImmediateRecoordination(bot)` forces next squad coordination pass ASAP when a member (not only leader) enters direct combat pressure. |
| **BigBrain priority normalization** | `BigBrainHandler`: runtime validation and normalization of preset priorities to enforce `AvoidThreat(80) > Squad > Solo > Extract`; logs one warning if preset values are invalid and auto-adjusted. |
| **Registration path coverage** | Normalized priorities now applied consistently across all SAIN layer registration paths (PMCs, Scavs, Raiders, Rogues, Bloodhounds, Bosses, Followers, Goons, Others, helper registration methods). |

### Why this matters

Leader-coordinated squad behavior remains the default (hierarchical collapse), but individual members now retain immediate self-preservation reactions when directly attacked. At the same time, squad-level coordination is pulled forward quickly and BigBrain ordering is protected from bad preset values.

---

## 2026-05-05 Session: Decision-collapse telemetry (BotDecisionManager + SAINPerfLog schema v5)

### Delivered

| Area | Notes |
| ---- | ----- |
| **Per-bot collapse counters** | `BotDecisionManager` now tracks cumulative `DecisionTicksTotal`, `DecisionSkipsSquadOrderTotal`, `DecisionPreemptionsTotal`, `SquadOrdersReceivedTotal`, `LastSquadOrderReceived(Time/Decision)`. |
| **Decision CPU accounting** | Added measured executed decision CPU (`DecisionCpuExecutedTotalMs`) + estimated saved CPU from skipped decision loops (`DecisionCpuEstimatedSavedTotalMs`) using per-bot EMA cost. |
| **Snapshot schema upgrade** | `SAINPerfLog/RaidPerfCsvLogger` BigBrain CSV upgraded to **SchemaVersion=5**. |
| **New collapse-impact fields** | Added: `SquadCommandedNowCount`, `SquadCommandUtilNowPct`, `DecisionTicksDelta`, `DecisionSkipsDelta`, `DecisionSkipRatePct`, `DecisionPreemptionsDelta`, `SquadOrdersReceivedDelta`, `DecisionCpuExecutedDeltaMs`, `DecisionCpuSavedDeltaMs`, `DecisionCpuDeltaMs`, `DecisionCpuSavedPerSkipMs`. |
| **Delta semantics** | Decision/cpu telemetry fields are emitted as **delta since previous snapshot** for interval-based analysis; utilization is instant sampled percentage for current snapshot. |

### Why this matters

This turns hierarchical-collapse behavior from a subjective feel into measurable telemetry: we can now quantify how often squad command suppresses local decision work, how often direct-threat preemption fires, and whether collapse is producing net CPU benefit per snapshot interval.

---

## 2026-05-05 Session: BigBrain + Rogue — status doc and index

### Delivered


| Area                   | Notes                                                                                                                                                                                               |
| ---------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Status narrative**   | New **[STATUS_BIGBRAIN_AND_ROGUE.md](STATUS_BIGBRAIN_AND_ROGUE.md)** — problems addressed, features shipped, **Rogue-only vs global vanilla strip** table, build commands, open runtime follow-ups. |
| **Rogue plan clarity** | [ROGUE_BASE_DEFENSE_PLAN.md](ROGUE_BASE_DEFENSE_PLAN.md) already clarifies posture vs BigBrain layer names; status doc links it for discoverability.                                                |
| **INDEX**              | [INDEX.md](../INDEX.md) documentation map row → `STATUS_BIGBRAIN_AND_ROGUE.md`.                                                                                                                     |


---

## 2026-05-04 Session: SMART offline combat — shipped slice + documentation

### Delivered


| Area                    | Notes                                                                                                                                                                                                              |
| ----------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Auto offline squads** | `OfflineSquadWorldSync` registers `auto_*` squads from qualifying occluded AI-vs-AI pairs; `BotManagerComponent` calls sync before `ProcessFrame`; `ResetForNewRaid` on activate.                                  |
| **Casualty trim**       | `AIFrameBudgetScheduler` skips `ApplyOfflineCasualties` member removal for `auto_*` squads.                                                                                                                        |
| **Audio**               | `CombatAudioSpoofer` uses a cached procedural `AudioClip` + `PlayClipAtPoint` (audible without external assets).                                                                                                   |
| **Materialization**     | Initial slice: API + stub return. **Later stack:** `**demat_*`** AILimit remat **shipped** — see [SAIN_AILIMIT_DEMATERIALIZATION.md](SAIN_AILIMIT_DEMATERIALIZATION.md); `**auto_*`** spawn-from-stats still open. |
| **Docs**                | New **[SMART_OFFLINE_COMBAT.md](SMART_OFFLINE_COMBAT.md)** (status vs full SMART, roadmap, observability). **INDEX.md** links this doc.                                                                            |


### Still open (full SMART)

`**auto_*`** stats-only replacement + offline→spawn from `OfflineCombatResult`, EFT-native gunfire audio, and reconciliation with real bot deaths/loot — see **SMART_OFFLINE_COMBAT.md** § “What is not implemented” and § “Future development”. (`**demat_*`** AILimit dematerialize/remat is shipped separately.)

---

## 2026-05-03 Session: Critical Wiring Fix + Full Stack Build

### Problem Found

The inner `SAIN/SAIN/` project (which actually compiles) was missing the budget scheduler and performance monitor wiring. The optimized code existed in root-level files but those files were excluded by the root `SAIN.csproj`'s `<Compile Remove>` directives.

### Fixes Applied


| File                                             | Change                                                                                                                                                                                    |
| ------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `SAIN/SAIN/SAINPlugin.cs`                        | Added `BotGameObjectPool` init, F12 Performance Monitor config entries (`PerfMonEnabled`, `PerfMonLogInterval`, etc.), `SyncPerfMonitor()` call in `Update()`, `SyncPerfMonitor()` method |
| `SAIN/SAIN/Components/BotManagerComponent.cs`    | Added `BudgetScheduler` property, budget scheduler creation, perf monitor init in `Activate()`, replaced manual bot iteration with `BudgetScheduler.ProcessFrame()`                       |
| `SAIN/SAIN/Components/SAINPerformanceMonitor.cs` | Changed `BudgetLimitMs` setter from `private set` to `set` (public)                                                                                                                       |


### Build Verification


| Mod                  | Build Result                       |
| -------------------- | ---------------------------------- |
| OptimizationCore     | 0 errors, 3 warnings ✓             |
| BigBrain             | 0 errors, 0 warnings ✓             |
| Waypoints            | 0 errors, 0 warnings ✓             |
| AILimit              | 0 errors, 2 warnings ✓             |
| **SAIN**             | **0 errors, 9 warnings** ✓ (wired) |
| LootingBots          | 0 errors, 0 warnings ✓             |
| ABPS (Client)        | 0 errors, 1 warning ✓              |
| MoreBotsAPI Plugin   | 0 errors, 3 warnings ✓             |
| MoreBotsAPI Prepatch | 0 errors, 0 warnings ✓             |


All DLLs deployed to `E:\SPT 4.0 Dev\BepInEx\plugins\` via post-build copy.

### Follow-up: SAINPerfLog owns F12 + diagnostics (same stack)


| Area                                             | Change                                                                                                                                                              |
| ------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `SAIN/SAIN/SAINPlugin.cs`                        | Removed **entire** legacy **SAIN Performance** / F12 block (`PerfMon*`, `SyncPerfMonitor`, rolling average).                                                        |
| `SAINPerfLog/PerfLogPlugin.cs`                   | Added `**SAINPerfLog (F12)`** config category: status lines, diagnostic toggle, scheduler readouts; exposes `public static bool DiagnosticLoggingEnabled` for SAIN. |
| `SAIN/SAIN/Interop/SainPerfLogInterop.cs`        | SAIN code gates diagnostics via reflection on that field when `me.sol.sain.perflog` is loaded.                                                                      |
| `SAIN/SAIN/Components/SAINPerformanceMonitor.cs` | **Removed** from shipping SAIN tree (CSV + F12 previously tied here).                                                                                               |


---

## Completed Phases

### Phase 1: Mechanical Fixes ✓


| Task                        | File                                             | What Changed                                                                            |
| --------------------------- | ------------------------------------------------ | --------------------------------------------------------------------------------------- |
| 1.1 TickInterval fix        | `SAIN/SAIN/Classes/Bot/BotBase.cs:70`            | `TickInterval = 1f/30f` default (was `0f` → every frame)                                |
| 1.2 Coroutine throttling    | 6 job files in `Jobs/`                           | Main-loop yields use `WaitForSeconds`; remaining are intentional Unity Job System syncs |
| 1.3 PerformanceMode default | `SAIN/SAIN/Preset/.../PerformanceSettings.cs:12` | `PerformanceMode = true` (master toggle)                                                |


### Phase 2: Structural Improvements ✓


| Task                            | File                                                                        | What Changed                                                                                                                                                            |
| ------------------------------- | --------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2.1 LOD raycast reduction       | `SAIN/SAIN/Classes/BotManager/Jobs/VisionRaycastJob.cs:171-179`             | Far tier → 1 raycast; VeryFar/Narnia → skipped entirely                                                                                                                 |
| 2.2 Budget Scheduler            | `SAIN/SAIN/Components/AIFrameBudgetScheduler.cs`                            | 2ms hard cap, Visible→Audible→Occluded priority tiers. Wired into BotManagerComponent                                                                                   |
| 2.5 Perception LOD              | `SAIN/SAIN/Classes/Bot/SAINAILimit.cs`                                      | Player-centric: camera frustum + single raycast visibility; gunfire/sprint audibility                                                                                   |
| 2.5 Offline Combat              | `SAIN/SAIN/Components/OfflineCombatResolver.cs` (+ ref `OptimizationCore/`) | Statistical power formula with fog-of-war randomness                                                                                                                    |
| 2.5 Offline squad sync          | `SAIN/SAIN/Components/OfflineSquadWorldSync.cs` + `BotManagerComponent`     | **Auto** `RegisterOfflineSquad` from occluded AI-vs-AI pairs (~5 s); `auto_*` squads skip list casualty trim                                                            |
| 2.5 Combat Audio                | `SAIN/SAIN/Components/CombatAudioSpoofer.cs`                                | Coroutine scheduling + distance attenuation; **procedural** `AudioClip` fallback + `PlayClipAtPoint` (BetterAudio TBD)                                                  |
| 2.5 OfflineSquadMaterialization | `SAIN/SAIN/Components/OfflineSquadMaterialization.cs`                       | `**demat_*`** remat path **shipped** (see [SAIN_AILIMIT_DEMATERIALIZATION.md](SAIN_AILIMIT_DEMATERIALIZATION.md)). `**auto_*`** offline→spawn still **not** implemented |


### Phase 3: Squad Collapse ✓


| Task                  | File                                                                                             | What Changed                                                                                          |
| --------------------- | ------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------- |
| 3.1 Squad awareness   | `SAIN/SAIN/Classes/Bot/EnemyControllers/SAINEnemyController.cs`                                  | Propagate enemy detection to squad members (O(N²)→O(N) visibility)                                    |
| 3.2 Squad coordinator | `SAIN/SAIN/Layers/Combat/Squad/SquadCombatCoordinator.cs` + leader hook in `CombatSquadLayer.cs` | Leader assigns targets, flanking, suppression when squad layer active                                 |
| 3.3 State Tree helper | `SAIN/SAIN/Layers/SAINLayer.cs` + Extract/Solo layers                                            | `CheckIsActiveWithCache(Func<bool>)` throttles inactive checks (~5Hz); BigBrain arbitration unchanged |


### Phase 4: Bot GameObject Pooling ✓


| Task             | File                                        | What Changed                                      |
| ---------------- | ------------------------------------------- | ------------------------------------------------- |
| 4.1 Bot pool     | `SAIN/SAIN/Components/BotGameObjectPool.cs` | Recycle bot GameObjects instead of destroy/create |
| 4.1 Pool patches | `SAIN/SAIN/Patches/BotPoolPatches.cs`       | Harmony intercepts on spawn/destroy               |
| 4.2 State reset  | SAIN BotComponent, LootingBots/LootingBrain | Full AI state reset on pool recycle               |


### Raid telemetry + F12 status ✓ (SAINPerfLog)


| Task                 | File                                                   | What Changed                                                                                                                         |
| -------------------- | ------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------ |
| Per-raid perf CSV    | `SAINPerfLog/Components/RaidPerfCsvLogger.cs`          | Appends on interval; flush/close on `GameWorld.OnDispose`; timestamped filenames under `LogOutput/sain_perf/`                        |
| BigBrain snapshots   | same (optional)                                        | Sparse aggregate layer histogram + mismatch count; separate CSV when enabled                                                         |
| F12 readouts         | `SAINPerfLog/PerfLogPlugin.cs`                         | Category `**SAINPerfLog (F12)`**: rolling FPS, `AIFrameBudgetScheduler` budget/bots, active CSV paths; **Diagnostic Logging** toggle |
| SAIN diagnostic gate | `SAIN/SAIN/Interop/SainPerfLogInterop.cs` + call sites | No SAIN F12 perf UI; diagnostics off unless SAINPerfLog installed and toggle on                                                      |


---

## Pending (Blocked on Windows SPT Runtime)


| Task                                             | Why Blocked                                                                                                                                                                                                                                                                   |
| ------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Profiling baseline**                           | Needs actual gameplay with bots on Lighthouse                                                                                                                                                                                                                                 |
| **CombatAudioSpoofer — EFT / BetterAudio**       | Procedural Unity fallback is **in code**; **BetterAudio** + real weapon clips still need version-correct API binding and SPT raid validation.                                                                                                                                 |
| **Offline-to-online (`auto_*`)**                 | No spawn/state handoff from statistical `auto_*` fights. `**demat_*`** AILimit proximity remat **shipped** in `OfflineSquadMaterialization` — see [SMART_OFFLINE_COMBAT.md](SMART_OFFLINE_COMBAT.md), [SAIN_AILIMIT_DEMATERIALIZATION.md](SAIN_AILIMIT_DEMATERIALIZATION.md). |
| **SMART full stack (dematerialize + reconcile)** | Far fights still use full AI on spawned bots; no stats-only replacement, no corpse/loot sync from offline rolls — roadmap in [SMART_OFFLINE_COMBAT.md](SMART_OFFLINE_COMBAT.md).                                                                                              |
| **Phase 4 pool verification**                    | Harmony intercepts on `GameObject.Destroy` — must verify no crashes on live SPT                                                                                                                                                                                               |
| **Full integration test**                        | Lighthouse 29+ bots at 60 FPS target — needs Windows SPT                                                                                                                                                                                                                      |


---

## Files Modified This Session (2026-05-03)

### Modified files


| File                                             | Changes                                                                                                                    |
| ------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------- |
| `SAIN/SAIN/SAINPlugin.cs`                        | Session wiring: pool init, etc. (**F12 perf block later removed** — see follow-up above).                                  |
| `SAIN/SAIN/Components/BotManagerComponent.cs`    | BudgetScheduler wiring, `ProcessFrame()` replacing manual for-loop (perf monitor init **removed** with standalone logger). |
| `SAIN/SAIN/Components/SAINPerformanceMonitor.cs` | **Removed** from tree after SAINPerfLog migration.                                                                         |


> **Note:** Legacy duplicate `.cs` trees under `OptimizedMod/SAIN/` (excluding `SAIN/` and `SAINServerMod/`) were **removed in Session 4** — all shipping edits belong under `SAIN/SAIN/` only.

### Session 3 (same day — see section below)

Key paths touched: `LootingBots/LootingBots.cs`, `SAIN/SAIN/Layers/SAINLayer.cs`, `SAIN/SAIN/Classes/Bot/EnemyControllers/SAINEnemyController.cs`, `SAIN/SAIN/Components/AIFrameBudgetScheduler.cs`, `SAIN/SAIN/Classes/Bot/WeaponFunction/SAINShootData.cs`, `SAIN/SAIN/Plugin/BigBrainHandler.cs`, `SAIN/SAIN/Classes/Bot/SAINAILimit.cs`, `SAIN/SAIN/Extensions/BotExtensions.cs`.

---

## How to Test on Windows

1. Build all mods: `dotnet build -c Release` from each project directory (or from `OptimizedMod/SAIN` for SAIN)
2. Verify DLLs are in `E:\SPT 4.0 Dev\BepInEx\plugins\`
3. Launch SPT, start a raid on Lighthouse
4. Press **F12** → open `**SAINPerfLog`** → `**SAINPerfLog (F12)**`:
  - Leave **F12 Status Lines** ON to refresh FPS / scheduler / bot readouts
  - Turn **Diagnostic Logging** ON only when chasing `[SAIN DIAG]` spam in `LogOutput.log`
5. After raid, inspect `**BepInEx/LogOutput/sain_perf/`** (timestamped `sain_perf_*.csv`; optional `*_latest.csv` if enabled)
6. Target: Lighthouse 29+ bots at 60 FPS

---

## 2026-05-03 Session: SAINAILimit Audibility Bugfix (Critical AI Behavior Fix)

### Problem Found

All bots exhibited "detect → stop → loop" behavior without shooting. Goons follower Big Pipe completely passive even with nearby gunfire. Root cause: `CheckPlayerCanHearBot()` had all detection logic stubbed as TODOs, always returning `false`. Bots behind cover dropped directly from Visible to Occluded (5Hz navigation-only), freezing combat decisions.

Three bugs identified in `SAINAILimit.cs`:


| Bug                                       | Description                                                         | Impact                                                                      |
| ----------------------------------------- | ------------------------------------------------------------------- | --------------------------------------------------------------------------- |
| 1. `CheckPlayerCanHearBot()` always false | All gunfire/sprint/grenade checks commented out as TODOs            | Bots behind cover dropped to Occluded tier — 5Hz combat processing → freeze |
| 2. No group combat awareness              | Followers (Big Pipe) didn't know their leader (Knight) was fighting | Big Pipe stayed Occluded even with gunfire nearby                           |
| 3. `HasActiveEnemy` undefined             | Referenced non-existent property on `SAINEnemyController`           | AI-vs-AI bots couldn't stay Audible                                         |


### Fixes Applied


| Fix                                 | File                     | What Changed                                                                                                                                                                                                                    |
| ----------------------------------- | ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Implement `CheckPlayerCanHearBot()` | `SAINAILimit.cs:219-287` | Active gunfire (SAIN `Shoot.LastShotEnemy` + EFT `ShootData.Shooting`), recent shots (3s window via `_lastShotTime`), sprinting (`Player.IsSprintEnabled` near player), group gunfire (`BotsGroup.Allies` `ShootData.Shooting`) |
| Add `CheckGroupMemberInCombat()`    | `SAINAILimit.cs:138-166` | Iterates `BotsGroup.Allies`, checks `ShootData.Shooting` and `Memory.GoalEnemy`                                                                                                                                                 |
| Fix `HasActiveEnemy`                | `SAINAILimit.cs:131`     | Replaced with `Enemies.Count > 0`                                                                                                                                                                                               |
| New `_lastShotTime` field           | `SAINAILimit.cs:289-290` | Tracks last shot time for 3-second audibility window                                                                                                                                                                            |
| Added to `ResetForPoolRecycle()`    | `SAINAILimit.cs:338`     | Reset `_lastShotTime` on pool recycle                                                                                                                                                                                           |


### Build & Deploy

- **SAIN.dll** built with 0 errors, 8 warnings
- Deployed to `E:\SPT 4.0 Dev\BepInEx\plugins\SAIN\SAIN.dll`
- Full documentation: **[docs/BUGFIX-SAINAILimit-Audibility.md](BUGFIX-SAINAILimit-Audibility.md)**

---

## 2026-05-03 Session 2: Combat Layer Priority Fix + Diagnostic Logging

### Problem Found

After fixing the audibility bug, bots still refused to fight. They remained stuck in patrol/follow/questing mode even when shot at point blank. BotMind's `MedicBuddyShooterLayer`/`MedicBuddyMedicLayer` appeared active on every bot in logs.

### Root Cause

SAIN's combat layer priorities for regular bots (PMCs, Scavs, Rogues) were **20-22** — far below BotMind's layers (estimated 25-50). BigBrain's layer system selects the highest-priority active layer, so BotMind's patrol/medic layers permanently blocked SAIN's combat layers from activating.

**Correction:** `BigBrainHandler.cs` registers **boss, follower, and goon** brains with the same `LayerSettings` combat priorities as PMCs/scavs (Goons were previously hardcoded **64/62**, below SAIN extract and many third-party layers).

### Fixes Applied


| Fix                           | File                                                                                                                        | Before | After                                                                               |
| ----------------------------- | --------------------------------------------------------------------------------------------------------------------------- | ------ | ----------------------------------------------------------------------------------- |
| Raise combat squad priority   | `LayerSettings.cs` + deployed JSON                                                                                          | 22     | **70**                                                                              |
| Raise combat solo priority    | `LayerSettings.cs` + deployed JSON                                                                                          | 20     | **69**                                                                              |
| Lower extract priority        | `LayerSettings.cs` + deployed JSON                                                                                          | 24     | **65**                                                                              |
| Add Diagnostic Logging toggle | `SAINPerfLog/PerfLogPlugin.cs`, `SAIN/SAIN/Interop/SainPerfLogInterop.cs`, `AIFrameBudgetScheduler.cs`, `SAINAILimit.cs`, … | N/A    | **SAINPerfLog (F12)** toggle + `[SAIN DIAG]` prefixed logs (SAIN gates via interop) |


### Diagnostic Logging Feature

New `[SAIN DIAG]` entries in `BepInEx/LogOutput.log` when **F12 → SAINPerfLog → `SAINPerfLog (F12)` → `2. Diagnostic Logging`** is ON:

- **Tier changes:** `TierChange: Bot[Scav] Occluded → Visible (ActiveEnemy=True)`
- **Budget status:** `OK — 1.23ms / 2ms` or `BUDGET EXHAUSTED at 2.01ms`
- **Bot distribution:** `V=3 A=5 O=12 (processed=8, skipped=12)`
- **Offline combat:** `OfflineCombat: PMC(4) vs Scav(3) @ 250m | Winner=PMC, KIA: A=1 B=2`

### Build & Deploy

- **SAIN.dll** built with 0 errors, 8 warnings
- Deployed to `E:\SPT 4.0 Dev\BepInEx\plugins\SAIN\SAIN.dll` + config JSON updated
- Full documentation:
  - **[docs/BUGFIX-SAINLayerPriority.md](BUGFIX-SAINLayerPriority.md)** — priority conflict fix
  - **[docs/BUGFIX-SAINAILimit-Audibility.md](BUGFIX-SAINAILimit-Audibility.md)** — audibility detection fix (Session 1)

---

## 2026-05-03 Session 3: LootingBots × SAIN × BigBrain stack + combat polish

### Problems addressed


| Symptom                                            | Likely cause                                                                                              | Mitigation in repo                                                                                                          |
| -------------------------------------------------- | --------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| Bots idle / stuck on quest with LootingBots + SAIN | Loot layer registered at priority **4–5**, losing to BotMind (~50) and most vanilla layers when competing | **LootingBots:** configurable BigBrain priority (default **62**, below SAIN extract ~65 & combat ~69–70)                    |
| Patrol never resumes after SAIN                    | `PatrollingData.Pause()` on SAIN activate without matching **Unpause** on deactivate                      | **SAIN `SAINLayer.cs`:** Unpause when leaving last SAIN layer if active brain layer ≠ `"Looting"`                           |
| PMC combat flicker / no decision tick              | `ChooseEnemy()` cleared EFT `Memory.GoalEnemy` before enemy ingest                                        | **SAIN `SAINEnemyController.cs`:** ingest EFT goal + under-fire enemy before `ClearEnemy()`                                 |
| Audible tier starvation under budget               | Scheduler returned early after Visible tier — Audible bots skipped whole frames                           | `**AIFrameBudgetScheduler.cs`:** time-sliced phases (~45% / ~88% cumulative caps), round-robin within tier, human-goal sort |
| Close-range run–shoot loops                        | No shots while sprinting; strict `CanShoot` gate                                                          | `**SAINShootData.cs`:** allow shoot while sprint vs human ≤18 m; aim when visible within ≤10 m without `CanShoot`           |
| Goons / bosses ignored preset priorities           | Hardcoded 64/62 / 70/69 in `BigBrainHandler`                                                              | Unified with `**LayerSettings`** for squad/solo                                                                             |
| Full Occlusion near unnamed player                 | No `KnownEnemies` yet but human close                                                                     | `**SAINAILimit.cs`:** proximity wake → **Audible** within **40 m** (linear dist via `OtherPlayersData`)                     |
| `IsBotActive` edge case                            | `null && botOwner.StandBy` could throw                                                                    | `**BotExtensions.cs`:** `null ||` guard                                                                                     |


### LootingBots — new config


| Setting                        | Section       | Default | Notes                                                                        |
| ------------------------------ | ------------- | ------- | ---------------------------------------------------------------------------- |
| `BigBrain Loot layer priority` | Compatibility | **62**  | Range 40–68 in F12; **new raid** after change. Boot log prints chosen value. |


**DLL:** `OptimizedMod/LootingBots/LootingBots/LootingBots.cs` → `skwizzy.LootingBots.dll` (Release build verified).

### BotMind / quest layer caveat

LootingBots `**LootingLayer.IsActive`** still requires `IsScheduledScan || IsBotLooting`. If neither is true, no loot layer competes — raise priority alone cannot fix **BotMind `GoToLocationLogic` Failed** staying active; update BotMind (e.g. ≥ **1.5.0** per upstream issue #9) and tune quest priorities in BotMind/BigBrain if needed.

---

## 2026-05-03 Session 4: Alignment Phase 2 remainder — duplicate-tree removal + squad/cache hygiene

### Goals

Finish **docs/code alignment** follow-ups: eliminate excluded duplicate sources under `OptimizedMod/SAIN/`, wire **lifecycle + behavioral guards** for the squad coordinator, adopt `**CheckIsActiveWithCache`** safely on hot layers, and sync INDEX / PERFORMANCE_ARCHITECTURE / PROGRESS paths.

### 1. Removed excluded duplicate roots (drift hazard)

Root `**OptimizedMod/SAIN/SAIN.csproj**` uses `<Compile Remove>` for `Classes`, `Components`, `Patches`, `Plugin`, `Preset`, etc. Those folders had mirrored copies alongside `**SAIN/SAIN/**` — confusing agents and risking divergence.

**Action:** Deleted **all** excluded duplicate `.cs` trees under `OptimizedMod/SAIN/` (everything mirrored under `SAIN/SAIN/`). Leftovers differing from inner were removed anyway (**inner tree is authoritative**). Top-level mod folders are now essentially `**SAIN/`**, `**SAINServerMod/**`, `**bin/**`, `**Build/**`, `**obj/**`.

**Docs:** [INDEX.md](INDEX.md) repo map updated to state duplicate roots are gone.

### 2. Squad coordinator lifecycle


| Item                                | File                                                      | Change                                                      |
| ----------------------------------- | --------------------------------------------------------- | ----------------------------------------------------------- |
| Clear throttle map on raid teardown | `SAIN/SAIN/Layers/Combat/Squad/SquadCombatCoordinator.cs` | `ResetCoordinationThrottle()` clears `LastCoordinationTime` |
| Hook                                | `SAIN/SAIN/Components/GameWorldComponent.cs`              | Call reset at start of `DestroyComponent()`                 |


### 3. Squad coordinator vs solo combat (behavioral guard)


| Item                                              | File                        | Change                                                                                                                  |
| ------------------------------------------------- | --------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| Skip centralized squad writes during **DogFight** | `SquadCombatCoordinator.cs` | `DistributeTargets` / `AssignFlankingPositions` do not call `SetSquadDecision` on members in `ECombatDecision.DogFight` |


### 4. Layer `IsActive` throttling (Phase 3.3 adoption)


| Item                    | File                                              | Change                                                                 |
| ----------------------- | ------------------------------------------------- | ---------------------------------------------------------------------- |
| Non-recursive cache API | `SAIN/SAIN/Layers/SAINLayer.cs`                   | `CheckIsActiveWithCache(Func<bool>)`, `ResetIsActiveEvaluationCache()` |
| Extract layer           | `SAIN/SAIN/Layers/Extract/ExtractLayer.cs`        | Uses lambda for expensive inactive checks                              |
| Combat solo             | `SAIN/SAIN/Layers/Combat/Solo/CombatSoloLayer.cs` | Same; `**IsActiveCheckInterval = 1f/30f`** in ctor for faster pickup   |


**Docs:** [docs/PERFORMANCE_ARCHITECTURE.md](PERFORMANCE_ARCHITECTURE.md) §3.3 updated (Func pattern, coordinator reset, DogFight skip).

### 5. Related doc alignment (from earlier same initiative)

- [docs/INTEGRATION.md](INTEGRATION.md), [INDEX.md](INDEX.md): LootingBots `**BigBrainLootLayerPriority` default 62**, numeric priority arbitration (no “SAIN always wins” shorthand).
- [docs/PERFORMANCE_PLAN.md](PERFORMANCE_PLAN.md): Phase 3.2/3.3 vs shipped fork; scheduler pseudocode note (`ProcessTierRoundRobin`).
- [docs/OPTIMIZED_MOD_README.md](OPTIMIZED_MOD_README.md): paths under `SAIN/SAIN/`.

### Build

- `**dotnet build OptimizedMod/SAIN/SAIN.csproj`** — **0 errors** (existing warnings unchanged).

### Still out of scope (unchanged)

- **OptimizationCore** → no `ProjectReference` from SAIN (library remains parallel documentation).
- **Runtime QA:** broader tuning of coordinator authority vs personality remains playtest-driven.

---

## 2026-05-04 Session: SAINPerfLog schema v4 (distance + engagement telemetry)

### Delivered


| Area                            | Notes                                                                                                                                                                                                                                  |
| ------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Distance-aware BigBrain CSV** | `SAINPerfLog/Components/RaidPerfCsvLogger.cs`: `SchemaVersion` bumped to **4**; added `DistNearCount`, `DistMidCount`, `DistFarCount` using main-player distance bands (`<30m`, `30-80m`, `>=80m`).                                    |
| **Engagement-at-distance**      | Added `EngagedNear/Mid/Far` counters where engagement is `GoalEnemy` OR `CombatDecision!=None` OR `SAINExternal.IsBotUnderCombatPressure`.                                                                                             |
| **ExUsec-specific engagement**  | Added `ExUsecEngagedNear/Mid/Far` to track Rogue behavior by distance.                                                                                                                                                                 |
| **Can-shoot-now at distance**   | Added `CanShootNowNear/Mid/Far` (`GoalEnemy.IsVisible && GoalEnemy.CanShoot`) to detect whether bots in each distance bucket have immediate firing opportunities.                                                                      |
| **Docs + index**                | Updated [SAIN_PERFLOG.md](SAIN_PERFLOG.md) for schema v4 and added dedicated change record [BUGFIX-SAINPerfLog-DistanceEngagementTelemetry.md](BUGFIX-SAINPerfLog-DistanceEngagementTelemetry.md); indexed in [INDEX.md](../INDEX.md). |
| **Build/deploy**                | Built `OptimizedMod/SAINPerfLog/SAINPerfLog.csproj` (Release) and deployed to `E:\SPT 4.0 Dev\BepInEx\plugins\SAINPerfLog\SAINPerfLog.dll`.                                                                                            |


---

## 2026-05-04 Session: VisionRaycastJob A/B rollback + source backup

### Delivered

| Area | Notes |
| ---- | ----- |
| **Controlled A/B rollback** | `SAIN/SAIN/Classes/BotManager/Jobs/VisionRaycastJob.cs`: reverted timing cadence to per-frame (`yield return null`) for both `EnemyVisionJob` post-schedule wait and `UpdateEFTVision` loop, matching original behavior for isolation testing. |
| **Source backup snapshot** | Pre-change backup created at `E:\spt-tarkov-ai\backups\vision_ab_20260504_212844` with `VisionRaycastJob.cs` and `RaidPerfCsvLogger.cs`. |
| **Debug process documentation** | Added [BUGFIX-VisionRaycastJob-ABRollback.md](BUGFIX-VisionRaycastJob-ABRollback.md) with trigger evidence (`GoalHuman*` counters all zero), exact rollback diff, test protocol, and pass/fail interpretation. |
| **Index updates** | `INDEX.md` updated with the new rollback doc and quick-reference entry for vision A/B workflow. |

### Why this matters

This creates a reproducible, reversible test step to prove or reject vision timing cadence as a root contributor before adding deeper raycast-internal instrumentation.

---

## 2026-05-04 Session: Vision diagnostics schema v7 + combat-pressure looting gate

### Delivered

| Area | Notes |
| ---- | ----- |
| **Vision raycast internals telemetry** | `SAIN/SAIN/Classes/BotManager/Jobs/VisionRaycastJob.cs`: added cumulative channel counters for LOS/Vision/Shoot outcomes (attempt, null hit, target hit, blocked hit) and exported snapshot accessor. |
| **BigBrain CSV schema upgrade** | `SAINPerfLog/Components/RaidPerfCsvLogger.cs`: `SchemaVersion` bumped to **7** and added `VisionRay*Total` columns so we can distinguish scheduling starvation vs obstruction vs synthesis failure. |
| **Combat-pressure anti-loot gate** | `LootingBots/LootingBots/LootingLayer.cs`: `IsActive()` now returns false when SAIN reports combat pressure (`SAINExternal.IsBotUnderCombatPressure` via reflection), preventing Looting layer takeover during engagement windows. |
| **Build + deploy** | Rebuilt and deployed `SAIN.dll`, `skwizzy.LootingBots.dll`, and `SAINPerfLog.dll` to runtime. |

### Why this matters

The previous A/B rollback proved timing alone did not recover visual acquisition. Schema v7 now provides low-level ray outcome evidence, while the looting gate reduces arbitration noise so the next run isolates the vision failure path cleanly.

---

## 2026-05-04 Session: Factory schema v7 validation + vision clamp hardening

### Delivered

| Area | Notes |
| ---- | ----- |
| **Factory run analyzed** | Reviewed `sain_bigbrain_20260504_152218_factory4_day_5509df05.csv` (SchemaVersion=7). |
| **Observed datapoints** | Bots showed sustained goal/combat/pressure activity and mostly near/mid distance presence, but `GoalHumanSainParts*` and `GoalHumanFinal*` stayed zero throughout. |
| **Critical diagnosis signal** | All `VisionRayAttempt*` counters remained zero, indicating ray-attempt stage degeneration/starvation rather than only downstream visibility arbitration. |
| **Hardening fix shipped** | `VisionRaycastJob.cs`: clamped `VisionRaycastFrequency >= 1`, `LookUpdateFrequency >= 1`, and `MaxRaycastsPerEnemy` to `1..3` to prevent runtime zero/invalid scheduling values. |
| **Build/deploy status** | Rebuilt and deployed after the clamp fix. |

### Expected next validation signal

Next controlled run should show `VisionRayAttempt*Total` advancing above zero. If attempts recover, `GoalHumanSainParts*` / `GoalHumanFinal*` should begin moving from zero; if attempts stay zero, investigation should continue at runtime config binding/assignment points before job execution.

---

## Architecture Quick Reference

```
BudgetScheduler.ProcessFrame(allBots) — 2ms HARD CAP (tier time-sliced + round-robin)
├── Phase 0: ResolveOfflineSquadCombat()  [≤1 Hz, statistical]
├── Visible tier:   process until ~45% of frame budget (rotate start index)
├── Audible tier:   process until ~88% cumulative (rotate start index)
└── Occluded tier:  remainder budget (rotate start index); closest human GoalEnemy sorted first per tier

PerceptionSystem determines tier:
├── Visible: camera frustum + 1 raycast (cached 0.5s)
├── Audible: gunfire within 500m OR sprinting within 60m (cached 1.0s)
└── Occluded: everything else
```

