# Workspace Index — SPT Tarkov AI Optimization

> **Purpose:** Entry point for AI coding agents working on this workspace. Read this first to
> understand what exists, where things are, and which document to open for a given task.

---

## Quick Reference

| Question | Answer |
|---|---|
| **What is this workspace?** | Optimization of SPT Tarkov AI mods. All source code lives in `OptimizedMod/` (6 forked mods: BigBrain, SAIN, LootingBots, Waypoints, AILimit, ABPS + MoreBotsAPI + new OptimizationCore library). |
| **What is OptimizedMod?** | Performance-optimized forks of 6 mods + MoreBotsAPI + new OptimizationCore library (budget scheduler, perception LOD, offline combat) |
| **Language** | C# (.NET Framework), BepInEx plugin, Harmony patching, Unity Engine, SPT server DI |
| **Core dependency** | **BigBrain** for behavior mods (SAIN, LootingBots). **Waypoints** for pathfinding. Others are standalone. |
| **Entry point** | Client mods: BepInEx `[BepInPlugin]`. Server mods: SPT DI `[Injectable]`. |

---

## Documentation Map

All project documentation lives in `docs/`. `INDEX.md` is the sole root-level entry point for AI agents.

| Document | Content | Read When |
|---|---|---|
| **[INDEX.md](INDEX.md)** | This file — workspace overview, quick answers, file map | Always start here (root level) |
| **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** | Deep dive into each mod's internal design: classes, tick flows, layer systems, performance hotspots | Understanding a specific mod's internals |
| **[docs/INTEGRATION.md](docs/INTEGRATION.md)** | How mods connect: dependency chains, layer priority, interop APIs, conflict resolution | Modifying cross-mod behavior, adding a new mod |
| **[docs/RESEARCH.md](docs/RESEARCH.md)** | Completed research: community findings, industry AI techniques, Unity constraints, filtered recommendations | Understanding the "why" behind design decisions |
| **[docs/PERFORMANCE_ARCHITECTURE.md](docs/PERFORMANCE_ARCHITECTURE.md)** | Multi-phase performance optimization architecture: budget scheduler, perception LOD, offline combat | Understanding the optimization system design |
| **[docs/PERFORMANCE_PLAN.md](docs/PERFORMANCE_PLAN.md)** | Task-level execution plan with phase tracking and completion status | Checking optimization progress or adding new phases |
| **[docs/OPTIMIZED_MOD_README.md](docs/OPTIMIZED_MOD_README.md)** | Comprehensive guide to the optimized fork stack: budget scheduler, perception system, offline combat, audio spoofer, build setup, and usage | Working on or understanding the optimized forks |
| **[docs/SPTQuestingBots.md](docs/SPTQuestingBots.md)** | SPT QuestingBots mod overview: architecture, questing system, PMC spawning, configuration | Understanding or integrating QuestingBots |
| **[docs/MoreBotsAPI.md](docs/MoreBotsAPI.md)** | MoreBotsAPI framework: custom WildSpawnType registration, client prepatching, server registration | Adding custom bot types |
| **[docs/discussion1.md](docs/discussion1.md)** | Design discussion: architecture decisions, integration strategy, AI LOD, tick management, risk register | Understanding architectural trade-offs and decisions |
| **[docs/PROGRESS.md](docs/PROGRESS.md)** | Implementation progress tracker for the optimization work | Tracking what's done and what remains |
| **[docs/ROGUE_BASE_DEFENSE_PLAN.md](docs/ROGUE_BASE_DEFENSE_PLAN.md)** | Planned Rogue (`ExUsec`) base-defense squad coordination + no-loot policy | Designing/defending Rogue behavior on Lighthouse without regressions |
| **[docs/BUGFIX-BigBrainPriority-QuestingBots.md](docs/BUGFIX-BigBrainPriority-QuestingBots.md)** | BigBrain arbitration fix: QuestingBots vs SAIN combat gating + minimal diagnostics | Debugging passive combat / wrong active layer selection with QuestingBots |
| **[docs/BUGFIX-SAINAILimit-Audibility.md](docs/BUGFIX-SAINAILimit-Audibility.md)** | Critical bugfix: SAINAILimit audibility detection was broken — bots not fighting, Big Pipe passive | Debugging AI "detect → stop → loop" behavior or boss passivity |
| **[docs/BUGFIX-SAINLayerPriority.md](docs/BUGFIX-SAINLayerPriority.md)** | Critical bugfix: SAIN combat layers at priority 20-22 blocked by BotMind — bots stuck in patrol, never fight | Debugging bots ignoring player fire or stuck in patrol/follow |

---

## Repository Map

> **All source code lives in `OptimizedMod/`.** Root directories (`SPT-BigBrain/`, `SAIN/`, etc.) are empty legacy placeholders and should be ignored.

```
Tarkov AI/
├── INDEX.md                         ← You are here (root-level entry point)
├── docs/                            ← All project documentation (13+ files)
│   ├── ARCHITECTURE.md              ← Mod internals (classes, ticks, layers)
│   ├── INTEGRATION.md               ← Cross-mod wiring (dependencies, priorities, interop)
│   ├── RESEARCH.md                  ← Research findings and filtered recommendations
│   ├── PERFORMANCE_ARCHITECTURE.md  ← Optimization system architecture
│   ├── PERFORMANCE_PLAN.md          ← Phase execution plan with completion status
│   ├── OPTIMIZED_MOD_README.md      ← Optimized fork stack guide
│   ├── SPTQuestingBots.md           ← QuestingBots mod overview and architecture
│   ├── MoreBotsAPI.md               ← MoreBotsAPI framework documentation
│   ├── discussion1.md               ← Architectural design discussion
│   ├── BUGFIX-BigBrainPriority-QuestingBots.md ← BigBrain arbitration + QB combat gating bugfix
│   ├── BUGFIX-SAINAILimit-Audibility.md ← Critical AI behavior bugfix (detect→loop, Big Pipe)
│   ├── BUGFIX-SAINLayerPriority.md   ← Combat layer priority fix (bots stuck in patrol)
│   ├── ROGUE_BASE_DEFENSE_PLAN.md    ← Planned Rogue base-defense squad coordination + no-loot policy
│   └── PROGRESS.md                  ← Implementation progress tracker
│
├── OptimizedMod/                     ← ALL source code lives here
│   ├── SAIN/                         ← Combat AI: shipped sources live under SAIN/SAIN/
│   │   ├── SAIN.csproj               ← Root build compiles `SAIN/**` only; legacy duplicate roots (Classes/, Components/, …) removed from repo
│   │   ├── SAIN/SAINPlugin.cs        ← [BepInPlugin] entry, TryCreateCustomPreset()
│   │   ├── SAIN/Plugin/BigBrainHandler.cs ← Registers SAIN layers, removes vanilla layers
│   │   ├── SAIN/Layers/              ← CustomLayer implementations (CombatSolo, CombatSquad, …)
│   │   ├── SAIN/Components/          ← MonoBehaviour components (BotComponent, BudgetScheduler, …)
│   │   ├── SAIN/Classes/Bot/         ← Per-bot subsystems (Decision, Vision, Hearing, Memory, etc.)
│   │   ├── SAIN/Classes/BotManager/Jobs/ ← Coroutine-based global jobs
│   │   ├── SAIN/Preset/              ← Configuration system (GlobalSettings, BotSettings)
│   │
│   ├── BigBrain/                     ← Framework: BrainManager, CustomLayer, CustomLogic
│   │   ├── BigBrainPlugin.cs         ← [BepInPlugin] entry, 6 Harmony patches
│   │   ├── Brains/BrainManager.cs    ← Singleton registry for layers/logic (IDs start at 9000)
│   │   ├── Brains/CustomLayer.cs     ← Abstract base: IsActive(), GetNextAction(), IsCurrentActionEnding()
│   │   ├── Brains/CustomLogic.cs     ← Abstract base: Update(), Start(), Stop()
│   │   ├── Internal/                 ← Wrappers bridging Custom* to EFT's native brain types
│   │   └── Patches/                  ← Harmony patches into EFT brain activation/update
│   │
│   ├── LootingBots/                  ← Looting AI: corpse/container/item scanning, gear swap
│   │   ├── LootingBots/LootingBots.cs     ← [BepInPlugin] entry, config, layer registration
│   │   ├── LootingBots/LootingLayer.cs    ← CustomLayer; priority via BigBrainLootLayerPriority (default 62)
│   │   ├── LootingBots/Logic/        ← CustomLogic implementations (FindLoot, Loot, Peaceful)
│   │   ├── LootingBots/Components/   ← MonoBehaviour components (LootingBrain, LootFinder)
│   │   ├── LootingBots/External.cs   ← Public interop API (ForceBotToScanLoot, etc.)
│   │   └── LootingBots/Utilities/    ← ScanScheduler, ActiveBotCache, ActiveLootCache, etc.
│   │
│   ├── Waypoints/                    ← Expanded NavMesh + door fixes
│   │   ├── WaypointsPlugin.cs        ← [BepInPlugin] entry, 7 Harmony patches
│   │   └── Patches/                  ← WaypointPatch (inject NavMesh), FindPathPatch, Door*.cs
│   │
│   ├── AILimit/                      ← Distance-based bot deactivation (retained for config compat)
│   │   ├── Plugin.cs                 ← [BepInPlugin] entry, config, 2 patches
│   │   └── Component.cs              ← AILimitComponent: sorts bots, activates closest N
│   │
│   ├── ABPS/                         ← Bot spawn control (client+server)
│   │   ├── Client/Plugin.cs          ← [BepInPlugin] entry, 13 Harmony patches
│   │   └── Server/Controllers/       ← Map, PMC, Scav, Boss spawn config
│   │
│   ├── MoreBotsAPI/                  ← API for dynamic bot count scaling
│   │   ├── Plugin/Plugin.cs          ← Client BepInEx plugin
│   │   └── Server/                   ← Server-side registration
│   │
│   └── OptimizationCore/             ← Shared perf library (not referenced by SAIN.csproj; SAIN inlines scheduler/components)
│       ├── AIFrameBudgetScheduler.cs ← Reference patterns duplicated under `SAIN/SAIN/Components/` for shipping DLL
│       ├── PerceptionSystem.cs       ← Player-centric visibility (frustum + raycast) and audibility
│       ├── OfflineCombatResolver.cs  ← Statistical AI-vs-AI combat using bot stats
│       ├── CombatAudioSpoofer.cs     ← Fake gunfire audio at combat zones with distance attenuation
│       ├── PerceptionTier.cs         ← Enum: Visible, Audible, Occluded
│       ├── IBudgetedAI.cs            ← Interface: ProcessAITick(), CurrentTier
│       ├── IOfflineSquad.cs          ← Interface: TickOffline(), SquadId, Members, SquadPosition
│       └── OfflineCombatTypes.cs     ← OfflineBotStats, OfflineCombatResult data models
│
└── build-output/                    ← Compiled DLLs for testing
```

---

## Key Concepts (for AI Agents)

### 1. BigBrain is the Foundation

All behavior mods register through BigBrain's `BrainManager`. The two core abstractions are:

| Abstraction | Purpose | Lifecycle Methods |
|---|---|---|
| `CustomLayer` | A behavior mode (e.g., "CombatSolo", "Looting") | `IsActive()` → `GetNextAction()` → `IsCurrentActionEnding()` → `Start()`/`Stop()` |
| `CustomLogic<T>` | An action within a layer (e.g., "DogFight", "LootCorpse") | `Start()` → `Update(T data)` → `Stop()` |

**How they connect:** A `CustomLayer` returns a `CustomLayer.Action` from `GetNextAction()`.
The action specifies a `CustomLogic` type. BigBrain's internal `CustomLayerWrapper` bridges
these to EFT's native brain system.

### 2. Layer Priority Arbitrates Behavior

When multiple layers want to be active, BigBrain checks them in **descending priority order**
(higher number = checked first). The first layer where `IsActive()` returns `true` takes control.

```
Priority 99: SAIN DebugLayer        (always wins if debug mode)
Priority 80: SAIN AvoidThreatLayer  (grenade/artillery nearby)
Priority 65: SAIN ExtractLayer      (bot wants to extract)
Priority 60-70: SAIN Combat layers  (bot in combat)
Priority ~62: LootingBots LootingLayer (default BigBrainLootLayerPriority — peaceful loot/peace)
```

**Key rule:** BigBrain picks the **highest-priority active layer**. With fork defaults, SAIN combat/extract (>62) beats LootingBots loot/peace; loot runs when those layers are inactive. Misconfigured priorities can change that — see `docs/INTEGRATION.md`.

### 3. Bot Tick Architecture (SAIN)

SAIN's per-bot tick is organized into **4 groups** with different activation conditions:

| Group | Activation Condition | Key Members |
|---|---|---|
| `_alwaysTickClasses` | Every frame | SAINActivationClass, SAINAILimit, SAINEnemyController, SAINDecisionClass |
| `_tickWhenActiveClasses` | Bot is active | SAINBotUnstuckClass |
| `_tickWhenNoSleepClasses` | Bot not in standby | Vision, Hearing, Mover, Cover, Steering, Memory, Suppression (~18 classes) |
| `_tickWhenCombatClasses` | Bot in combat | SAINShootData, AimDownSightsController, SAINFriendlyFireClass |

### 4. Performance Architecture (SAIN)

SAIN has a **3-tier AI Limit system** based on distance from the nearest human player:

| Tier | Distance | Vision Rate | Cover | Decision Rate |
|---|---|---|---|---|
| None | < 150m | 30Hz | 10Hz | 10Hz |
| Far | 150-250m | Reduced | 5Hz | 5Hz |
| VeryFar | 250-400m | Minimal | Disabled | 3Hz |
| Narnia | > 400m | Near-zero | Disabled | 2Hz |

### 5. Performance Architecture (LootingBots)

LootingBots has **3 performance gates**:

| Gate | Default | Mechanism |
|---|---|---|
| ActiveBotCache | 20 bots max | Bots beyond cap have looting brain disabled entirely |
| Distance gating | 0 (off) | Bots beyond N meters from player are disabled |
| ScanScheduler | 3 concurrent | Token-based limiter — only N bots scan at once |

### 6. Mod Categories

The 6 forked mods (plus OptimizationCore) form **four layers** of the AI stack:

| Layer | Mods | Function |
|---|---|---|
| **Infrastructure** | Waypoints, AILimit | NavMesh data, bot activation. Below behavior mods. |
| **Behavior Framework** | BigBrain | `BrainManager`, `CustomLayer`/`CustomLogic` abstractions. |
| **Behavior Mods** | SAIN, LootingBots | Combat AI and looting AI. Extend BigBrain. |
| **Spawn/Placement** | ABPS (botplacementsystem fork) | Controls what bots spawn, where, and how many. Client+Server. |
| **Performance/Scale** | OptimizedMod/OptimizationCore | Frame budget scheduling, player-centric perception LOD, offline combat resolution, audio spoofing. Wraps the entire stack. |

### 7. No-BigBrain Mods

Three mods operate **without BigBrain** because they work at a lower level:

| Mod | Integration Mechanism | Why No BigBrain |
|---|---|---|
| **Waypoints** | Direct Harmony patches into `BotsController.Init` and `BotPathFinderClass.FindPath` | Operates at Unity NavMesh level, below brain abstraction |
| **AILimit** | `MonoBehaviour` on GameWorld, `GameObject.SetActive(false)` | Operates at Unity GameObject level, below brain tick |
| **botplacementsystem (ABPS)** | 13 Harmony patches into bot spawn system | Controls bot spawning, not bot behavior |

---

## Common Agent Tasks

### Task: Add a new bot behavior

1. Read `docs/ARCHITECTURE.md` → SAIN Layer/Action System section
2. Read `docs/INTEGRATION.md` → SAIN ↔ BigBrain → Registration Flow
3. Create a new class extending `SAINLayer` or `CustomLayer`
4. Register via `BrainManager.AddCustomLayer()` in plugin init
5. Choose appropriate priority (above/below existing layers)

### Task: Fix a performance issue

1. Read `docs/ARCHITECTURE.md` → SAIN Performance Hotspots section
2. Read `docs/PERFORMANCE_ARCHITECTURE.md` for the full optimization architecture and hotspot analysis
3. Identify the hotspot from the ranked priority table
4. Check the AI Limit tiering system (`Bot.CurrentAILimit`) for throttling hooks
5. Use `WaitForSeconds` caching, `ShallTick()` gating, or coroutine interval adjustment

### Task: Understand how SAIN and LootingBots coexist

1. Read `docs/INTEGRATION.md` → SAIN ↔ LootingBots section
2. Key insight: Numeric priority determines behavior (fork default ~62 loot vs higher SAIN combat).
3. Interop API: LootingBots exposes `External.PreventBotFromLooting()` and `ForceBotToScanLoot()`

### Task: Add a new mod to the workspace

1. Fork the mod into `OptimizedMod/<ModName>/`
2. Read `docs/INTEGRATION.md` → Adding a New Mod → Template
3. Document in `docs/ARCHITECTURE.md` (new section before Directory Map)
4. Document in `docs/INTEGRATION.md` (new section before Mod Compatibility Matrix)
5. Update the compatibility matrix and known integration points table
6. Update `INDEX.md` repository map and quick reference

### Task: Understand BigBrain's internal mechanics

1. Read `docs/ARCHITECTURE.md` → BigBrain section
2. Key classes: `BrainManager`, `CustomLayer`, `CustomLogic`, `CustomLayerWrapper`, `CustomLogicWrapper`
3. IDs start at 9000 (`START_LAYER_ID`, `START_LOGIC_ID`)
4. Patches: 6 Harmony patches into bot brain lifecycle

### Task: Reduce bot CPU usage across all mods

1. **AILimit**: Configure per-map distances and `BotLimit` (default 10) for binary bot deactivation
2. **SAIN AI Limit**: Enable internal throttling tiers (Far/VeryFar/Narnia) in preset settings
3. **ABPS**: Reduce `SoftCap` and per-map limits, enable `DespawnFurthest`
4. **Combined approach**: AILimit for farthest, SAIN limit for mid-range, ABPS for total cap
5. **OptimizedMod approach**: Use `AIFrameBudgetScheduler` (2ms hard cap with Visible/Audible/Occluded
   priority tiers) + `PerceptionSystem` (camera frustum + raycast, replacing distance-only AILimit) +
   `OfflineCombatResolver` (statistical combat for bots outside player view) + `CombatAudioSpoofer`
   (fake audio instead of real AI processing for distant combat)
6. See `docs/PERFORMANCE_ARCHITECTURE.md` for identified code-level hotspots

### Task: Tune PMC spawns with ABPS

1. **ABPS** (client-side): Set per-map `PmcSpawnDistanceCheck` and map limits
2. Configure `SoftCap` and per-map limits in `OptimizedMod/ABPS/Server/config.json`
3. See `docs/INTEGRATION.md` → `botplacementsystem (ABPS) ↔ Ecosystem` for the interaction analysis

### Task: Debug bot pathfinding issues

1. Check if **Waypoints** custom NavMesh is loaded (verify `EnableCustomNavmesh` is enabled in config)
2. Check `OptimizedMod/Waypoints/Patches/FindPathPatch.cs` — it overrides `BotPathFinderClass.FindPath`
3. Enable debug components via `DebugPatch` for NavMesh/BotZone visualization
4. If navmesh bundles are missing, check `OptimizedMod/Waypoints/navmesh/` for `.bundle` files

---

## Performance Baselines

Real-world frame time measurements for calibration. These baselines help quantify the gap
between current performance and the 16.7ms target (60 fps). Each row is a concrete datapoint
from actual gameplay.

| # | Map | Bots | Scenario | Frame Time | FPS | CPU | GPU | Date |
|---|---|---|---|---|---|---|---|---|
| **1** | Custom | 5-6 fighting | Combat | ~20ms | ~15 (↓45 from 60) | Ryzen 5 5600 | RX 5700 XT | 2026-05-02 |
| 2 | _TBD_ | _TBD_ | _TBD_ | _TBD_ | _TBD_ | _TBD_ | _TBD_ | — |
| 3 | _TBD_ | _TBD_ | _TBD_ | _TBD_ | _TBD_ | _TBD_ | _TBD_ | — |

**Interpretation of Baseline 1:**
- With only 5-6 bots fighting on a custom map, frame time already exceeds the 16.7ms budget by ~3ms
- At typical bot counts (15-20 on Streets/Lighthouse), this scales non-linearly due to O(N²) vision checks
- Target: bring 5-6 bot combat frame time under 16.7ms → then scale to 15-20 bots
- See `docs/PERFORMANCE_ARCHITECTURE.md` for the optimization architecture and performance plan

**OptimizedMod target:** Lighthouse with 29+ bots at 60 FPS (≤16.7ms frame time) using the full
OptimizationCore stack — budget scheduler at 2ms cap, perception LOD (camera frustum + raycast
instead of distance-only checks), offline combat resolution for AI-vs-AI, and audio spoofing.

**How to add a baseline:** Run a raid, note map + approximate bot count + scenario (idle/combat/looting),
record frame time (BepInEx.FPSCounter or SAIN debug overlay), and add a row above.

---

## File Location Quick Reference

> All code paths are relative to `OptimizedMod/`. All document paths are relative to `docs/`.

| Need | File(s) | Section |
|---|---|---|
| SAIN layer registration code | `SAIN/SAIN/Plugin/BigBrainHandler.cs` | `BrainAssignment.Init()` |
| LootingBots layer registration | `LootingBots/LootingBots/LootingBots.cs` | `Awake()` |
| How a CustomLayer works | `BigBrain/Brains/CustomLayer.cs` | Full file (49 lines) |
| How a CustomLogic works | `BigBrain/Brains/CustomLogic.cs` | Full file (27 lines) |
| BigBrain wrapper internals | `BigBrain/Internal/CustomLayerWrapper.cs`, `CustomLogicWrapper.cs` | Whole files |
| SAIN tick entry point | `SAIN/SAIN/Components/GameWorldComponent.cs` | `WorldTick()` |
| SAIN bot tick groups | `SAIN/SAIN/Components/BotComponent.cs` | `ManualUpdate()`, `TickClassGroup()` |
| SAIN performance architecture | `docs/PERFORMANCE_ARCHITECTURE.md` | Full document |
| SAIN AI limit tiers | `SAIN/SAIN/Classes/Bot/SAINAILimit.cs` | `AILimitSetting` enum |
| SAINAILimit audibility bugfix | `docs/BUGFIX-SAINAILimit-Audibility.md` | Full document — 3 bugs, fixes, verification guide |
| SAIN layer priority bugfix | `docs/BUGFIX-SAINLayerPriority.md` | Full document — priority conflict with BotMind, fix and verification |
| LootingBots interop API | `LootingBots/LootingBots/External.cs` | All public methods |
| LootingBots scan scheduling | `LootingBots/LootingBots/Utilities/ScanScheduler.cs` | `CanStartScan()` |
| LootingBots loot state machine | `LootingBots/LootingBots/Components/LootingBrain.cs` | `Update()`, async loot methods |
| All mod dependency declarations | Each mod's plugin `.cs` file | `[BepInDependency]` attributes |
| Waypoints NavMesh injection | `Waypoints/Patches/WaypointPatch.cs` | `InjectNavmesh()` |
| Waypoints pathfinding override | `Waypoints/Patches/FindPathPatch.cs` | `PatchPrefix()` |
| AILimit bot activation logic | `AILimit/Component.cs` | `Update()`, `UpdateBots()` |
| AILimit config (distances/limits) | `AILimit/Plugin.cs` | Config.Bind entries |
| ABPS spawn patches | `ABPS/Client/Plugin.cs` | `Awake()` → patch list |
| ABPS server config | `ABPS/Server/Models/AbpsConfig.cs` | Full config model |
| OptimizationCore budget scheduler | `OptimizationCore/AIFrameBudgetScheduler.cs` | `Update()`, `ProcessTier()`, `RegisterBot()` |
| OptimizationCore perception | `OptimizationCore/PerceptionSystem.cs` | `EvaluateBot()`, `IsVisible()` |
| OptimizationCore offline combat | `OptimizationCore/OfflineCombatResolver.cs` | `ResolveCombat()`, `CalculateSquadPower()` |
| OptimizationCore audio spoofing | `OptimizationCore/CombatAudioSpoofer.cs` | `ScheduleCombatAudio()`, `PlayCombatSequence()` |
| OptimizationCore interfaces | `OptimizationCore/IBudgetedAI.cs`, `IOfflineSquad.cs` | `ProcessAITick()`, `TickOffline()` |
| OptimizationCore types/enums | `OptimizationCore/PerceptionTier.cs`, `OfflineCombatTypes.cs` | `PerceptionTier` enum, `OfflineBotStats`, `OfflineCombatResult` |

---

## Layer Priority Reference

Complete priority hierarchy across all mods:

| Priority | Layer | Mod | Bot Types |
|---|---|---|---|
| 99 | DebugLayer | SAIN | All |
| 80 | SAINAvoidThreatLayer | SAIN | All |
| ~65 | ExtractLayer | SAIN | PMCs, Scavs (configurable) |
| ~60-70 | CombatSquadLayer | SAIN | All |
| ~60-70 | CombatSoloLayer | SAIN | All |
| ~62 (cfg) | LootingLayer | LootingBots | All registered brains (`BigBrainLootLayerPriority` default 62) |

---

## Quick Answers

**Q: Why can't SAIN and LootingBots run at the same time?**
A: They can coexist on every bot; BigBrain activates **one** layer per decision cycle based on
`IsActive()` and numeric priority. With fork defaults, SAIN combat/extract priorities beat LootingBots'
loot layer (~62). When combat SAIN layers go inactive, loot/peace can run.

**Q: Where are performance settings stored?**
A: SAIN: `OptimizedMod/SAIN/Preset/GlobalSettings/Categories/General/PerformanceSettings.cs`. LootingBots:
`MaxActiveLootingBots`, `LimitDistanceFromPlayer`, `MaxConcurrentScans` config entries in
`OptimizedMod/LootingBots/LootingBots/LootingBots.cs`.

**Q: How do I make a bot more aggressive?**
A: Modify SAIN's personality settings or per-bot difficulty settings in the SAIN preset system.
See `docs/PERFORMANCE_ARCHITECTURE.md` for the optimization architecture and preset configuration details.

**Q: How do I make bots loot more?**
A: Tweak LootingBots config: lower `LootScanInterval` (default 15s), increase `MaxActiveLootingBots`
(default 20), lower value thresholds, or enable more loot types. Config entries in `LootingBots.cs`.

**Q: What happens when a new mod's layer conflicts with existing layers?**
A: BigBrain checks layers in priority order. The first active layer wins. To ensure your layer
runs, set its priority appropriately. Use `BrainManager.GetActiveLayer()` and
`BrainManager.GetActiveLayerName()` to debug which layer is active.

**Q: How does AILimit differ from SAIN's internal AI Limit?**
A: AILimit completely deactivates distant bots (`GameObject.SetActive(false)` = zero CPU cost) at
per-map configurable distances (80-400m). SAIN's AI Limit throttles subsystems while keeping bots
active. They can complement each other.

**Q: How do ABPS and bot limits interact?**
A: ABPS handles bot spawn caps and distance checks. AILimit further deactivates distant spawned bots
at the GameObject level. SAIN's internal AI Limit throttles subsystems for mid-range bots. They
operate at different layers and can complement each other.

**Q: How do I reduce CPU usage from bots?**
A: Three complementary approaches from the original mods: (1) AILimit — completely disable distant bots,
(2) SAIN AI Limit settings — throttle vision/hearing for far bots, (3) ABPS — reduce map bot caps and
enable despawn. For the optimized fork stack, use the **OptimizationCore budget scheduler** (2ms hard
cap per frame with Visible/Audible/Occluded priority tiers) and **PerceptionSystem** (player-centric
camera frustum + raycast checks instead of distance-only AILimit). Also see `docs/PERFORMANCE_ARCHITECTURE.md`
for the optimization architecture.

**Q: How do SAIN's performance optimizations affect LootingBots?**
A: They don't directly. But both mods add per-bot CPU overhead. The performance architecture guide
(`docs/PERFORMANCE_ARCHITECTURE.md`) notes that SAIN changes are "fully compatible" with LootingBots
since they're separate systems, but the combined load should be considered on high-bot-count maps.
