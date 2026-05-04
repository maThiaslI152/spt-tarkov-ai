# OptimizedMod — SPT Tarkov AI Performance Overhaul

Performance-optimized forks of 6 SPT AI mods + MoreBotsAPI + new **OptimizationCore** shared library.
Target: **Lighthouse with 29+ bots at stable 60 FPS.**

## Quick Start (Windows SPT)

1. Copy `OptimizedMod/` to SPT `BepInEx/plugins/`
2. **SAIN preset:** this fork’s **SAIN** builds a customized bootstrap preset **`Optimized (Harder PMCs)`** under `BepInEx/plugins/SAIN/Presets/` after you run the game with our `SAIN.dll` (see [SAIN_FORK_PRESET.md](SAIN_FORK_PRESET.md)). Replace the plugin’s `SAIN.dll` with the one from `dotnet build OptimizedMod/SAIN/SAIN.csproj -c Release` if the folder never appears.
3. Launch SPT, start raid
4. Press **F12** → **SAINPerfLog (F12)** for live scheduler stats + diagnostics toggles
5. Check `BepInEx/LogOutput/sain_perf/` after raid (timestamped per-raid CSVs; optional `*_latest.csv` aliases)

## What's Inside

### Forked Mods (7)
| Mod | Optimizations |
|---|---|
| **SAIN** | Budget scheduler, perception-tiered AI LOD, squad coordinator, State Tree layer eval, bot pool; fork bootstrap preset + **what each preset parameter does to NPCs** — [SAIN_FORK_PRESET.md](SAIN_FORK_PRESET.md) |
| **BigBrain** | State-tree migration (4x faster layer arbitration) |
| **LootingBots** | Pool recycle state reset |
| **Waypoints** | Path cache for recycled bots |
| **AILimit** | SAIN **dematerialize/rematerialize** + pool (soft dep); LINQ alloc fix — [SAIN_AILIMIT_DEMATERIALIZATION.md](SAIN_AILIMIT_DEMATERIALIZATION.md) |
| **ABPS** | Pool interceptor in spawn pipeline |
| **MoreBotsAPI** | Pool integration, per-map config |

### OptimizationCore (New — 8 source files)
| Component | Role |
|---|---|
| `AIFrameBudgetScheduler` | 2ms hard cap, Visible→Audible→Occluded priority processing |
| `PerceptionSystem` | Player-centric visibility (camera frustum + raycast) and audibility checks |
| `OfflineCombatResolver` | Statistical AI-vs-AI combat using bot equipment/level stats |
| `CombatAudioSpoofer` | Fake gunfire audio with distance attenuation for offline battles |
| `PerceptionTier` | Enum: Visible, Audible, Occluded |
| `IBudgetedAI` / `IOfflineSquad` | Interfaces for scheduler integration |
| `OfflineCombatTypes` | Data types: OfflineBotStats, OfflineCombatResult |

## Architecture

```
Each 16.7ms Frame (60 FPS target):
├── Rendering: ~8ms
├── Physics: ~2ms
├── AI Budget: MAX 2ms (guaranteed hard cap)
│   ├── Visible bots: full SAIN AI (always completes)
│   ├── Audible bots: movement only (budget permitting)
│   └── Occluded bots: navigation only (round-robin across frames)
└── Other: ~4.7ms
```

### Design Philosophy: "Fake It When Unseen"

| Tier | What's Real | What's Faked | CPU/bot |
|---|---|---|---|
| **Visible** | Vision, cover, tactics, shooting | Nothing | ~0.5ms |
| **Audible** | Position, movement, basic reactions | Vision, cover, tactical decisions | ~0.1ms |
| **Occluded** | Patrol path navigation | Everything else | ~0.02ms |

## F12 status + raid telemetry (SAINPerfLog)

Press F12 in-game → **SAINPerfLog** plugin → **SAINPerfLog (F12)**:

| Config | Description |
|---|---|
| `F12 Status Lines` | Master toggle for read-only FPS / scheduler / bot lines |
| `Diagnostic Logging` | Spammy BepInEx traces (tier changes, budget exhaustion, offline combat) |
| `BigBrain verbose sample` | When Diagnostic Logging is on: periodic **Info** lines for every human-proximate bot’s active BigBrain layer (not only mismatches) |
| `FPS / Frame Time` | Read-only rolling average |
| `AI Budget` | Read-only scheduler utilization + exhaustion counters |
| `Bot / Tier` | Read-only processed/skipped + V/A/O/offline squad counts |
| `Active CSV Paths` | Read-only paths for the current raid perf / BigBrain snapshot writers |

Raid CSV rows are written by **SAINPerfLog** on its own interval (see **SAINPerfLog** category), not from SAIN.

## Build Instructions

```bash
# OptimizationCore (shared library)
cd OptimizedMod/OptimizationCore
dotnet build -c Release

# Individual mods (each has its own .csproj)
cd OptimizedMod/SAIN
dotnet build -c Release
```

Projects target `.NET Standard 2.1` with BepInEx.Core 5.x and UnityEngine.Modules references.

## Files Inventory

### New files (this project)
```
OptimizedMod/
├── PROGRESS.md
├── OptimizationCore/
│   ├── AIFrameBudgetScheduler.cs
│   ├── PerceptionSystem.cs
│   ├── OfflineCombatResolver.cs
│   ├── CombatAudioSpoofer.cs
│   ├── PerceptionTier.cs
│   ├── IBudgetedAI.cs
│   ├── IOfflineSquad.cs
│   ├── OfflineCombatTypes.cs
│   └── OptimizationCore.csproj
├── SAIN/SAIN/Components/
│   ├── AIFrameBudgetScheduler.cs
│   ├── OfflineCombatResolver.cs
│   ├── CombatAudioSpoofer.cs
│   ├── BotGameObjectPool.cs
│   └── BotComponent.cs (modified)
├── SAINPerfLog/Components/
│   └── RaidPerfCsvLogger.cs          ← per-raid perf CSV (+ optional BigBrain snapshot CSV)
├── SAIN/SAIN/Layers/Combat/Squad/
│   ├── SquadCombatCoordinator.cs
│   └── CombatSquadLayer.cs (leader calls CoordinateSquad)
├── SAIN/SAIN/Patches/
│   └── BotPoolPatches.cs
```

### Modified files (key changes)
- `SAIN/SAIN/Classes/Bot/BotBase.cs` — TickInterval = 1f/30f
- `SAIN/SAIN/Classes/Bot/SAINAILimit.cs` — PerceptionTier, visibility/audibility
- `SAIN/SAIN/Classes/BotManager/Jobs/VisionRaycastJob.cs` — LOD raycast reduction
- `SAIN/SAIN/Classes/Bot/EnemyControllers/SAINEnemyController.cs` — Squad propagation
- `SAIN/SAIN/Classes/BotManager/Squad.cs` — Squad awareness
- `SAIN/SAIN/Layers/SAINLayer.cs` — optional CheckIsActiveWithCache helper
- `SAIN/SAIN/Layers/Combat/Squad/CombatSquadLayer.cs` — squad coordinator integration (leader)
- `SAIN/SAIN/SAINPlugin.cs` — Pool init (perf/F12 lives in **SAINPerfLog**)
- `SAIN/SAIN/Components/BotManagerComponent.cs` — Budget scheduler wiring
- `SAINPerfLog/PerfLogPlugin.cs` — Raid hook + **F12** readouts + diagnostic toggle
- `BigBrain/Internal/CustomLayerWrapper.cs` — *(planned)* State Tree migration — **not shipped** in this fork
- `LootingBots/.../LootingBrain.cs` — ResetForPoolRecycle
- `AILimit/Component.cs` — LINQ allocation fix; SAIN `Dematerialization` / `TrySain*` helpers
- `AILimit/AILimit.csproj`, `AILimit/Plugin.cs` — `ProjectReference` to SAIN; soft BepInEx dependency
- `SAIN/SAIN/Components/AIFrameBudgetScheduler.cs` — `RecheckActivation` before skip; `TotalOnlineBots` + `forceTickBots`
- `SAIN/SAIN/Components/BotDematerializationController.cs`, `BotGameObjectPool.cs`, `OfflineSquadMaterialization.cs` — pool + `demat_*` + proximity remat
- `SAIN/SAIN/Preset/.../PerformanceSettings.cs` — PerformanceMode=true

## Known Limitations

| Issue | Status |
|---|---|
| CombatAudioSpoofer needs EFT AudioClip wiring | Coded but untested — BetterAudio path ready |
| Offline-to-online for **`auto_*`** statistical squads | Not implemented — **`demat_*`** proximity remat shipped; see [SAIN_AILIMIT_DEMATERIALIZATION.md](SAIN_AILIMIT_DEMATERIALIZATION.md) |
| Bot pool Harmony intercepts | Coded but untested — verify on live SPT |
| Profiling baseline | Not captured — needs Windows SPT |

## Documentation

- [SAIN_AILIMIT_DEMATERIALIZATION.md](SAIN_AILIMIT_DEMATERIALIZATION.md) — AILimit ↔ SAIN pool / dematerialize / proximity remat (**full phased change record**)
- `PROGRESS.md` — Current completion status and test instructions
- `PERFORMANCE_ARCHITECTURE.md` — Full architecture guide (all 4 phases)
- `PERFORMANCE_PLAN.md` — Detailed todo list with status
- `../INDEX.md` — Workspace entry point
- `ARCHITECTURE.md` — Original mod internals + OptimizationCore section
