# SPT Tarkov AI — Progress Report

> Last updated: 2026-05-03 | State: Ready for Windows SPT testing

## Overall Status: 11/13 tasks complete (85%)

All Phases 1–4 have been implemented in the `OptimizedMod/` forked source files.
**Build verified today** — all 9 client mods compile with 0 errors and deploy correctly.
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
| 1.3 PerformanceMode default | `SAIN/Preset/.../PerformanceSettings.cs:12` | `PerformanceMode = true` (master toggle)                                                |


### Phase 2: Structural Improvements ✓


| Task                      | File                                   | What Changed                                                                          |
| ------------------------- | -------------------------------------- | ------------------------------------------------------------------------------------- |
| 2.1 LOD raycast reduction | `SAIN/Classes/BotManager/Jobs/VisionRaycastJob.cs:171-179`     | Far tier → 1 raycast; VeryFar/Narnia → skipped entirely                               |
| 2.2 Budget Scheduler      | `SAIN/SAIN/Components/AIFrameBudgetScheduler.cs` | 2ms hard cap, Visible→Audible→Occluded priority tiers. Wired into BotManagerComponent |
| 2.5 Perception LOD        | `SAIN/Classes/Bot/SAINAILimit.cs`           | Player-centric: camera frustum + single raycast visibility; gunfire/sprint audibility |
| 2.5 Offline Combat        | `OptimizationCore/OfflineCombatResolver.cs`  | Statistical power formula with fog-of-war randomness                                  |
| 2.5 Combat Audio          | `SAIN/SAIN/Components/CombatAudioSpoofer.cs`     | Coroutine-based gunshot scheduling, distance attenuation, BetterAudio+fallback paths  |


### Phase 3: Squad Collapse ✓


| Task                     | File                                            | What Changed                                                               |
| ------------------------ | ----------------------------------------------- | -------------------------------------------------------------------------- |
| 3.1 Squad awareness      | `SAIN/Classes/Bot/EnemyControllers/SAINEnemyController.cs` | Propagate enemy detection to squad members (O(N²)→O(N) visibility)         |
| 3.2 Squad coordinator    | `SAIN/SAIN/Layers/Combat/Squad/SquadCombatCoordinator.cs` | Leader assigns targets, flanking, suppression every 500ms                  |
| 3.3 State Tree migration | `SAIN/Layers/SAINLayer.cs`                           | `CheckIsActiveWithCache()` — inactive layers checked at 5Hz (4x reduction) |


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
| `OptimizedMod/SAIN/Components/BotManagerComponent.cs` | Root copy had scheduler; now inner copy matches |
| `OptimizedMod/SAIN/SAINPlugin.cs`                     | Root copy had F12 config; now inner copy matches |

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

Bosses/followers/Goons were NOT affected because they use hardcoded priorities (62-70) instead of the config.

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

## Architecture Quick Reference

```
BudgetScheduler.ProcessFrame(allBots) — 2ms HARD CAP
├── Phase 0: ProcessOfflineSquads()     [statistical combat, zero CPU]
├── Phase 1: ProcessTier(Visible)       [full SAIN AI — always completes]
├── Phase 2: ProcessTier(Audible)       [movement only — budget permitting]
└── Phase 3: ProcessTier(Occluded)      [nav only — round-robin across frames]

PerceptionSystem determines tier:
├── Visible: camera frustum + 1 raycast (cached 0.5s)
├── Audible: gunfire within 500m OR sprinting within 60m (cached 1.0s)
└── Occluded: everything else
```
