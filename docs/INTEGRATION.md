# Mod Integration — SPT Tarkov AI Optimization

> **Code location:** All source code lives in `OptimizedMod/`. File paths in this document use original mod names for clarity — actual files are under `OptimizedMod/BigBrain/`, `OptimizedMod/SAIN/`, etc. spt-unda is not included in the fork.  
> This document maps how the AI mods in this workspace interoperate. It is designed to be extensible — as new mods are added, their integration points can be appended.

---

## Table of Contents

1. [Integration Overview](#integration-overview)
2. [How BigBrain Controls Tarkov's Unity Bot System](#how-bigbrain-controls-tarkovs-unity-bot-system)
3. [SAIN ↔ BigBrain](#sain--bigbrain)
4. [LootingBots ↔ BigBrain](#lootingbots--bigbrain)
5. [SAIN ↔ LootingBots](#sain--lootingbots)
6. [Waypoints ↔ Ecosystem](#waypoints--ecosystem)
7. [SPT-AILimit ↔ Ecosystem](#spt-ailimit--ecosystem)
8. [botplacementsystem (ABPS) ↔ Ecosystem](#botplacementsystem-abps--ecosystem)
9. [spt-unda ↔ Ecosystem](#spt-unda--ecosystem)
10. [Mod Compatibility Matrix](#mod-compatibility-matrix)
11. [Adding a New Mod](#adding-a-new-mod)
12. [Known Integration Points Table](#known-integration-points-table)
13. [Appendix: Registration Order](#appendix-registration-order)

---

## Integration Overview

```
┌───────────────────────────────────────────────────────────────────┐
│                    SPT Core (EFT)                                 │
│           Bot Brains, GameWorld, NavMesh                          │
└───────┬──────────────────┬──────────────────┬──────────┬──────────┘
        │ Harmony Patches  │                  │          │ Server DI
        ▼                  ▼                  ▼          ▼
┌───────────────┐  ┌──────────────┐    ┌─────────┐  ┌──────────┐
│   BigBrain    │  │  Waypoints   │    │AILimit  │  │ spt-unda │
│  (Framework)  │  │ (NavMesh)    │    │(Perf)   │  │PMC wave  │
│               │  │              │    │         │  │ generator│
│ BrainManager  │  │ Expanded     │    │SetActive│  │ zone open│
│ CustomLayer   │  │ navmesh data │    │(false)  │  └──────────┘
│ CustomLogic   │  └──────┬───────┘    └─────────┘
└───────┬───────┘         │
        │ Registers       │ Pathfinding          ┌──────────────────┐
        │ layers          │               ┌─────>│botplacementsystem│
        │                 ▼               │      │  Spawn caps     │
   ┌────┴────────────┐  ┌──────────────┐  │      │  Despawn        │
   ▼                 ▼  │              │  │      │  Boss chances   │
┌──────────┐   ┌──────────────┐        │  │      └──────────────────┘
│   SAIN   │   │ LootingBots  │◄───────┘  │
│(Combat)  │   │  (Looting)   │           │
│          │   │              │           │
│Priority: │   │Priority:     │           │
│ 60-99    │   │ 4-13         │           │
│          │   │              │           │
└──────────┘   └──────────────┘           │
                                          │
     Infrastructure layer ◄───────────────┘
     (Waypoints + AILimit + ABPS are NavMesh/GameObject-level,
      transparent to BigBrain layers)
```

The relationship is a **stack with priority-based arbitration**:

1. **SPT Core** provides the base game: `BotOwner`, brains (`AICoreLayerClass`), navmesh, physics
2. **BigBrain** patches into the brain system and provides the `BrainManager` API + `CustomLayer`/`CustomLogic` base classes
3. **Waypoints** provides expanded navmesh data for bot pathfinding
4. **SAIN** consumes BigBrain's API to register combat layers at priorities 60-99
5. **LootingBots** consumes BigBrain's API to register looting layers at priorities 4-13
6. **Infrastructure mods** operate below or alongside behavior mods:
  - **AILimit**: Unity-level bot deactivation via `GameObject.SetActive(false)` (below BigBrain, below NavMesh)
  - **ABPS**: Client-side spawn caps, despawn mechanics, boss chance tuning (spawn system level)
  - **Unda**: Server-side PMC wave generation (server level, before raid starts)

**Layer priority determines behavior precedence:**

- SAIN combat layers (60-99) always override LootingBots (4-13)
- When a bot enters combat, SAIN takes control and LootingBots yields
- When peaceful, LootingBots runs loot scans and SAIN's combat layers are inactive

---

## How BigBrain Controls Tarkov's Unity Bot System

Understanding BigBrain's control mechanism is critical for working with any of the behavior mods.
BigBrain uses **wrapper classes** and **Harmony patches** to make EFT's native bot brain system
execute custom C# code. Here is the full chain:

### The Problem BigBrain Solves

EFT's native bot brain system (`AICoreLayerClass<BotLogicDecision>`) decides bot behavior through
a fixed set of hardcoded layers and logic nodes. Adding new behavior types requires injecting into
this closed system. BigBrain provides the bridge.

### The Wrapper Architecture

```
BigBrain's CustomLayer / CustomLogic  (abstract C# classes, developer-friendly)
        │
        ▼  wrapped by
CustomLayerWrapper : AICoreLayerClass          CustomLogicWrapper : BotNodeAbstractClass
        │  (native EFT layer type)                   │  (native EFT logic node type)
        │                                            │
        ▼                                            ▼
   EFT Bot Brain (BotOwner.BotBrain)
   │
   └── BrainManager (BigBrain singleton)
        ├── Tracks all registered layers per brain
        └── Runs layer arbitration each tick
```

### Step-by-Step: How a Custom Layer Controls a Bot

**1. Registration** — Mod calls `BrainManager.AddCustomLayer(typeof(MyLayer), brainNames, priority)`:

- BigBrain assigns an internal numeric ID (`START_LAYER_ID = 9000` + counter)
- Stores the `Type`, brain list, and priority in its internal state
- If the target brain already exists in the bot's layer array, injects immediately

**2. Bot Spawn** — When a bot spawns, BigBrain's `BotBrainAddLayerPatch` fires:

- For each registered custom layer, checks if this bot's brain name matches
- If match: instantiates `CustomLayerWrapper(CustomLayer)` creating a real `AICoreLayerClass` instance
- This wrapper is inserted directly into the bot's brain layer dictionary
- EFT's native system treats it as a normal layer — it doesn't know it's a wrapper

**3. Layer Activation Check** — Each tick, EFT's brain calls `ShallUseNow()` on all layers:

- `CustomLayerWrapper.ShallUseNow()` → delegates to `customLayer.IsActive()`
- If the mod's custom layer reports active, BigBrain's wrapper returns true
- Highest-priority active layer wins (standard EFT arbitration)

**4. Action Selection** — When BigBrain's layer is chosen:

- EFT calls `GetNewAction()` on the wrapper (standard `AICoreLayerClass` method)
- `CustomLayerWrapper.GetNewAction()` → delegates to `customLayer.GetNextAction()` 
- The returned `CustomLogic` type is mapped to its registered ID (≥ 9000)
- This ID tells the brain which logic node to run

**5. Logic Execution** — EFT's brain creates a logic node for the action:

- `BotBrainCreateLogicNodePatch` intercepts logic node creation
- If the logic ID ≥ 9000, instead of creating a vanilla node, creates a `CustomLogicWrapper`
- `CustomLogicWrapper` holds a reference to the actual `CustomLogic` instance
- When EFT ticks this wrapper, it calls `customLogic.Update()`

**6. Action Ending** — Each tick, EFT checks `IsActionFinished()`:

- `CustomLayerWrapper.IsActionEnding()` → delegates to `customLayer.IsCurrentActionEnding()`
- When the mod reports the action is ending, BigBrain's wrapper returns true
- EFT's brain then asks all layers again: "Who's active now?" → cycle repeats

### The Priority Arbitration

Priority determines **which layer runs the bot**. BigBrain stores priority per registered layer:

```csharp
BrainManager.AddCustomLayer(typeof(CombatSoloLayer), "pmcBrain", 60);
BrainManager.AddCustomLayer(typeof(LootingLayer),     "pmcBrain", 5);
```

When both layer wrappers exist on the same bot brain:

1. EFT checks `ShallUseNow()` on CombatSoloLayer → SAIN says yes (bot sees enemy)
2. EFT checks `ShallUseNow()` on LootingLayer → LootingBots says yes too
3. EFT picks the **higher priority** layer (60 > 5) → CombatSoloLayer controls the bot

This is why layer priorities **must be carefully chosen** — they are the sole arbitration mechanism.

### Key Files


| File                                                     | Role                                                                |
| -------------------------------------------------------- | ------------------------------------------------------------------- |
| `SPT-BigBrain/Internal/CustomLayerWrapper.cs`            | Wraps `CustomLayer` as `AICoreLayerClass` for EFT compatibility     |
| `SPT-BigBrain/Internal/CustomLogicWrapper.cs`            | Wraps `CustomLogic` as `BotNodeAbstractClass` for EFT compatibility |
| `SPT-BigBrain/Patches/BotBrainAddLayerPatch.cs`          | Injects wrapper layers into bot brain on spawn                      |
| `SPT-BigBrain/Patches/BotBrainCreateLogicNodePatch.cs`   | Redirects logic creation to `CustomLogicWrapper`                    |
| `SPT-BigBrain/Patches/BotBaseBrainActivateLayerPatch.cs` | Handles layer start/stop lifecycle                                  |
| `SPT-BigBrain/Brains/BrainManager.cs`                    | Singleton registry for all layers and logics                        |


### Next Sections

The following sections detail how specific mods (SAIN, LootingBots) use this mechanism.

---

## SAIN ↔ BigBrain

### Dependency Chain

```
SAIN depends on:
  ├── BigBrain (hard dependency — must be installed)
  └── Waypoints (hard dependency — must be installed)
```

SAIN's `README.md` explicitly lists BigBrain as a requirement. SAIN cannot function without
BigBrain because its entire behavior system is built on top of `CustomLayer` and `BrainManager`.

### Registration Flow

When SAIN initializes (`SAINPlugin.Awake()`), it calls `BigBrainHandler.Init()` which
executes `BigBrainHandler.BrainAssignment.Init()`. This method performs **two key operations**
for every bot type:

#### 1. Add Custom Layers (SAIN's behavior layers)

SAIN registers its layers via `BrainManager.AddCustomLayer()` for each bot brain type:

```csharp
// Example for PMCs (from BigBrainHandler.cs):
BrainManager.AddCustomLayer(typeof(DebugLayer),          pmcBrain, 99);
BrainManager.AddCustomLayer(typeof(SAINAvoidThreatLayer), pmcBrain, 80);
BrainManager.AddCustomLayer(typeof(ExtractLayer),         pmcBrain, settings.SAINExtractLayerPriority);
BrainManager.AddCustomLayer(typeof(CombatSquadLayer),     pmcBrain, settings.SAINCombatSquadLayerPriority);
BrainManager.AddCustomLayer(typeof(CombatSoloLayer),      pmcBrain, settings.SAINCombatSoloLayerPriority);
```

The `brainNames` parameter determines which vanilla EFT brains get the SAIN layers injected.
For example, PMCs use brains like `"PmcBear"`, `"PmcUsec"`, etc.

#### 2. Remove Vanilla Layers (disable default EFT combat behavior)

SAIN then removes the vanilla combat layers so they don't compete with SAIN's behavior:

```csharp
// Vanilla layers removed for all bot types:
string[] _commonVanillaLayersToRemove = [
    "Help", "AdvAssaultTarget", "AssaultEnemyFar", "Hit",
    "Simple Target", "Pmc", "AssaultHaveEnemy", "Assault Building",
    "Enemy Building", "PushAndSup", "Pursuit",
];
```

Each bot type has additional type-specific vanilla layers removed (e.g., boss fight layers,
knight fight layers, etc.).

### Runtime Lifecycle

```
1. Game loads → BigBrain patches activate
   │
2. SAINPlugin.Awake()
   │  → BigBrainHandler.BrainAssignment.Init()
   │     → AddCustomLayersToPMCs/Scavs/Raiders/Bosses/... 
   │     → ToggleVanillaLayers for each bot type (remove vanilla, restore SAIN)
   │
3. During raid, each bot spawn:
   │  BotSpawnController detects spawn event
   │  → Creates BotComponent (SAIN's per-bot instance)
   │  → BotComponent subscribes to BigBrain layer system
   │
4. Each frame, BotComponent.ManualUpdate():
   │  → TickClassGroup → each SAIN subsystem updates
   │  → Vision, Hearing, Cover, Decision, Movement, etc.
   │
5. BigBrain's BrainManager ticks the bot's brain:
   │  → For each CustomLayer (SAIN layer):
   │     → Calls IsActive() on SAINLayer
   │     → If active: GetNextAction() → returns a BotAction
   │     → EFT executes the action via CustomLogicWrapper
   │     → Action.Update() runs SAIN's behavior logic
```

### Layer-Level Integration

SAIN layers integrate at **three distinct interfaces** of BigBrain:

#### Interface 1: `CustomLayer` (via `SAINLayer`)

SAIN defines `SAINLayer : CustomLayer` as its base class. Each SAIN behavior layer
(CombatSoloLayer, CombatSquadLayer, etc.) extends `SAINLayer`.

BigBrain calls these methods on `SAINLayer`:

- `IsActive()` — SAIN checks conditions (e.g., is bot in combat? does it see an enemy?)
- `GetNextAction()` — SAIN returns a `BotAction` (extends `CustomLogic<T>`)
- `IsCurrentActionEnding()` — SAIN signals when the current action should be replaced
- `Start()` / `Stop()` — Lifecycle hooks

#### Interface 2: `CustomLogic<T>` (via `BotAction`)

SAIN defines `BotAction : CustomLogic<T>` as its base action class. Each specific action
(DogFightAction, SearchAction, RushEnemyAction, etc.) extends `BotAction`.

BigBrain calls:

- `Update(ActionData data)` — executes the behavior each tick
- `Start()` / `Stop()` — action lifecycle hooks

#### Interface 3: `BrainManager`

SAIN uses `BrainManager` for:

- `AddCustomLayer()` — register SAIN layers at init time
- `RemoveLayer()` — disable vanilla EFT layers
- `RestoreLayer()` — re-enable vanilla layers (for config toggles)
- `GetActiveLayer()` — debug/diagnostic queries
- `IsCustomLayerActive()` — check if SAIN currently controls the bot

### Compatibility Guarantees

From the performance optimization architecture (`PERFORMANCE_ARCHITECTURE.md`), SAIN's optimizations are designed to be compatible with
BigBrain:


| SAIN Change                                                       | BigBrain Impact | Reason                                                                                      |
| ----------------------------------------------------------------- | --------------- | ------------------------------------------------------------------------------------------- |
| Coroutine throttling (Vision, DirectionData, EnemyPlace, Unstuck) | None            | BigBrain manages layer execution order; SAIN coroutines are internal timing mechanisms      |
| `TickClassGroup ShallTick()` wiring                               | None            | BigBrain's `BotLayerPriority` and `CustomBotLayer` decisions are unaffected                 |
| `ShallCheckLook`/`ShallCheckLoS` merge                            | None            | BigBrain calls `SAINBotSearchData` which uses SAIN's enemy info; method signature unchanged |
| Job System batching (Squad Raycasts)                              | None            | `RaycastCommand.ScheduleBatch` is Unity-side; BigBrain layers see the same squad data       |
| `PerformanceSettings` additions                                   | None            | Settings are opt-in, defaulted to original values; no breaking changes                      |


**Key principle**: SAIN never modifies BigBrain's public API or Harmony patches. All SAIN
changes are internal to SAIN's own subsystems.

---

## LootingBots ↔ BigBrain

### Dependency Chain

```
LootingBots depends on:
  └── BigBrain (hard dependency — must be installed)
```

LootingBots declares `[BepInDependency("xyz.drakia.bigbrain", "1.4.0")]` and uses
`BrainManager.AddCustomLayer()` and `BrainManager.RemoveLayer()` at init.

### Registration Flow

When LootingBots initializes (`LootingBots.Awake()`), it performs three operations:

#### 1. Remove Vanilla Looting Layers

```csharp
BrainManager.RemoveLayer(
    "Utility peace",
    ["Assault", "ExUsec", "BossSanitar", "CursAssault", "PMC", "PmcUsec", "PmcBear",
     "ExUsec", "ArenaFighter", "SectantWarrior"]
);
BrainManager.RemoveLayer("LootPatrol", ["Assault", "PmcUsec", "PmcBear"]);
```

This disables the vanilla EFT peaceful-looting and patrol-looting behavior.

#### 2. Register `LootingLayer` with bot-type-specific priorities

Unlike SAIN which uses one priority per layer type, LootingBots assigns **different priorities
per bot type** for the same `LootingLayer`:

```csharp
// Priority 4: Scavs, Bosses, Followers, Goons — combat easily overrides
BrainManager.AddCustomLayer(typeof(LootingLayer), [/* scavs, bosses, followers */], 4);

// Priority 5: PMCs, Rogues, ArenaFighters — combat still overrides
BrainManager.AddCustomLayer(typeof(LootingLayer), ["PMC", "PmcUsec", "PmcBear", "ExUsec", "ArenaFighter"], 5);

// Priority 11: Zombies — loot aggressively
BrainManager.AddCustomLayer(typeof(LootingLayer), ["Obdolbs"], 11);

// Priority 13: Sectants — highest looting priority
BrainManager.AddCustomLayer(typeof(LootingLayer), ["SectantWarrior"], 13);
```

#### 3. Per-Bot Component Initialization

On bot spawn, `LootingLayer`'s constructor:

- Creates `LootingBrain` and `LootFinder` as `MonoBehaviour` components on the bot's GameObject
- `LootingBrain.Start()` initializes `ActiveBotCache`, `ScanScheduler`, and `ActiveLootCache`
- The bot's looting brain may be disabled for performance (distance/active count limits)

### Layer Integration

LootingBots extends `CustomLayer` **directly** (no intermediate abstract class like SAIN's `SAINLayer`):

```
CustomLayer (BigBrain)
  ├── SAINLayer (SAIN, abstract)
  │   ├── CombatSoloLayer
  │   ├── CombatSquadLayer
  │   └── ...
  │
  └── LootingLayer (LootingBots)  ← Direct extension
```

LootingBots uses three `CustomLogic` subclasses:

- `LootingLogic : CustomLogic` — Navigate to and interact with loot
- `FindLootLogic : CustomLogic` — Trigger a loot scan
- `PeacefulLogic : CustomLogic` — Passthrough to vanilla patrol behavior

---

## SAIN ↔ LootingBots

### Dependency Chain

SAIN and LootingBots are **siblings** — neither depends on the other. Both depend on BigBrain.
They coexist via BigBrain's priority-based layer arbitration.

### Layer Priority Arbitration

When multiple BigBrain layers want to be active, they're checked in **priority order**
(higher number = checked first):


| Priority | Layer                | Mod             | When Active                                |
| -------- | -------------------- | --------------- | ------------------------------------------ |
| 99       | DebugLayer           | SAIN            | Debug mode                                 |
| 80       | SAINAvoidThreatLayer | SAIN            | Grenade/artillery nearby                   |
| ~65      | ExtractLayer         | SAIN            | Bot wants to extract                       |
| ~60-70   | CombatSquadLayer     | SAIN            | Squad combat                               |
| ~60-70   | CombatSoloLayer      | SAIN            | Solo combat                                |
| **5**    | **LootingLayer**     | **LootingBots** | **PMCs/Rogues peaceful + loot available**  |
| **4**    | **LootingLayer**     | **LootingBots** | **Scavs/Bosses peaceful + loot available** |
| 13       | LootingLayer         | LootingBots     | Sectants (cultists loot aggressively)      |


**Result:** During combat, SAIN's layers (60+) always override LootingBots (4-5). Looting only
occurs when no higher-priority SAIN layer is active.

### Interop API (Cross-Mod Communication)

LootingBots exposes an `External` API and a reflection-based `LootingBotsInterop` class that
SAIN (or any mod) can use without a hard dependency:

```csharp
// Any mod can check and use LootingBots without referencing its DLL:
if (LootingBotsInterop.IsLootingBotsLoaded())
{
    LootingBotsInterop.Init();

    LootingBotsInterop.TryPreventBotFromLooting(botOwner, 30f);  // Stop looting in combat
    LootingBotsInterop.TryForceBotToScanLoot(botOwner);           // Force scan after combat
    bool isFull = LootingBotsInterop.CheckIfInventoryFull(botOwner);  // Check for extract
}
```

### Conflict Resolution


| Area                       | Potential Conflict                       | Resolution                                                                                             |
| -------------------------- | ---------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| **Vanilla layer removal**  | Both mods remove layers from same brains | Each removes different layer names — no collision                                                      |
| **Bot movement**           | Both may issue move commands             | BigBrain priority ensures only one layer is active at a time                                           |
| **Bot steering**           | LootingBots sets `LookToPoint()`         | Only runs when LootingLayer is the active layer                                                        |
| **Inventory modification** | Both modify bot inventory                | SAIN manages weapons in combat; LootingBots manages loot in peace                                      |
| **Performance overhead**   | Both add per-bot CPU cost                | LootingBots' `MaxActiveLootingBots` and distance gating prevent overlap with SAIN's taxed frame budget |


### Design Harmony

1. **SAIN handles combat** (priority 60-99): Vision, shooting, cover, squad tactics
2. **LootingBots handles peace** (priority 4-13): Scanning, navigating, inventory management
3. **BigBrain arbitrates** via priority: combat always wins
4. **Both share Waypoints** for NavMesh pathfinding data

---

## Waypoints ↔ Ecosystem

Waypoints is a **foundational infrastructure mod** — it enhances the Unity NavMesh that all
behavior mods depend on. It does not use BigBrain.

### Dependency Chain

- **Depended on by**: SAIN (declared dependency in `SAINPlugin.cs`)
- **Used by**: LootingBots, AILimit, ABPS (implicit — they all pathfind via NavMesh)
- **No dependency on**: BigBrain, SAIN, or any behavior mod

### Integration Points

1. **NavMesh Injection**: Patches `BotsController.Init` → replaces entire NavMesh with custom
  map-specific data (`NavMesh.RemoveAllNavMeshData()` + `NavMesh.AddNavMeshData()`)
2. **Pathfinding Override**: Patches `BotPathFinderClass.FindPath` → uses
  `NavMesh.CalculatePath()` instead of EFT's buggy internal pathfinder
3. **Door Navigation Fixes**: 4 patches (`DoorLink`, `DoorLinkStateChange`, `SwitchDoorBlocker`,
  `ExfilDoorBlocker`) fix broken door navigation links

### What It Means for Behavior Mods

- SAIN bots have access to more pathfinding surface (rooftops, interiors, off-path terrain)
- LootingBots can pathfind to loot in areas the vanilla NavMesh didn't cover
- AILimit deactivation is NavMesh-agnostic (operates on GameObject active state)
- Transparent to BigBrain layers — no layer priority consideration needed

---

## SPT-AILimit ↔ Ecosystem

SPT-AILimit is a **performance mod** that completely deactivates distant bots. It operates at
the Unity GameObject level, below the behavior layer system.

### Dependency Chain

- **Depends on**: SPT Core 4.0+, Fika (soft dependency)
- **No dependency on**: BigBrain, SAIN, or any behavior mod
- **Compatible with**: All mods (operates on `GameObject.SetActive()` independent of behavior)

### Integration Points

1. **Bot Activation/Deactivation**: `AILimitComponent` subscribes to `botSpawnerClass.OnBotCreated`
  and `OnBotRemoved`. On each update cycle (~300 frames), it sorts all bots by distance to nearest
   human player, activates the closest `BotLimit` bots, and deactivates the rest via
   `GameObject.SetActive(false)`.
2. **BotStandBy Integration**: Before deactivation, calls `standBy.method_1()` (standby pause)
  and sets `standBy.NextCheckTime = Time.time + 1000f` to prevent wake-up.
3. **Spawn Timer**: New bots enter an `eligibleNow = false` state with a Timer that fires after
  `TimeAfterSpawn` (default 10s), preventing premature deactivation of freshly spawned bots.
4. **Patch Safety**: Patches `Player.ComplexUpdate` to skip inactive GameObjects entirely,
  preventing unnecessary code paths.

### Relationship with SAIN's Internal AI Limit


| Aspect          | SPT-AILimit                                | SAIN AI Limit (internal)                      |
| --------------- | ------------------------------------------ | --------------------------------------------- |
| Mechanism       | `GameObject.SetActive(false)` — binary off | Bot tier throttling (None/Far/VeryFar/Narnia) |
| CPU impact      | Zero (disabled objects don't tick)         | Reduced (subsystems skip work)                |
| Distance config | Per-map distances (80-400m)                | Tiers: Far=100m, VeryFar=200m, Narnia=300m    |
| Re-evaluation   | Every ~~300 frames (~~5s)                  | Per frame (ShallTick)                         |
| Bot count limit | `BotLimit` (default 10)                    | N/A (throttles, doesn't disable)              |


**Recommendation**: Can coexist. AILimit handles distant bot cleanup; SAIN's limit handles
nearby bot throttling. Set SAIN's AI Limit to "None" if AILimit is enabled, or use both for
maximum performance.

---

## botplacementsystem (ABPS) ↔ Ecosystem

ABPS is a **bot spawn and placement control mod** with client-side Harmony patches and
server-side configuration. It controls how many bots exist, where they spawn, and at what
distances.

### Dependency Chain

- **Depends on**: SPT Core 4.0+, Fika headless (soft dependency)
- **No dependency on**: BigBrain, SAIN, or any behavior mod
- **Compatible with**: All behavior mods (spawn control is independent of behavior logic)
- **Potential conflict with**: spt-unda (both modify PMC spawn mechanics)

### Integration Points

1. **Map Bot Caps**: Overrides max bot counts via `SetMaxBotCountPatch` and
  `BotsControllerInitPatch`. Limits total bots per map via per-map static config fields.
2. **Spawn Zone Control**: `TryToSpawnInZonePatch` enforces zone-based scav caps.
3. **Despawn Mechanics**: `DespawnFurthest` + `DespawnDistance` + `DespawnTimer` config fields
  control auto-despawn of distant bots (complementary to AILimit's deactivation).
4. **PMC Spawn Distance**: `PmcSpawnHookPatch` enforces per-map `PmcSpawnDistanceCheck` —
  PMCs can only spawn if they're beyond the minimum distance from players.
5. **Boss Chances**: `BossProgressiveRegressivePatch` implements dynamic boss spawn
  probability (chances increase on boss kill, decrease on boss spawn).
6. **Bot Creation Hook**: `BotOwnerCreationPatch` fires when a bot is created, allowing early
  state initialization before BigBrain layers activate.
7. **Assault Groups**: `AssaultGroupPatch` controls scav assault group behavior.
8. **Non-Wave Spawning**: `NonWavesSpawnSystemPatch` overrides the non-wave bot spawn system
  with custom logic using `Plugin.SoftCap` instead of map max bots.

### Interaction with Unda

ABPS and Unda both modify PMC spawning but at different levels:


| Aspect        | ABPS                                        | Unda                                        |
| ------------- | ------------------------------------------- | ------------------------------------------- |
| Layer         | Client-side (BepInEx)                       | Server-side (SPT DI)                        |
| Mechanism     | Harmony patches into EFT spawn methods      | Replaces `PmcWaveGenerator` server class    |
| PMC spawns    | Distance checks, map limits, despawn        | Wave generation, zone opening, group config |
| Boss spawns   | Progressive/regressive chances              | N/A                                         |
| Conflict risk | **Medium** — both touch PMC spawn mechanics | **Medium** — conflicting overrides possible |


**Recommendation**: If using both, Unda should take precedence for PMC wave generation (server-side
overhaul), while ABPS handles per-map limits and distance checks (client-side enforcement).

---

## spt-unda ↔ Ecosystem

Unda is a **server-side PMC spawn overhaul** that replaces the vanilla PMC wave generation.
It is the only server-only mod in this workspace.

### Dependency Chain

- **Depends on**: SPT Server (server-side DI container)
- **No client plugin**: No BepInEx, no Harmony patches, no BigBrain dependency
- **Compatible with**: All client-side mods (spawning happens before behavior runs)
- **Potential conflict with**: botplacementsystem (both modify PMC spawn systems)

### Integration Points

1. **Zone Opening**: `Data.cs` reads all spawn zones per map, excludes sniper zones, and sets
  `location.Base.OpenZones` to a comma-separated list of all zones — allowing PMCs to spawn
   anywhere on the map.
2. **PMC Wave Generation**: `PmcWaveGeneratorEx` extends SPT's `PmcWaveGenerator`:
  - Deletes existing `pmcBEAR`/`pmcUSEC` boss entries
  - Clears custom wave configs
  - Generates new PMC groups as boss spawns with `IgnoreMaxBots = true`
  - Group count = `minPlayers - 1` (reserving one slot for the human player)
  - Groups are dynamically split (1 to `MaxPmcGroupSize`)
3. **Raid Time Adjustments**: `RaidTimeAdjustmentServiceEx` provides extended raid time
  handling to accommodate the larger PMC presence.

### Impact on Other Mods


| Mod             | Impact                                                                                                                                 |
| --------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| **ABPS**        | Both modify PMC spawns. Unda generates waves server-side; ABPS enforces limits client-side. See ABPS section for conflict analysis.    |
| **SAIN**        | No direct interaction. SAIN handles behavior of whatever bots are spawned, regardless of spawn origin. More PMCs = more SAIN CPU cost. |
| **AILimit**     | More PMCs = more bots to deactivate. Each additional PMC is eligible for AILimit deactivation once beyond distance threshold.          |
| **BigBrain**    | No integration. BigBrain is bot-system-level; Unda is pre-bot (spawn generation).                                                      |
| **LootingBots** | More PMCs = more potential loot sources (corpses). Indirect positive effect on loot availability.                                      |


---

## Mod Compatibility Matrix

This section tracks how each mod interacts with others. Mark new entries as mods are added.


| Mod                    | Depends On             | Integrates Via                                                | Conflicts With                                  | Notes                                     |
| ---------------------- | ---------------------- | ------------------------------------------------------------- | ----------------------------------------------- | ----------------------------------------- |
| **BigBrain**           | SPT Core 4.0+          | Direct Harmony patches                                        | None                                            | Framework; other mods depend on it        |
| **SAIN**               | BigBrain, Waypoints    | `BrainManager.AddCustomLayer()`, `CustomLayer`, `CustomLogic` | Vanilla EFT combat layers (explicitly removed)  | Replaces all bot combat behavior          |
| **LootingBots**        | BigBrain               | `BrainManager.AddCustomLayer()`, `CustomLayer`, `CustomLogic` | Vanilla `"Utility peace"`, `"LootPatrol"`       | Adds bot looting during peace state       |
| **Waypoints**          | SPT Core               | NavMesh data + pathfinding patches                            | None                                            | Expanded NavMesh for all mods             |
| **SPT-AILimit**        | SPT Core (Fika soft)   | `MonoBehaviour` on GameWorld, `GameObject.SetActive(false)`   | None                                            | Disables distant bots to save CPU         |
| **botplacementsystem** | SPT Core (Fika soft)   | 13 Harmony patches into spawn system                          | **Unda** (both modify PMC spawns)               | Map limits, spawn distances, boss chances |
| **spt-unda**           | SPT Core (server-only) | Replaces `PmcWaveGenerator`                                   | **botplacementsystem** (both modify PMC spawns) | PMCs as boss groups, all zones open       |
| *(future mod)*         | —                      | —                                                             | —                                               | —                                         |


---

## Adding a New Mod

When a new mod is added to this workspace, document its integration here:

### Template: New Mod Entry

```markdown
## ModName ↔ Ecosystem

### Dependency Chain
- Depends on: [list required mods]
- Optionally integrates with: [list optional integrations]

### Integration Points
1. **Registration**: How does it hook in? (BigBrain layers? Harmony patches? Direct API?)
2. **Runtime**: When and how does it interact with other mods per frame?
3. **Data Sharing**: What data does it read from / write to other mods?

### Layer Priority
- Custom layer(s) registered at priority: X
- Relative to SAIN: [above / below / parallel]

### Potential Conflicts
- [Any systems or layers that might overlap]

### Performance Impact
- [Known CPU/GC cost, if assessed]
```

---

## Known Integration Points Table


| Integration Point            | Source Mod        | Target Mod        | Method                                        | Frequency               |
| ---------------------------- | ----------------- | ----------------- | --------------------------------------------- | ----------------------- |
| Combat layer registration    | SAIN              | BigBrain          | `BrainManager.AddCustomLayer()`               | Once at init            |
| Looting layer registration   | LootingBots       | BigBrain          | `BrainManager.AddCustomLayer()`               | Once at init            |
| Vanilla combat layer removal | SAIN              | BigBrain          | `BrainManager.RemoveLayer()`                  | Once at init            |
| Vanilla loot layer removal   | LootingBots       | BigBrain          | `BrainManager.RemoveLayer()`                  | Once at init            |
| Behavior execution           | BigBrain          | SAIN              | `CustomLayer.GetNextAction()`                 | Per decision (~10Hz)    |
| Loot behavior execution      | BigBrain          | LootingBots       | `CustomLayer.GetNextAction()`                 | Per loot scan (~0.07Hz) |
| Action ticking               | BigBrain          | SAIN              | `CustomLogic.Update()`                        | Per frame when active   |
| Loot action ticking          | BigBrain          | LootingBots       | `CustomLogic.Update()`                        | Per frame when looting  |
| Bot spawn tracking           | SAIN              | BigBrain          | `BrainManager.ActivatedBots`                  | Per spawn/despawn       |
| NavMesh pathfinding          | SAIN, LootingBots | Waypoints         | Waypoints navmesh data                        | Per movement request    |
| Squad visibility             | SAIN              | BigBrain          | Layer-agnostic (Unity job system)             | ~1Hz                    |
| Loot prevention (interop)    | SAIN              | LootingBots       | `External.PreventBotFromLooting()`            | On combat enter         |
| Force loot scan (interop)    | SAIN              | LootingBots       | `External.ForceBotToScanLoot()`               | On combat exit          |
| Inventory check (interop)    | SAIN              | LootingBots       | `External.CheckIfInventoryFull()`             | On extract decision     |
| Net loot value (interop)     | SAIN              | LootingBots       | `External.GetNetLootValue()`                  | On raid end             |
| Layer priority arbitration   | BigBrain          | SAIN, LootingBots | Layer priority comparison (60+ vs 4-13)       | Per brain tick          |
| NavMesh injection            | Waypoints         | EFT Core          | `NavMesh.AddNavMeshData()`                    | Per raid start          |
| Pathfinding override         | Waypoints         | EFT Core          | `NavMesh.CalculatePath()` in `FindPath` patch | Per path request        |
| Bot deactivation (distance)  | AILimit           | EFT Core          | `GameObject.SetActive(false)`                 | Every ~300 frames       |
| Spawn limit override         | ABPS              | EFT Core          | `SetMaxBotCount`, `TryToSpawnInZone` patches  | Per spawn attempt       |
| PMC wave generation          | Unda              | SPT Server        | `PmcWaveGenerator.ApplyWaveChangesToMap()`    | Per raid start          |


---

## Appendix: Registration Order

The order in which mods initialize matters. The current initialization order is:

```
1. SPT Core loads
2. BigBrain patches activate (BepInEx plugin load order)
3. Waypoints initializes navmesh data
4. SAIN initializes:
   a. Loads preset/config
   b. Calls BigBrainHandler.Init()
   c. BigBrainHandler.BrainAssignment.Init():
      - Registers SAIN layers for all bot types (priorities 60-99)
      - Removes vanilla combat layers for all bot types
      - Registers with BotSpawnController for spawn events
   d. Starts global coroutines (Vision, DirectionData, etc.)
5. LootingBots initializes:
   a. Registers LootingLayer for all bot types (priorities 4-13)
   b. Removes vanilla "Utility peace" and "LootPatrol" layers
   c. Initializes ItemAppraiser (async price fetching)
6. Raid begins → bots spawn:
   a. SAIN creates BotComponent per bot (combat AI)
   b. LootingBots creates LootingBrain + LootFinder per bot (looting AI)
   c. BigBrain manages layer priority: SAIN (60+) always wins over LootingBots (4-13)
7. SPT-Waypoints initializes:
   a. Injects custom NavMesh per map (replaces Unity NavMesh data)
   b. Patches pathfinding with more reliable NavMesh.CalculatePath()
   c. Fixes door navigation links
8. SPT-AILimit initializes:
   a. Attaches AILimitComponent to GameWorld
   b. Tracks bot spawns, measures distances, activates/deactivates per frame interval
9. botplacementsystem initializes:
   a. Loads server config into static fields
   b. Patches bot spawn system with 13 Harmony patches
10. spt-unda initializes (server-side):
   a. Opens all bot zones on all maps
   b. Replaces PMC wave generation with dynamic boss-spawn groups
```

