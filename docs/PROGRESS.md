# SPT Tarkov AI — Progress Report

> Last updated: 2026-05-03 (pre-build doc pass) | State: Single-tree SAIN sources under `OptimizedMod/SAIN/SAIN/`

## Pre-build checklist (2026-05-03)

| Area | Status | Notes |
|------|--------|--------|
| Squad coordinator vs solo combat | Implemented | [`SquadCombatCoordinator`](SAIN/SAIN/Layers/Combat/Squad/SquadCombatCoordinator.cs): skip `SetSquadDecision` when `CurrentCombatDecision != None`. [`CombatSquadLayer`](SAIN/SAIN/Layers/Combat/Squad/CombatSquadLayer.cs): active only when `CurrentCombatDecision == None`. Reduces passive suppress/follow and run-stop jitter from coordinator wiping solo decisions. |
| AI frame budget preset | Implemented | [`PerformanceSettings.MaxAiBudgetMilliseconds`](SAIN/SAIN/Preset/GlobalSettings/Categories/General/PerformanceSettings.cs); [`BotManagerComponent.SyncAiFrameBudgetFromPreset`](SAIN/SAIN/Components/BotManagerComponent.cs) syncs scheduler + perf monitor. Tune **2–6 ms** in preset if needed. |
| Perf CSV interpretation | Documented | Low **`BudgetUtil%`** / **`BudgetExhausted%`** does not rule out **BigBrain priority** issues (e.g. QuestingBots). See [AI_BUDGET_LOD_PLAN.md](AI_BUDGET_LOD_PLAN.md). |
| QuestingBots + combat | Implemented + documented | `SAINExternal.IsBotInCombat` hardened (under-fire recency, QB threat signals, null guards) so `CanBotQuest` blocks better during combat pressure. See [INTEGRATION.md](INTEGRATION.md), [SPTQuestingBots.md](SPTQuestingBots.md), and [BUGFIX-BigBrainPriority-QuestingBots.md](BUGFIX-BigBrainPriority-QuestingBots.md). |
| BigBrain priority diagnostics | Implemented | Minimal `[SAIN DIAG][BigBrain]` arbitration hints added in `BotManagerComponent` (rate-limited, nearby-human filtered, gated by `DiagnosticLogging`) to capture active layer mismatch vs combat signals. See [BUGFIX-BigBrainPriority-QuestingBots.md](BUGFIX-BigBrainPriority-QuestingBots.md). |
| Build | Run before deploy | `dotnet build OptimizedMod/SAIN/SAIN.csproj -c Release` |
| Perf logging quality | Implemented | `SAINPerformanceMonitor` now records **instant exhaustion flag**, **budget headroom**, and **processed/skipped bots** in CSV + verbose log; runtime toggling of CSV now opens/closes writer safely. `SAINPlugin` F12 read-only strings now show headroom, instant exhaustion, and processed/skipped counts. |
| Rogue base-defense coordination (`ExUsec`) | Implemented (build-verified) | Added Rogue-only squad leader election (hysteresis + deterministic tie-break), order TTL/cancel lifecycle, Lighthouse scope guard, and LootingBots anti-loot interop suppression while in base-defense mode. Configurable via `General -> Rogue Base Defense` settings. See [ROGUE_BASE_DEFENSE_PLAN.md](ROGUE_BASE_DEFENSE_PLAN.md). |

---

## Overall Status: 11/13 tasks complete (85%)

All Phases 1–4 have been implemented in the `OptimizedMod/` forked source files.
**Build verified today** — 9 of 10 client mods compile with 0 errors and are deployed to SPT.
Two items remain blocked pending SPT runtime on Windows.

---

## 2026-05-03 Session: Critical Wiring Fix + Full Stack Build

### Problem Found
The inner `SAIN/SAIN/` project (which actually compiles) was missing the budget scheduler and performance monitor wiring. The optimized code existed in root-level files but those files were excluded by the root `SAIN.csproj`'s `<Compile Remove>` directives.

### Fixes Applied
| File | Change |
|---|---|
| `SAIN/SAIN/SAINPlugin.cs` | Added `BotGameObjectPool` init, F12 Performance Monitor config entries (`PerfMonEnabled`, `PerfMonLogInterval`, etc.), `SyncPerfMonitor()` call in `Update()`, `SyncPerfMonitor()` method |
| `SAIN/SAIN/Components/BotManagerComponent.cs` | Added `BudgetScheduler` property, budget scheduler creation, perf monitor init in `Activate()`, replaced manual bot iteration with `BudgetScheduler.ProcessFrame()` |
| `SAIN/SAIN/Components/SAINPerformanceMonitor.cs` | Changed `BudgetLimitMs` setter from `private set` to `set` (public) |

### Build Verification
| Mod | Build Result |
|---|---|
| OptimizationCore | 0 errors, 3 warnings ✓ |
| BigBrain | 0 errors, 0 warnings ✓ |
| Waypoints | 0 errors, 0 warnings ✓ |
| AILimit | 0 errors, 2 warnings ✓ |
| **SAIN** | **0 errors, 9 warnings** ✓ (wired) |
| LootingBots | 0 errors, 0 warnings ✓ |
| ABPS (Client) | 0 errors, 1 warning ✓ |
| MoreBotsAPI Plugin | 0 errors, 3 warnings ✓ |
| MoreBotsAPI Prepatch | 0 errors, 0 warnings ✓ |

All DLLs deployed to `E:\SPT 4.0 Dev\BepInEx\plugins\` via post-build copy.

---

## Completed Phases

### Phase 1: Mechanical Fixes ✓


| Task                        | File                                        | What Changed                                                                            |
| --------------------------- | ------------------------------------------- | --------------------------------------------------------------------------------------- |
| 1.1 TickInterval fix        | `SAIN/SAIN/Classes/Bot/BotBase.cs:70`            | `TickInterval = 1f/30f` default (was `0f` → every frame)                                |
| 1.2 Coroutine throttling    | 6 job files in `Jobs/`                      | Main-loop yields use `WaitForSeconds`; remaining are intentional Unity Job System syncs |
| 1.3 PerformanceMode default | `SAIN/SAIN/Preset/.../PerformanceSettings.cs:12` | `PerformanceMode = true` (master toggle)                                                |


### Phase 2: Structural Improvements ✓


| Task                      | File                                   | What Changed                                                                          |
| ------------------------- | -------------------------------------- | ------------------------------------------------------------------------------------- |
| 2.1 LOD raycast reduction | `SAIN/SAIN/Classes/BotManager/Jobs/VisionRaycastJob.cs:171-179`     | Far tier → 1 raycast; VeryFar/Narnia → skipped entirely                               |
| 2.2 Budget Scheduler      | `SAIN/SAIN/Components/AIFrameBudgetScheduler.cs` | 2ms hard cap, Visible→Audible→Occluded priority tiers. Wired into BotManagerComponent |
| 2.5 Perception LOD        | `SAIN/SAIN/Classes/Bot/SAINAILimit.cs`           | Player-centric: camera frustum + single raycast visibility; gunfire/sprint audibility |
| 2.5 Offline Combat        | `OptimizationCore/OfflineCombatResolver.cs`  | Statistical power formula with fog-of-war randomness                                  |
| 2.5 Combat Audio          | `SAIN/SAIN/Components/CombatAudioSpoofer.cs`     | Coroutine-based gunshot scheduling, distance attenuation, BetterAudio+fallback paths  |


### Phase 3: Squad Collapse ✓


| Task                     | File                                            | What Changed                                                               |
| ------------------------ | ----------------------------------------------- | -------------------------------------------------------------------------- |
| 3.1 Squad awareness      | `SAIN/SAIN/Classes/Bot/EnemyControllers/SAINEnemyController.cs` | Propagate enemy detection to squad members (O(N²)→O(N) visibility)         |
| 3.2 Squad coordinator    | `SAIN/SAIN/Layers/Combat/Squad/SquadCombatCoordinator.cs` + leader hook in `CombatSquadLayer.cs` | Leader assigns targets, flanking, suppression when squad layer active |
| 3.3 State Tree helper    | `SAIN/SAIN/Layers/SAINLayer.cs` + Extract/Solo layers    | `CheckIsActiveWithCache(Func<bool>)` throttles inactive checks (~5Hz); BigBrain arbitration unchanged |


### Phase 4: Bot GameObject Pooling ✓


| Task             | File                              | What Changed                                      |
| ---------------- | --------------------------------- | ------------------------------------------------- |
| 4.1 Bot pool     | `SAIN/SAIN/Components/BotGameObjectPool.cs` | Recycle bot GameObjects instead of destroy/create |
| 4.1 Pool patches | `SAIN/SAIN/Patches/BotPoolPatches.cs`       | Harmony intercepts on spawn/destroy               |
| 4.2 State reset  | SAIN BotComponent, LootingBots/LootingBrain   | Full AI state reset on pool recycle               |


### New: F12 Performance Monitor ✓ (WIRED ✓)


| Task              | File                                   | What Changed                                                                           |
| ----------------- | -------------------------------------- | -------------------------------------------------------------------------------------- |
| Monitor component | `SAIN/SAIN/Components/SAINPerformanceMonitor.cs` | Real-time FPS/budget/tier tracking, rolling averages                                   |
| CSV logging       | same                                   | Writes to `BepInEx/LogOutput/sain_perf.csv` every N seconds                            |
| F12 config        | `SAIN/SAIN/SAINPlugin.cs`              | "SAIN Performance" section: Enable, Interval, CSV, Verbose, DumpNow, + read-only stats |
| Dump snapshots    | same                                   | Toggle "Dump Stats Now" in F12 for full performance snapshot to log                    |


---

## Pending (Blocked on Windows SPT Runtime)


| Task                                    | Why Blocked                                                                                              |
| --------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| **Profiling baseline**                  | Needs actual gameplay with bots on Lighthouse                                                            |
| **CombatAudioSpoofer AudioClip wiring** | Needs EFT internal audio API and asset bundle references — BetterAudio path is coded but untested        |
| **Offline-to-online squad transition**  | Materializing offline squads as real GameObjects when player approaches — complex, needs runtime testing |
| **Phase 4 pool verification**           | Harmony intercepts on `GameObject.Destroy` — must verify no crashes on live SPT                          |
| **Full integration test**               | Lighthouse 29+ bots at 60 FPS target — needs Windows SPT                                                 |


---

## Files Modified This Session (2026-05-03)

### Modified files

| File                                                  | Changes                                        |
| ----------------------------------------------------- | ---------------------------------------------- |
| `SAIN/SAIN/SAINPlugin.cs`                     | Added pool init, F12 perf monitor config entries, SyncPerfMonitor(), `using SAIN.Components` |
| `SAIN/SAIN/Components/BotManagerComponent.cs` | Added BudgetScheduler wiring, perf monitor init, ProcessFrame() replacing manual for-loop |
| `SAIN/SAIN/Components/SAINPerformanceMonitor.cs` | Changed BudgetLimitMs `private set` → `set` |

> **Note:** Legacy duplicate `.cs` trees under `OptimizedMod/SAIN/` (excluding `SAIN/` and `SAINServerMod/`) were **removed in Session 4** — all shipping edits belong under `SAIN/SAIN/` only.

### Session 3 (same day — see section below)

Key paths touched: `LootingBots/LootingBots.cs`, `SAIN/SAIN/Layers/SAINLayer.cs`, `SAIN/SAIN/Classes/Bot/EnemyControllers/SAINEnemyController.cs`, `SAIN/SAIN/Components/AIFrameBudgetScheduler.cs`, `SAIN/SAIN/Classes/Bot/WeaponFunction/SAINShootData.cs`, `SAIN/SAIN/Plugin/BigBrainHandler.cs`, `SAIN/SAIN/Classes/Bot/SAINAILimit.cs`, `SAIN/SAIN/Extensions/BotExtensions.cs`.

---

## How to Test on Windows

1. Build all mods: `dotnet build -c Release` from each project directory (or from `OptimizedMod/SAIN` for SAIN)
2. Verify DLLs are in `E:\SPT 4.0 Dev\BepInEx\plugins\`
3. Launch SPT, start a raid on Lighthouse
4. Press **F12** → scroll to "SAIN Performance":
  - Toggle `Monitor Enabled` ON
  - Set `CSV Logging` ON
  - Set `Log Interval` to 5 seconds
  - Toggle `Dump Stats Now` ON to print snapshot
5. After raid, check `BepInEx/LogOutput/sain_perf.csv` for data
6. Target: Lighthouse 29+ bots at 60 FPS

---

## 2026-05-03 Session: SAINAILimit Audibility Bugfix (Critical AI Behavior Fix)

### Problem Found
All bots exhibited "detect → stop → loop" behavior without shooting. Goons follower Big Pipe completely passive even with nearby gunfire. Root cause: `CheckPlayerCanHearBot()` had all detection logic stubbed as TODOs, always returning `false`. Bots behind cover dropped directly from Visible to Occluded (5Hz navigation-only), freezing combat decisions.

Three bugs identified in `SAINAILimit.cs`:

| Bug | Description | Impact |
|---|---|---|
| 1. `CheckPlayerCanHearBot()` always false | All gunfire/sprint/grenade checks commented out as TODOs | Bots behind cover dropped to Occluded tier — 5Hz combat processing → freeze |
| 2. No group combat awareness | Followers (Big Pipe) didn't know their leader (Knight) was fighting | Big Pipe stayed Occluded even with gunfire nearby |
| 3. `HasActiveEnemy` undefined | Referenced non-existent property on `SAINEnemyController` | AI-vs-AI bots couldn't stay Audible |

### Fixes Applied

| Fix | File | What Changed |
|---|---|---|
| Implement `CheckPlayerCanHearBot()` | `SAINAILimit.cs:219-287` | Active gunfire (SAIN `Shoot.LastShotEnemy` + EFT `ShootData.Shooting`), recent shots (3s window via `_lastShotTime`), sprinting (`Player.IsSprintEnabled` near player), group gunfire (`BotsGroup.Allies` `ShootData.Shooting`) |
| Add `CheckGroupMemberInCombat()` | `SAINAILimit.cs:138-166` | Iterates `BotsGroup.Allies`, checks `ShootData.Shooting` and `Memory.GoalEnemy` |
| Fix `HasActiveEnemy` | `SAINAILimit.cs:131` | Replaced with `Enemies.Count > 0` |
| New `_lastShotTime` field | `SAINAILimit.cs:289-290` | Tracks last shot time for 3-second audibility window |
| Added to `ResetForPoolRecycle()` | `SAINAILimit.cs:338` | Reset `_lastShotTime` on pool recycle |

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

| Fix | File | Before | After |
|---|---|---|---|
| Raise combat squad priority | `LayerSettings.cs` + deployed JSON | 22 | **70** |
| Raise combat solo priority | `LayerSettings.cs` + deployed JSON | 20 | **69** |
| Lower extract priority | `LayerSettings.cs` + deployed JSON | 24 | **65** |
| Add Diagnostic Logging toggle | `SAINPerformanceMonitor.cs`, `SAINPlugin.cs`, `AIFrameBudgetScheduler.cs`, `SAINAILimit.cs` | N/A | F12 config entry + `[SAIN DIAG]` prefixed logs |

### Diagnostic Logging Feature
New `[SAIN DIAG]` entries in `BepInEx/LogOutput.log` when F12 → `5. Diagnostic Logging` is ON:
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
| Symptom | Likely cause | Mitigation in repo |
|--------|----------------|---------------------|
| Bots idle / stuck on quest with LootingBots + SAIN | Loot layer registered at priority **4–5**, losing to BotMind (~50) and most vanilla layers when competing | **LootingBots:** configurable BigBrain priority (default **62**, below SAIN extract ~65 & combat ~69–70) |
| Patrol never resumes after SAIN | `PatrollingData.Pause()` on SAIN activate without matching **Unpause** on deactivate | **SAIN `SAINLayer.cs`:** Unpause when leaving last SAIN layer if active brain layer ≠ `"Looting"` |
| PMC combat flicker / no decision tick | `ChooseEnemy()` cleared EFT `Memory.GoalEnemy` before enemy ingest | **SAIN `SAINEnemyController.cs`:** ingest EFT goal + under-fire enemy before `ClearEnemy()` |
| Audible tier starvation under budget | Scheduler returned early after Visible tier — Audible bots skipped whole frames | **`AIFrameBudgetScheduler.cs`:** time-sliced phases (~45% / ~88% cumulative caps), round-robin within tier, human-goal sort |
| Close-range run–shoot loops | No shots while sprinting; strict `CanShoot` gate | **`SAINShootData.cs`:** allow shoot while sprint vs human ≤18 m; aim when visible within ≤10 m without `CanShoot` |
| Goons / bosses ignored preset priorities | Hardcoded 64/62 / 70/69 in `BigBrainHandler` | Unified with **`LayerSettings`** for squad/solo |
| Full Occlusion near unnamed player | No `KnownEnemies` yet but human close | **`SAINAILimit.cs`:** proximity wake → **Audible** within **40 m** (linear dist via `OtherPlayersData`) |
| `IsBotActive` edge case | `null && botOwner.StandBy` could throw | **`BotExtensions.cs`:** `null \|\|` guard |

### LootingBots — new config
| Setting | Section | Default | Notes |
|---------|---------|--------|--------|
| `BigBrain Loot layer priority` | Compatibility | **62** | Range 40–68 in F12; **new raid** after change. Boot log prints chosen value. |

**DLL:** `OptimizedMod/LootingBots/LootingBots/LootingBots.cs` → `skwizzy.LootingBots.dll` (Release build verified).

### BotMind / quest layer caveat
LootingBots **`LootingLayer.IsActive`** still requires `IsScheduledScan || IsBotLooting`. If neither is true, no loot layer competes — raise priority alone cannot fix **BotMind `GoToLocationLogic` Failed** staying active; update BotMind (e.g. ≥ **1.5.0** per upstream issue #9) and tune quest priorities in BotMind/BigBrain if needed.

---

## 2026-05-03 Session 4: Alignment Phase 2 remainder — duplicate-tree removal + squad/cache hygiene

### Goals
Finish **docs/code alignment** follow-ups: eliminate excluded duplicate sources under `OptimizedMod/SAIN/`, wire **lifecycle + behavioral guards** for the squad coordinator, adopt **`CheckIsActiveWithCache`** safely on hot layers, and sync INDEX / PERFORMANCE_ARCHITECTURE / PROGRESS paths.

### 1. Removed excluded duplicate roots (drift hazard)
Root **`OptimizedMod/SAIN/SAIN.csproj`** uses `<Compile Remove>` for `Classes`, `Components`, `Patches`, `Plugin`, `Preset`, etc. Those folders had mirrored copies alongside **`SAIN/SAIN/`** — confusing agents and risking divergence.

**Action:** Deleted **all** excluded duplicate `.cs` trees under `OptimizedMod/SAIN/` (everything mirrored under `SAIN/SAIN/`). Leftovers differing from inner were removed anyway (**inner tree is authoritative**). Top-level mod folders are now essentially **`SAIN/`**, **`SAINServerMod/`**, **`bin/`**, **`Build/`**, **`obj/`**.

**Docs:** [INDEX.md](INDEX.md) repo map updated to state duplicate roots are gone.

### 2. Squad coordinator lifecycle
| Item | File | Change |
|------|------|--------|
| Clear throttle map on raid teardown | `SAIN/SAIN/Layers/Combat/Squad/SquadCombatCoordinator.cs` | `ResetCoordinationThrottle()` clears `LastCoordinationTime` |
| Hook | `SAIN/SAIN/Components/GameWorldComponent.cs` | Call reset at start of `DestroyComponent()` |

### 3. Squad coordinator vs solo combat (behavioral guard)
| Item | File | Change |
|------|------|--------|
| Skip centralized squad writes during **DogFight** | `SquadCombatCoordinator.cs` | `DistributeTargets` / `AssignFlankingPositions` do not call `SetSquadDecision` on members in `ECombatDecision.DogFight` |

### 4. Layer `IsActive` throttling (Phase 3.3 adoption)
| Item | File | Change |
|------|------|--------|
| Non-recursive cache API | `SAIN/SAIN/Layers/SAINLayer.cs` | `CheckIsActiveWithCache(Func<bool>)`, `ResetIsActiveEvaluationCache()` |
| Extract layer | `SAIN/SAIN/Layers/Extract/ExtractLayer.cs` | Uses lambda for expensive inactive checks |
| Combat solo | `SAIN/SAIN/Layers/Combat/Solo/CombatSoloLayer.cs` | Same; **`IsActiveCheckInterval = 1f/30f`** in ctor for faster pickup |

**Docs:** [docs/PERFORMANCE_ARCHITECTURE.md](PERFORMANCE_ARCHITECTURE.md) §3.3 updated (Func pattern, coordinator reset, DogFight skip).

### 5. Related doc alignment (from earlier same initiative)
- [docs/INTEGRATION.md](INTEGRATION.md), [INDEX.md](INDEX.md): LootingBots **`BigBrainLootLayerPriority` default 62**, numeric priority arbitration (no “SAIN always wins” shorthand).
- [docs/PERFORMANCE_PLAN.md](PERFORMANCE_PLAN.md): Phase 3.2/3.3 vs shipped fork; scheduler pseudocode note (`ProcessTierRoundRobin`).
- [docs/OPTIMIZED_MOD_README.md](OPTIMIZED_MOD_README.md): paths under `SAIN/SAIN/`.

### Build
- **`dotnet build OptimizedMod/SAIN/SAIN.csproj`** — **0 errors** (existing warnings unchanged).

### Still out of scope (unchanged)
- **OptimizationCore** → no `ProjectReference` from SAIN (library remains parallel documentation).
- **Runtime QA:** broader tuning of coordinator authority vs personality remains playtest-driven.

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
