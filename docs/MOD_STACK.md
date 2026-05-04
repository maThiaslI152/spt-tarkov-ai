# Mod Stack — Architecture & Integration

> **Purpose:** This document describes every mod in the optimization stack, how they connect,
> their dependency graph, priority hierarchy, and initialization order. It serves as the
> canonical reference for understanding how the pieces fit together.

**Agents:** Start at [INDEX.md](../INDEX.md). Companion: [INTEGRATION.md](INTEGRATION.md),
[ARCHITECTURE.md](ARCHITECTURE.md), [PERFORMANCE_ARCHITECTURE.md](PERFORMANCE_ARCHITECTURE.md).

---

## Table of Contents

1. [Mod Inventory](#mod-inventory)
2. [Dependency Graph](#dependency-graph)
3. [Layered Stack Diagram](#layered-stack-diagram)
4. [Mod Deep-Dives](#mod-deep-dives)
   - [BigBrain — Behavior Framework](#1-bigbrain--behavior-framework)
   - [SAIN — Combat AI](#2-sain--combat-ai)
   - [LootingBots — Looting AI](#3-lootingbots--looting-ai)
   - [Waypoints — Expanded NavMesh](#4-waypoints--expanded-navmesh)
   - [AILimit — Distance-Based Deactivation](#5-ailimit--distance-based-bot-deactivation)
   - [ABPS — Bot Spawn Control](#6-abps--bot-spawn-control)
   - [MoreBotsAPI — Custom Bot Types](#7-morebotsapi--custom-bot-types)
   - [SAINPerfLog — Raid Telemetry](#8-sainperflog--raid-telemetry)
   - [OptimizationCore — Performance Infrastructure](#9-optimizationcore--performance-infrastructure)
5. [Performance Infrastructure (Shipped Inside SAIN)](#performance-infrastructure-shipped-inside-sain)
6. [Reference-Only Mods](#reference-only-mods)
7. [Layer Priority Hierarchy](#layer-priority-hierarchy)
8. [Initialization & Runtime Sequence](#initialization--runtime-sequence)
9. [Data Flow Diagrams](#data-flow-diagrams)
10. [Key Integration Points Table](#key-integration-points-table)

---

## Mod Inventory

All source code lives under `E:\spt-tarkov-ai\OptimizedMod\`. Each directory is one mod.

| # | Mod | Category | Language | Entry Point | Purpose |
|---|-----|----------|----------|-------------|---------|
| 1 | **BigBrain** | Framework | C#, BepInEx, Harmony | `BigBrainPlugin.cs` `[BepInPlugin]` | Behavior layer framework — `BrainManager`, `CustomLayer`/`CustomLogic` |
| 2 | **SAIN** | Behavior | C#, BepInEx, Harmony | `SAINPlugin.cs` `[BepInPlugin]` | Full combat AI replacement (vision, hearing, cover, squad tactics) |
| 3 | **LootingBots** | Behavior | C#, BepInEx, Harmony | `LootingBots.cs` `[BepInPlugin]` | Bot looting AI (corpses, containers, loose items) |
| 4 | **Waypoints** | Infrastructure | C#, BepInEx, Harmony | `WaypointsPlugin.cs` `[BepInPlugin]` | Expanded per-map NavMesh + door fix patches |
| 5 | **AILimit** | Infrastructure | C#, BepInEx, Harmony | `Plugin.cs` `[BepInPlugin]` | Binary bot deactivation by distance (`SetActive(false)`) |
| 6 | **ABPS** | Spawn Control | C#, BepInEx + Server | `Plugin.cs` `[BepInPlugin]` | Bot spawn caps, despawn, boss chances, PMC distance checks |
| 7 | **MoreBotsAPI** | Spawn Control | C#, BepInEx + Server | `Plugin.cs` `[BepInPlugin]` | Custom bot type registration & dynamic bot count scaling |
| 8 | **SAINPerfLog** | Telemetry | C#, BepInEx | `PerfLogPlugin.cs` `[BepInPlugin]` | Per-raid CSV logging, F12 diagnostics, BigBrain snapshots |
| 9 | **OptimizationCore** | Performance Library | C# | N/A (shared lib) | Contracts and reference types for performance infrastructure |

**Reference-only mods** (outside `OptimizedMod/`, not in shipped stack):

| Mod | Purpose | Why Not Shipped |
|-----|---------|-----------------|
| **SPTQuestingBots/** | Quest-driven bot behavior | Study/reference only. Patterns copied into OptimizedMod |
| **spt-unda/** | Server-side PMC wave overhaul | Standalone; can complement ABPS |
| **SPT/** | SPT Core 4.0+ | Game server, not mod source |
| **SAIN443/** | Older SAIN version | Archive for comparison |
| **botplacementsystem-csharp/** | Original ABPS reference | Source study only |

---

## Dependency Graph

```
                    ┌──────────────────────────┐
                    │       SPT Core 4.0+       │
                    │    (EFT Game + Server)     │
                    └────┬──────┬───────┬───────┘
                         │      │       │
              ┌──────────┘      │       └──────────┐
              ▼                 ▼                   ▼
       ┌────────────┐   ┌──────────────┐   ┌───────────────┐
       │  BigBrain  │   │   Waypoints  │   │  spt-unda     │
       │ (Hard dep) │   │  (Hard dep)  │   │ (Server only) │
       └─────┬──────┘   └──────┬───────┘   └───────────────┘
             │                 │
    ┌────────┴────────┐       │
    ▼                 ▼        │
┌─────────┐   ┌────────────┐  │
│  SAIN   │   │ LootingBots│  │  (SAIN hard-depends on both)
│         │   │            │  │
│ Dependencies:             │  │
│  ├── BigBrain (hard)      │  │
│  └── Waypoints (hard)     │  │
└────┬────┘                  │  │
     │                       │  │
     └─────── sibings ───────┘  │
                                │
     ┌──────────────────────────┘
     ▼
┌───────────┐   ┌──────────┐   ┌──────────────┐
│ AILimit   │   │   ABPS   │   │  MoreBotsAPI  │
│ (No deps) │   │(No deps) │   │  (No deps)    │
└───────────┘   └──────────┘   └──────────────┘

Key:
  Hard dependency = required for the mod to function
  Sibling = both depend on same parent; no cross-dependency
  No deps = depends only on SPT Core, no other mods
```

Hard dependency chain:
- **SAIN** → BigBrain + Waypoints
- **LootingBots** → BigBrain
- **All others** → SPT Core only

---

## Layered Stack Diagram

The mods form **four layers** from bottom (closest to game engine) to top (highest-level behavior):

```
                  ┌──────────────────────────────────────────────────┐
    LAYER 4       │            PERFORMANCE / SCALE                   │
    (Wraps all)   │  OptimizationCore (budget scheduler, perception  │
                  │  LOD, offline combat, audio spoofing)            │
                  │  SAINPerfLog (telemetry, diagnostics)            │
                  └──────────────────────┬───────────────────────────┘
                                          │
                  ┌──────────────────────┴───────────────────────────┐
    LAYER 3       │              BEHAVIOR MODS                       │
    (BigBrain     │                                                  │
     layers)      │   SAIN (Combat)        LootingBots (Looting)     │
                  │   ┌─────────────┐      ┌────────────────────┐    │
                  │   │ DebugLayer  │ 99   │                    │    │
                  │   │ AvoidThreat │ 80   │  LootingLayer      │    │
                  │   │ CombatSquad │ ~78  │  (priority ~62     │    │
                  │   │ CombatSolo  │ ~77  │   default in fork) │    │
                  │   │ Extract     │ ~74  │                    │    │
                  │   └─────────────┘      └────────────────────┘    │
                  └──────────────────────┬───────────────────────────┘
                                          │
                  ┌──────────────────────┴───────────────────────────┐
    LAYER 2       │             BEHAVIOR FRAMEWORK                   │
                  │                                                  │
                  │   BigBrain                                       │
                  │   ├── BrainManager (singleton registry)          │
                  │   ├── CustomLayer (abstract base for layers)     │
                  │   ├── CustomLogic (abstract base for actions)    │
                  │   ├── CustomLayerWrapper (bridges to EFT)        │
                  │   └── CustomLogicWrapper (bridges to EFT)        │
                  └──────────────────────┬───────────────────────────┘
                                          │
                  ┌──────────────────────┴───────────────────────────┐
    LAYER 1       │           INFRASTRUCTURE MODS                    │
                  │                                                  │
                  │   Waypoints        AILimit         ABPS          │
                  │   (NavMesh data)   (bot deactivation) (spawn    │
                  │                     by distance)      control)   │
                  │                                                 │
                  │   MoreBotsAPI  (bot type scaling)               │
                  └──────────────────────┬───────────────────────────┘
                                          │
                  ┌──────────────────────┴───────────────────────────┐
    LAYER 0       │            SPT CORE (EFT GAME ENGINE)           │
                  │    BotOwner, GameWorld, NavMesh, Physics         │
                  └──────────────────────────────────────────────────┘
```

---

## Mod Deep-Dives

### 1. BigBrain — Behavior Framework

**Role:** The foundation. Provides the API that all behavior mods use to inject custom
AI into EFT's closed bot brain system.

**Key classes:**
- `BrainManager` — Singleton registry. Tracks all registered `CustomLayer` types, `CustomLogic`
  types, excluded vanilla layers, and activated bots.
- `CustomLayer` — Abstract base for behavior modes. Methods: `IsActive()`, `GetNextAction()`,
  `IsCurrentActionEnding()`, `Start()`, `Stop()`.
- `CustomLogic<T>` — Abstract base for actions within a layer. Methods: `Start()`, `Stop()`,
  `Update(T data)`.
- `CustomLayerWrapper` — Internal bridge. Wraps `CustomLayer` as EFT's native `AICoreLayerClass`
  so EFT treats it as a built-in layer.
- `CustomLogicWrapper` — Internal bridge. Wraps `CustomLogic` as EFT's `BotNodeAbstractClass`.

**How it works:**
1. Mods call `BrainManager.AddCustomLayer(typeof(MyLayer), brainNames, priority)` at init.
2. BigBrain assigns numeric IDs starting at 9000 for both layers and logics.
3. Harmony patches intercept bot brain creation and inject `CustomLayerWrapper` instances.
4. EFT ticks all layers each frame; BigBrain bridges calls to `CustomLayer` methods.
5. Layer priority (higher number = checked first) determines which behavior runs.

**Custom IDs:** `START_LAYER_ID = 9000`, `START_LOGIC_ID = 9000`. Any logic ≥ 9000 is custom.

---

### 2. SAIN — Combat AI

**Role:** Full combat AI replacement. Handles vision, hearing, movement, cover, squad
coordination, suppression, and personality-driven behavior. Built entirely on BigBrain.

**Registration:** `BigBrainHandler.BrainAssignment.Init()` in `SAINPlugin.Awake()`:
- Registers 5 SAIN layer types per bot brain at priorities 60-99
- Removes ~15+ vanilla combat layers per bot type so they don't compete

**Bot tick groups** (inside `BotComponent.ManualUpdate()`):

| Group | Classes | When |
|-------|---------|------|
| `_alwaysTickClasses` | SAINActivationClass, SAINAILimit, SAINEnemyController, SAINDecisionClass, CurrentTargetClass | Every frame |
| `_tickWhenActiveClasses` | SAINBotUnstuckClass | Bot is active |
| `_tickWhenNoSleepClasses` | Vision, Hearing, Mover, Medical, Cover, Steering, Memory, Suppression, Search, Grenade, Extract, Flashlight, Aiming (~18 classes) | Bot not in standby |
| `_tickWhenCombatClasses` | SAINShootData, AimDownSightsController, SAINFriendlyFireClass | Bot in combat |

**SAIN layers registered via BigBrain:**

| Layer | Priority | Purpose |
|-------|----------|---------|
| DebugLayer | 99 | Debug mode (always wins) |
| SAINAvoidThreatLayer | 80 | Grenade/artillery avoidance |
| CombatSquadLayer | ~78 (config) | Squad combat coordination |
| CombatSoloLayer | ~77 (config) | Solo combat |
| ExtractLayer | ~74 (config) | Move to extract |

**AI Limit tiers** (distance-based throttling):

| Tier | Distance | Vision | Cover | Decision Rate |
|------|----------|--------|-------|---------------|
| None | < 150m | Full (30Hz) | Full (10Hz) | Full (10Hz) |
| Far | 150-250m | Reduced | Reduced (5Hz) | Reduced (5Hz) |
| VeryFar | 250-400m | Minimal | Disabled | Slow (3Hz) |
| Narnia | > 400m | Near-zero | Disabled | Slow (2Hz) |

---

### 3. LootingBots — Looting AI

**Role:** Adds automated bot looting behavior. Scans corpses, containers, and loose items,
navigates to them, and intelligently loots with gear comparison and inventory management.

**Registration** (in `LootingBots.Awake()`):
1. Removes vanilla `"Utility peace"` and `"LootPatrol"` layers
2. Registers `LootingLayer` via `BrainManager.AddCustomLayer()` with different priorities
   per bot type (scavs=4, PMCs=5, zombies=11, sectants=13 in vanilla, or unified ~62 in fork)

**Performance gates:**

| Gate | Default | Purpose |
|------|---------|---------|
| ActiveBotCache | 20 bots max | Caps total bots running looting logic |
| Distance gating | 0 (off) | Disables looting beyond N meters from player |
| ScanScheduler | 3 concurrent | Token-based concurrency limiter for scans |
| Scan Interval | 15s | Time between loot scans per bot |

**Interop API** (reflection-based, no hard dependency):
- `TryForceBotToScanLoot(botOwner)`
- `TryPreventBotFromLooting(botOwner, duration)`
- `CheckIfInventoryFull(botOwner)`
- `GetNetLootValue(botOwner)`

---

### 4. Waypoints — Expanded NavMesh

**Role:** Replaces vanilla Unity NavMesh with hand-authored, expanded NavMesh per map.
Gives bots access to rooftops, interiors, and off-path terrain. Also fixes door navigation.

**7 Harmony patches:**
1. `WaypointPatch` — Injects custom NavMesh at `BotsController.Init`
2. `FindPathPatch` — Replaces `BotPathFinderClass.FindPath` with reliable `NavMesh.CalculatePath()`
3. `DoorLinkPatch` — Fixes door navigation links
4. `DoorLinkStateChangePatch` — Updates nav links on door open/close
5. `SwitchDoorBlockerPatch` — Handle switch-operated doors
6. `ExfilDoorBlockerPatch` — Handle extraction doors
7. `DebugPatch` — Optional NavMesh/BotZone visualization

**Key:** No BigBrain dependency. Operates at Unity NavMesh level, below all behavior mods.

---

### 5. AILimit — Distance-Based Bot Deactivation

**Role:** Completely deactivates distant bots via `GameObject.SetActive(false)`,
eliminating all CPU cost. Complementary to SAIN's internal AI Limit (which throttles
subsystems rather than disabling bots entirely).

**Mechanism:**
- Runs every ~300 frames (configurable)
- Sorts bots by distance to nearest human player
- Activates closest `BotLimit` (default 10) bots
- Deactivates all others via `GameObject.SetActive(false)`
- New bots have a 10s spawn timer before eligibility

**Per-map distance configs:** Factory=80m, Labs=250m, all others=400m

---

### 6. ABPS — Bot Spawn Control

**Role:** Controls bot spawn and placement. Both client-side (13 Harmony patches) and
server-side (SPT DI config).

**Key functions:**
- Per-map bot caps (soft limits)
- PMC spawn distance checks
- Zone-based scav caps with hotzone support
- Progressive/regressive boss spawn chances
- Bot despawn mechanics (distance + timer)

**13 Harmony patches** override: bot creation, max count, PMC spawn hooks, scav groups,
non-wave spawning, zone spawning, boss chances, hostility, and death cleanup.

---

### 7. MoreBotsAPI — Custom Bot Types

**Role:** API for dynamic bot count scaling and custom bot type registration.
Both client (BepInEx) and server (SPT DI) components.

---

### 8. SAINPerfLog — Raid Telemetry

**Role:** Standalone BepInEx plugin separate from SAIN. Owns all performance logging.

**Responsibilities:**
- Per-raid performance CSV (timestamped, no overwrite)
- Optional BigBrain snapshot CSV
- F12 read-only display (FPS, budget stats, bot counts)
- Diagnostic logging toggle
- Communicates with SAIN via reflection-based interop (`SainPerfLogInterop`)

**Plugin identity:** `me.sol.sain.perflog` — soft dependency on SAIN.

---

### 9. OptimizationCore — Performance Infrastructure

**Role:** Shared library with contracts and reference types for the performance
infrastructure. Key interfaces:

| Interface/Type | Purpose |
|----------------|---------|
| `IBudgetedAI` | `ProcessAITick()`, `CurrentTier` property |
| `IOfflineSquad` | `TickOffline()`, squad ID, members, position |
| `PerceptionTier` | Enum: `Visible`, `Audible`, `Occluded` |
| `OfflineBotStats` | Stat block: weapon damage, armor, health, range |
| `OfflineCombatResult` | Resolution: casualties, winner, duration, zone center |

---

## Performance Infrastructure (Shipped Inside SAIN)

While `OptimizationCore/` defines the contracts, the actual shipped implementations
live inside `SAIN/SAIN/Components/`:

| Component | File | Role |
|-----------|------|------|
| **AIFrameBudgetScheduler** | `Components/AIFrameBudgetScheduler.cs` | 2ms hard budget cap per frame. Processes bots in Visible→Audible→Occluded order with time-sliced round-robin. Offline squads resolved first. |
| **SAINAILimit** (rewritten) | `Classes/Bot/SAINAILimit.cs` | Player-centric perception tiering (Visible/Audible/Occluded) replacing old distance-only tiers |
| **OfflineCombatResolver** | `Components/OfflineCombatResolver.cs` | Statistical AI-vs-AI combat resolution using bot power scores |
| **CombatAudioSpoofer** | `Components/CombatAudioSpoofer.cs` | Fake gunfire audio at combat zones with distance attenuation |
| **BotGameObjectPool** | `Components/BotGameObjectPool.cs` | Recycles bot GameObjects instead of destroy/create |
| **SquadCombatCoordinator** | `Layers/Combat/Squad/SquadCombatCoordinator.cs` | Squad-level target distribution, flanking, suppression assignment |
| **PerceptionSystem** | `Components/PerceptionSystem.cs` | Camera frustum + raycast for visibility; gunfire/sprint for audibility |
| **OfflineSquadWorldSync** | `Components/OfflineSquadWorldSync.cs` | Auto-registers offline squads from hostile bot groups |

### Frame Budget Flow

```
Each Frame (16.7ms at 60 FPS)
│
└── AIFrameBudgetScheduler.ProcessFrame()
    │
    ├── Phase 0: ResolveOfflineSquadCombat()  [≤1 Hz]
    │   └── CombatAudioSpoofer: spoofed gunfire
    │
    ├── Phase 1: Visible tier (45% of budget first)
    ├── Phase 2: Audible tier (cumulative ~88%)
    └── Phase 3: Occluded tier (remaining budget)
        └── Hard cap at MaxAIBudgetMs (default 2ms)
            → unfinished tiers resume next frame (round-robin)
```

---

## Reference-Only Mods

These mods exist at the repo root (`E:\spt-tarkov-ai\`) for study but are **not**
part of the shipped `OptimizedMod/` stack:

### SPTQuestingBots
- **Purpose:** Adds quest-driven bot behavior (patrol routes, quest objectives, navigation)
- **Reference value:** Study architecture, layer patterns, BigBrain interop, BotZone usage
- **Why separate:** Complex mod with its own build pipeline; keep as reference for copying patterns
- **Key learnings:** Uses BigBrain, has `brain_layer_priorities` config, proves BotZone access at runtime

### spt-unda
- **Purpose:** Server-side PMC wave generator replacement. Converts PMCs to "boss" spawns
  with dynamic group sizes, all zones open.
- **Why reference:** Complements ABPS; study server-side spawn generation patterns
- **Integration potential:** Can work alongside ABPS (Unda = wave gen, ABPS = client limits)

---

## Layer Priority Hierarchy

This is the **critical arbitration mechanism**. BigBrain checks layers in descending
priority; the first layer where `IsActive()` returns `true` controls the bot.

```
Priority 99:  SAIN DebugLayer                    ← Debug mode override
Priority 80:  SAIN AvoidThreatLayer              ← Grenade/artillery emergency
Priority ~78: SAIN CombatSquadLayer              ← Squad combat (config, max 79)
Priority ~77: SAIN CombatSoloLayer               ← Solo combat (config, max 77)
              ────────────────────────────────── ← SAIN extract must beat loot
Priority ~74: SAIN ExtractLayer                   ← Extraction behavior
              ────────────────────────────────── ← Loot threshold (fork default)
Priority ~62: LootingBots LootingLayer           ← Looting (fork unified default)
              ────────────────────────────────── ← Below loot
Priority ~50: EFT BotMind layers                  ← Vanilla patrol/wander
Priority 13:  LootingBots (Sectants)             ← Cultists (vanilla priority)
Priority 11:  LootingBots (Obdolbs/zombies)      ← Zombies (vanilla priority)
Priority 5:   LootingBots (PMCs/Rogues)          ← Vanilla PMC priority
Priority 4:   LootingBots (Scavs/Bosses)         ← Vanilla scav priority
```

**Key rule:** When SAIN combat layers are active (`IsActive() = true`), they win over
looting because their priorities (77-78) are higher than the loot layer (~62). Loot
runs only when SAIN combat and extract layers are inactive.

**Fork difference:** This fork sets `BigBrainLootLayerPriority` to a unified **62**
for all bot types, replacing the vanilla LootingBots scheme (4/5/11/13 per type).

---

## Initialization & Runtime Sequence

The order mods initialize and interact during a raid:

```
GAME START:
  1. SPT Core loads
  2. BigBrain patches activate (BepInEx plugin load order)
  3. Waypoints initializes (prepares navmesh data)
  4. SAIN initializes:
     a. Loads preset/config
     b. BigBrainHandler.Init():
        - Registers SAIN layers for all bot types (priorities 60-99)
        - Removes vanilla combat layers for all bot types
     c. Starts global coroutines (Vision, DirectionData, etc.)
  5. LootingBots initializes:
     a. Registers LootingLayer (priority ~62 in fork)
     b. Removes vanilla "Utility peace" and "LootPatrol" layers
     c. Initializes ItemAppraiser (async price fetching)
  6. SAINPerfLog initializes (hooks into SAIN via reflection)
  7. AILimit initializes (attaches component to GameWorld)
  8. ABPS loads server config and applies 13 Harmony patches

RAID START:
  9. Waypoints injects custom NavMesh (replaces Unity NavMesh data)
  10. Unda (if installed) generates PMC waves server-side
  11. ABPS enforces spawn caps and distance checks
  12. MoreBotsAPI scales bot count

PER FRAME (during raid):
  13. GameWorld.DoWorldTick() fires
  14. WorldTickPatch (Harmony postfix) → GameWorldComponent.WorldTick()
  15. BotManagerComponent.ManualUpdate():
      a. BotSpawnController.Update (spawn/despawn)
      b. TimeVision.Update, WeatherVision.Update
      c. BotSquads.Update (squad coordination)
      d. AIFrameBudgetScheduler.ProcessFrame():
         - ProcessOfflineSquads (statistical combat)
         - Visible tier → Audible tier → Occluded tier
         - Each BotComponent.ManualUpdate():
           → TickClassGroup(_alwaysTickClasses)
           → TickClassGroup(_tickWhenActiveClasses)
           → TickClassGroup(_tickWhenNoSleepClasses)
           → TickClassGroup(_tickWhenCombatClasses)
  16. BigBrain ticks bot brain:
      → Checks layers in priority order (highest first)
      → First active layer → GetNextAction() → EFT executes action
  17. AILimit (every ~300 frames): sorts bots by distance, deactivates farthest

RAID END:
  18. SAINPerfLog closes CSV writers
  19. GameWorld.OnDispose cleans up components
```

---

## Data Flow Diagrams

### BigBrain Layer Arbitration Flow

```
BotBrain tick (each frame)
  │
  ├── Layer 1 (priority 99): DebugLayer
  │   └── IsActive()? ──YES──▶ Takes control
  │       └── NO
  │
  ├── Layer 2 (priority 80): AvoidThreatLayer
  │   └── IsActive()? ──YES──▶ Takes control
  │       └── NO
  │
  ├── Layer 3 (priority ~78): CombatSquadLayer
  │   └── IsActive()? ──YES──▶ Takes control
  │       └── NO
  │
  ├── Layer 4 (priority ~77): CombatSoloLayer
  │   └── IsActive()? ──YES──▶ Takes control
  │       └── NO
  │
  ├── Layer 5 (priority ~74): ExtractLayer
  │   └── IsActive()? ──YES──▶ Takes control
  │       └── NO
  │
  └── Layer 6 (priority ~62): LootingLayer
      └── IsActive()? ──YES──▶ Takes control (peace/loot)
          └── NO
              └── Vanilla BotMind layers run (patrol/wander)
```

### SAIN Bot Perception → Budget Flow

```
Player Camera + Audio
        │
        ▼
PerceptionSystem / SAINAILimit
  ├── Is bot in camera frustum + raycast hit?  → Visible tier
  ├── Is bot firing/sprinting within range?    → Audible tier
  └── Otherwise?                               → Occluded tier
        │
        ▼
AIFrameBudgetScheduler.ProcessFrame()
  ├── Visible bots:    Process first (45% budget slice)
  ├── Audible bots:    Process second (cumulative 88%)
  ├── Occluded bots:   Process last (remaining budget)
  └── Budget exceeded? → Skip remaining, resume next frame
        │
        ▼
BotComponent.ManualUpdate(tier)
  ├── If Visible:   Full AI (30Hz vision, 10Hz cover, 10Hz decisions)
  ├── If Audible:   Reduced AI (10Hz vision, 5Hz cover, 5Hz decisions)
  └── If Occluded:  Minimal AI (5Hz nav only, no combat)
```

### Offline Squad Combat Flow

```
2 hostile squads both in Occluded tier (>200m from player)
        │
        ▼
AIFrameBudgetScheduler detects IOfflineSquad instances
        │
        ▼
OfflineCombatResolver.ResolveCombat(squadA, squadB)
  ├── CalculateSquadPower(A) vs CalculateSquadPower(B)
  ├── Random roll with ±30% variance
  ├── Determine winner, casualties, duration
  └── Return OfflineCombatResult
        │
        ▼
CombatAudioSpoofer.ScheduleCombatAudio(result)
  ├── Play gunfire shots at combat zone location
  ├── Volume attenuates with distance
  ├── Muffled pass beyond 200m
  └── Trailing burst (unless ambush)
        │
        ▼
IOfflineSquad.TickOffline() updates squad state
  ├── Remove casualties from member list
  ├── Track winning squad
  └── If one squad eliminated → stop combat
```

---

## Key Integration Points Table

| Integration Point | Source Mod | Target Mod | Method | Frequency |
|---|---|---|---|---|
| Combat layer registration | SAIN | BigBrain | `BrainManager.AddCustomLayer()` | Once at init |
| Looting layer registration | LootingBots | BigBrain | `BrainManager.AddCustomLayer()` | Once at init |
| Vanilla layer removal | SAIN, LootingBots | BigBrain | `BrainManager.RemoveLayer()` | Once at init |
| Behavior execution | BigBrain | SAIN, LootingBots | `CustomLayer.GetNextAction()` | Per decision |
| Action execution | BigBrain | SAIN, LootingBots | `CustomLogic.Update()` | Per frame when active |
| NavMesh pathfinding | SAIN, LootingBots | Waypoints | NavMesh data | Per movement request |
| Loot prevention | SAIN | LootingBots | `External.PreventBotFromLooting()` | On combat enter |
| Force loot scan | SAIN | LootingBots | `External.ForceBotToScanLoot()` | On combat exit |
| Layer priority arbitration | BigBrain | SAIN, LootingBots | Numeric priority | Per brain tick |
| Bot deactivation | AILimit | EFT Core | `GameObject.SetActive(false)` | Every ~300 frames |
| Spawn limit override | ABPS | EFT Core | 13 Harmony patches | Per spawn attempt |
| PMC wave generation | Unda | SPT Server | `PmcWaveGenerator` extension | Per raid start |
| Telemetry sampling | SAINPerfLog | SAIN | Reflection interop | Per frame / per CSV row |
| Budget scheduling | AIFrameBudgetScheduler | SAIN | `ProcessFrame()` → `BotComponent.ManualUpdate()` | Per frame |
| Perception tiering | SAINAILimit / PerceptionSystem | AIFrameBudgetScheduler | `UpdateBotTier()` | Per evaluation interval |
| Offline combat | OfflineCombatResolver | AIFrameBudgetScheduler | `ResolveCombat()` | ≤1 Hz |
| Audio spoofing | CombatAudioSpoofer | OfflineCombatResult | `ScheduleCombatAudio()` | Per combat resolution |
| Bot object pooling | BotGameObjectPool | SAIN | Harmony patches on Destroy/Spawn | Per bot spawn/despawn |
| Squad coordination | SquadCombatCoordinator | CombatSquadLayer | `CoordinateSquad()` | Per combat tick |
