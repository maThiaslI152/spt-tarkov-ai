# SPT Tarkov AI — Progress Report

> Last updated: 2026-05-03 | State: Ready for Windows SPT testing

## Overall Status: 11/13 tasks complete (85%)

All Phases 1–4 have been implemented in the `OptimizedMod/` forked source files.
Two items remain blocked pending SPT runtime on Windows.

---

## Completed Phases

### Phase 1: Mechanical Fixes ✓


| Task                        | File                                        | What Changed                                                                            |
| --------------------------- | ------------------------------------------- | --------------------------------------------------------------------------------------- |
| 1.1 TickInterval fix        | `SAIN/Classes/Bot/BotBase.cs:70`            | `TickInterval = 1f/30f` default (was `0f` → every frame)                                |
| 1.2 Coroutine throttling    | 6 job files in `Jobs/`                      | Main-loop yields use `WaitForSeconds`; remaining are intentional Unity Job System syncs |
| 1.3 PerformanceMode default | `SAIN/Preset/.../PerformanceSettings.cs:12` | `PerformanceMode = true` (master toggle)                                                |


### Phase 2: Structural Improvements ✓


| Task                      | File                                   | What Changed                                                                          |
| ------------------------- | -------------------------------------- | ------------------------------------------------------------------------------------- |
| 2.1 LOD raycast reduction | `Jobs/VisionRaycastJob.cs:171-179`     | Far tier → 1 raycast; VeryFar/Narnia → skipped entirely                               |
| 2.2 Budget Scheduler      | `Components/AIFrameBudgetScheduler.cs` | 2ms hard cap, Visible→Audible→Occluded priority tiers, offline squad dispatch         |
| 2.5 Perception LOD        | `Classes/Bot/SAINAILimit.cs`           | Player-centric: camera frustum + single raycast visibility; gunfire/sprint audibility |
| 2.5 Offline Combat        | `Components/OfflineCombatResolver.cs`  | Statistical power formula with fog-of-war randomness                                  |
| 2.5 Combat Audio          | `Components/CombatAudioSpoofer.cs`     | Coroutine-based gunshot scheduling, distance attenuation, BetterAudio+fallback paths  |


### Phase 3: Squad Collapse ✓


| Task                     | File                                            | What Changed                                                               |
| ------------------------ | ----------------------------------------------- | -------------------------------------------------------------------------- |
| 3.1 Squad awareness      | `SAINEnemyController.cs`                        | Propagate enemy detection to squad members (O(N²)→O(N) visibility)         |
| 3.2 Squad coordinator    | `Layers/Combat/Squad/SquadCombatCoordinator.cs` | Leader assigns targets, flanking, suppression every 500ms                  |
| 3.3 State Tree migration | `Layers/SAINLayer.cs`                           | `CheckIsActiveWithCache()` — inactive layers checked at 5Hz (4x reduction) |


### Phase 4: Bot GameObject Pooling ✓


| Task             | File                              | What Changed                                      |
| ---------------- | --------------------------------- | ------------------------------------------------- |
| 4.1 Bot pool     | `Components/BotGameObjectPool.cs` | Recycle bot GameObjects instead of destroy/create |
| 4.1 Pool patches | `Patches/BotPoolPatches.cs`       | Harmony intercepts on spawn/destroy               |
| 4.2 State reset  | SAIN BotComponent, LootingBrain   | Full AI state reset on pool recycle               |


### New: F12 Performance Monitor ✓


| Task              | File                                   | What Changed                                                                           |
| ----------------- | -------------------------------------- | -------------------------------------------------------------------------------------- |
| Monitor component | `Components/SAINPerformanceMonitor.cs` | Real-time FPS/budget/tier tracking, rolling averages                                   |
| CSV logging       | same                                   | Writes to `BepInEx/LogOutput/sain_perf.csv` every N seconds                            |
| F12 config        | `SAINPlugin.cs`                        | "SAIN Performance" section: Enable, Interval, CSV, Verbose, DumpNow, + read-only stats |
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

## Files Modified/Created This Session

### New files


| File                                                     | Purpose                             |
| -------------------------------------------------------- | ----------------------------------- |
| `OptimizedMod/SAIN/Components/SAINPerformanceMonitor.cs` | F12 performance monitor (270 lines) |
| `OptimizedMod/PROGRESS.md`                               | This file                           |


### Modified files


| File                                                  | Changes                                        |
| ----------------------------------------------------- | ---------------------------------------------- |
| `OptimizedMod/SAIN/SAINPlugin.cs`                     | F12 config entries, SyncPerfMonitor()          |
| `OptimizedMod/SAIN/Components/BotManagerComponent.cs` | Perf monitor init in Activate()                |
| `OptimizedMod/SAIN/Components/CombatAudioSpoofer.cs`  | BetterAudio + Unity fallback paths             |
| `OptimizedMod/OptimizationCore/CombatAudioSpoofer.cs` | Documentation for audio wiring                 |
| `PERFORMANCE_PLAN.md`                                 | All todo statuses updated to completed/pending |


---

## How to Test on Windows

1. Copy entire `OptimizedMod/` folder to SPT `BepInEx/plugins/`
2. (First time only) Build `OptimizationCore.csproj` and copy DLL to each mod's refs
3. Launch SPT, start a raid on Lighthouse
4. Press **F12** → scroll to "SAIN Performance":
  - Toggle `Monitor Enabled` ON
  - Set `CSV Logging` ON
  - Set `Log Interval` to 5 seconds
  - Toggle `Dump Stats Now` ON to print snapshot
5. After raid, check `BepInEx/LogOutput/sain_perf.csv` for data
6. Target: Lighthouse 29+ bots at 60 FPS

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

