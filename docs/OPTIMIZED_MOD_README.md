# OptimizedMod — SPT Tarkov AI Performance Overhaul

Performance-optimized forks of 6 SPT AI mods + MoreBotsAPI + new **OptimizationCore** shared library.
Target: **Lighthouse with 29+ bots at stable 60 FPS.**

## Quick Start (Windows SPT)

1. Copy `OptimizedMod/` to SPT `BepInEx/plugins/`
2. Launch SPT, start raid
3. Press **F12** → "SAIN Performance" section for live stats
4. Check `BepInEx/LogOutput/sain_perf.csv` after raid

## What's Inside

### Forked Mods (7)
| Mod | Optimizations |
|---|---|
| **SAIN** | Budget scheduler, perception-tiered AI LOD, squad coordinator, State Tree layer eval, bot pool |
| **BigBrain** | State-tree migration (4x faster layer arbitration) |
| **LootingBots** | Pool recycle state reset |
| **Waypoints** | Path cache for recycled bots |
| **AILimit** | Perception-system compat, LINQ alloc fix, pool coord |
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

## F12 Performance Monitor

Press F12 in-game → "SAIN Performance" section:

| Config | Description |
|---|---|
| `Monitor Enabled` | Master toggle |
| `Log Interval (sec)` | How often to write stats (1-60s) |
| `CSV Logging` | Write `sain_perf.csv` to `BepInEx/LogOutput/` |
| `Verbose BepInEx Log` | Also log summaries to console |
| `Dump Stats Now` | Toggle ON → full performance snapshot to log |
| `FPS / Frame Time` | Read-only real-time display |
| `AI Budget` | ms used/max, utilization %, exhaustion rate |
| `Bot Distribution` | V/A/O/Offline counts |

CSV format: timestamp, FPS, frame time, budget used, budget limit, utilization%, exhaustion%, visible/audible/occluded/offline counts, pool stats.

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
├── SAIN/Components/
│   ├── AIFrameBudgetScheduler.cs
│   ├── OfflineCombatResolver.cs
│   ├── CombatAudioSpoofer.cs
│   ├── SAINPerformanceMonitor.cs
│   ├── BotGameObjectPool.cs
│   └── BotComponent.cs (modified)
├── SAIN/Layers/Combat/Squad/
│   └── SquadCombatCoordinator.cs
├── SAIN/Patches/
│   └── BotPoolPatches.cs
```

### Modified files (key changes)
- `SAIN/Classes/Bot/BotBase.cs` — TickInterval = 1f/30f
- `SAIN/Classes/Bot/SAINAILimit.cs` — PerceptionTier, visibility/audibility
- `SAIN/Classes/BotManager/Jobs/VisionRaycastJob.cs` — LOD raycast reduction
- `SAIN/Classes/Bot/EnemyControllers/SAINEnemyController.cs` — Squad propagation
- `SAIN/Classes/BotManager/Squad.cs` — Squad awareness
- `SAIN/Layers/SAINLayer.cs` — CheckIsActiveWithCache (State Tree)
- `SAIN/Layers/Combat/Squad/CombatSquadLayer.cs` — Squad coordinator integration
- `SAIN/SAINPlugin.cs` — Pool init + F12 perf monitor config
- `SAIN/Components/BotManagerComponent.cs` — Budget scheduler + perf monitor wiring
- `BigBrain/Internal/CustomLayerWrapper.cs` — State Tree migration
- `LootingBots/.../LootingBrain.cs` — ResetForPoolRecycle
- `AILimit/Component.cs` — LINQ allocation fix
- `SAIN/Preset/.../PerformanceSettings.cs` — PerformanceMode=true

## Known Limitations

| Issue | Status |
|---|---|
| CombatAudioSpoofer needs EFT AudioClip wiring | Coded but untested — BetterAudio path ready |
| Offline-to-online squad transition | Not implemented — needs runtime testing |
| Bot pool Harmony intercepts | Coded but untested — verify on live SPT |
| Profiling baseline | Not captured — needs Windows SPT |

## Documentation

- `PROGRESS.md` — Current completion status and test instructions
- `PERFORMANCE_ARCHITECTURE.md` — Full architecture guide (all 4 phases)
- `PERFORMANCE_PLAN.md` — Detailed todo list with status
- `../INDEX.md` — Workspace entry point
- `ARCHITECTURE.md` — Original mod internals + OptimizationCore section
