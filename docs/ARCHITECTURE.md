# Workspace Architecture — SPT Tarkov AI Optimization

> **Code location:** All source code lives in `OptimizedMod/`. The architecture described below uses the original mod names (e.g., "SPT-BigBrain") for clarity — actual files are under `OptimizedMod/BigBrain/`, `OptimizedMod/SAIN/`, etc. spt-unda is not included in the fork.  
> This document details the internal architecture of the forked mods plus the new **OptimizationCore** performance infrastructure. It serves as a reference for understanding, modifying, and optimizing the AI pipeline.

**Agents:** [INDEX.md](../INDEX.md) · [AGENTS.md](AGENTS.md).

---

## Table of Contents

1. [SPT-BigBrain (DrakiaXYZ-BigBrain)](#spt-bigbrain)
  - [Purpose](#bigbrain-purpose)
  - [Core Architecture](#bigbrain-core-architecture)
  - [Key Classes](#bigbrain-key-classes)
  - [Harmony Patch Strategy](#bigbrain-harmony-patches)
  - [Layer Lifecycle](#bigbrain-layer-lifecycle)
2. [SAIN (Solarint's AI Modifications)](#sain)
  - [Purpose](#sain-purpose)
  - [High-Level Tick Flow](#sain-tick-flow)
  - [Component Architecture](#sain-component-architecture)
  - [Bot Tick Groups](#sain-bot-tick-groups)
  - [Layer/Action System](#sain-layer-action-system)
  - [AI Limit System](#sain-ai-limit-system)
  - [Performance Hotspots](#sain-performance-hotspots)
3. [SPT-LootingBots](#spt-lootingbots)
  - [Purpose](#lootingbots-purpose)
  - [Architecture Overview](#lootingbots-architecture)
  - [Layer/Action System](#lootingbots-layer-action-system)
  - [Loot Scan & Decision Flow](#lootingbots-decision-flow)
  - [Key Classes](#lootingbots-key-classes)
  - [Interop API](#lootingbots-interop-api)
  - [Performance Design](#lootingbots-performance)
4. [SPT-Waypoints](#spt-waypoints)
  - [Purpose](#waypoints-purpose)
  - [Architecture](#waypoints-architecture)
  - [Key Classes & Patches](#waypoints-key-classes)
5. [SPT-AILimit](#spt-ailimit)
  - [Purpose](#ailimit-purpose)
  - [Architecture](#ailimit-architecture)
  - [Key Classes](#ailimit-key-classes)
6. [botplacementsystem (ABPS)](#botplacementsystem)
  - [Purpose](#abps-purpose)
  - [Architecture](#abps-architecture)
  - [Key Configuration](#abps-key-config)
7. [spt-unda](#spt-unda)
  - [Purpose](#unda-purpose)
  - [Architecture](#unda-architecture)

---

## SPT-BigBrain

### BigBrain Purpose

BigBrain is a **framework mod** — it provides the infrastructure for other mods to inject custom
AI behavior layers and logic into Escape From Tarkov's bot brain system. It does not define any
bot behavior itself; it provides the hooks, wrappers, and management layer that other mods
(like SAIN) consume.

### BigBrain Core Architecture

```
                    ┌──────────────────────────┐
                    │     BrainManager          │
                    │  (Singleton Registry)      │
                    │                           │
                    │  CustomLayers  : Dict<>   │ ← Mods register CustomLayer types here
                    │  CustomLogics  : Dict<>   │ ← Maps CustomLogic types → IDs
                    │  ExcludeLayers : List<>   │ ← Vanilla layers to remove per-brain
                    │  ExcludedLayers: List<>   │ ← Cached removed layers (for restore)
                    │  ActivatedBots : Dict<>   │ ← Tracked spawned bots
                    └───────────┬──────────────┘
                                │
        ┌───────────────────────┼───────────────────────┐
        │                       │                       │
        ▼                       ▼                       ▼
┌───────────────┐    ┌──────────────────┐    ┌──────────────────┐
│ CustomLayer   │    │ CustomLayerWrapper│    │ CustomLogicWrapper│
│ (abstract)    │    │ (internal bridge) │    │ (internal bridge) │
│               │    │                   │    │                   │
│ - BotOwner    │    │ Wraps CustomLayer │    │ Wraps CustomLogic │
│ - Priority    │    │ as EFT's native   │    │ as EFT's native   │
│ - IsActive()  │    │ AICoreLayerClass  │    │ BotNodeAbstract   │
│ - GetNextAct()│    │                   │    │                   │
│ - IsCurrEnd() │    └──────────────────┘    └──────────────────┘
└───────────────┘
        ▲
        │  (mods extend this)
        │
  ┌─────┴──────┐          ┌──────────────┐
  │ SAINLayer  │          │ LootingLayer │
  │ (abstract) │          │  (concrete)  │
  └────────────┘          └──────────────┘
```

### BigBrain Key Classes


| Class                  | Location                           | Role                                                                                                                                                                   |
| ---------------------- | ---------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `BrainManager`         | `Brains/BrainManager.cs`           | Singleton registry. Manages custom layer/logic registration, layer exclusion/restoration, and bot tracking.                                                            |
| `CustomLayer`          | `Brains/CustomLayer.cs`            | Abstract base for user-defined behavior layers. Exposes `IsActive()`, `GetNextAction()`, `IsCurrentActionEnding()`.                                                    |
| `CustomLogic`          | `Brains/CustomLogic.cs`            | Abstract base for actions (logic nodes) within a layer. Generic `CustomLogic<T>` where `T : ActionData`. Exposes `Start()`, `Stop()`, `Update(T)`.                     |
| `CustomBrain`          | `Brains/CustomBrain.cs`            | Stub (TODO). Reserved for full custom brain replacement.                                                                                                               |
| `CustomLayerWrapper`   | `Internal/CustomLayerWrapper.cs`   | Internal bridge. Wraps a `CustomLayer` instance so EFT's native `AICoreLayerClass<BotLogicDecision>` can execute it. Handles logic-to-ID mapping and action lifecycle. |
| `CustomLogicWrapper`   | `Internal/CustomLogicWrapper.cs`   | Internal bridge. Wraps a `CustomLogic` instance as a `BotNodeAbstractClass` so EFT's agent system can tick it.                                                         |
| `BrainHelpers`         | `Internal/BrainHelpers.cs`         | Extension methods on `BotOwner` for `RemoveLayerForBot()` and `RestoreLayerForBot()`.                                                                                  |
| `ExcludeLayerHelpers`  | `Internal/ExcludeLayerHelpers.cs`  | Helpers for matching layers/brains/roles against exclusion criteria.                                                                                                   |
| `ExcludeLayerSplitter` | `Internal/ExcludeLayerSplitter.cs` | Dynamically creates exclusion entries based on brain name + role combinations.                                                                                         |
| `AbstractLayerInfo`    | `Internal/AbstractLayerInfo.cs`    | Base class for `LayerInfo` and `ExcludeLayerInfo` — holds brain names and roles (WildSpawnType).                                                                       |
| `Utils`                | `Utils.cs`                         | Reflection helpers (`GetFieldByType`, `GetPropertyNameByType`).                                                                                                        |


### BigBrain Harmony Patches

BigBrain uses **5 Harmony patches** to inject into EFT's brain system:


| Patch Class                        | Target              | Purpose                                                                                 |
| ---------------------------------- | ------------------- | --------------------------------------------------------------------------------------- |
| `BotBaseBrainActivatePatch`        | Brain activation    | Intercepts bot brain creation to inject custom layers.                                  |
| `BotBrainCreateLogicNodePatch`     | Logic node creation | Redirects logic node creation to instantiate `CustomLogicWrapper` for custom logic IDs. |
| `BotBaseBrainUpdatePatch`          | Brain update tick   | Ensures custom layers and logics are properly ticked during brain update.               |
| `BotAgentUpdatePatch`              | Agent update        | Hooks the agent's decision loop so custom layers participate in action selection.       |
| `BotBaseBrainActivateLayerPatch`   | Layer activation    | Intercepts layer transitions to handle `CustomLayerWrapper.Start()`/`Stop()`.           |
| `BotStandartBotBrainActivatePatch` | Standard brain init | Ensures custom layers are injected during standard bot brain initialization.            |


### BigBrain Layer Lifecycle

```
1. Mod calls BrainManager.AddCustomLayer(typeof(MyLayer), brainNames, priority)
   │
2. When a bot spawns with a matching brain name:
   │  BotBaseBrainActivatePatch fires
   │  → Reads BrainManager.Instance.CustomLayers
   │  → Instantiates CustomLayerWrapper for each matching layer
   │  → Inserts wrapper into bot's brain layer dictionary
   │
3. Each frame, EFT's brain ticks all layers:
   │  For each layer, calls ShallUseNow() → CustomLayerWrapper → customLayer.IsActive()
   │
4. When a custom layer becomes active:
   │  GetDecision() → customLayer.GetNextAction()
   │  Returns a (logicId, reason, data) tuple
   │  EFT agent executes the logic node
   │
5. When the action should end:
   │  ShallEndCurrentDecision() → customLayer.IsCurrentActionEnding()
   │  If true → StopCurrentLogic() → customLogic.Stop()
```

### Custom Layer IDs

BigBrain assigns numeric IDs starting at **9000**:

- `START_LAYER_ID = 9000` — custom layer indices in the brain dictionary
- `START_LOGIC_ID = 9000` — custom logic IDs cast to `BotLogicDecision` enum

Any logic decision with value ≥ 9000 is recognized as a custom action by `CustomLayerWrapper`.

---

## SAIN

### SAIN Purpose

SAIN is a **full combat AI replacement** built on top of BigBrain. It replaces the vanilla
Tarkov bot combat behavior with a player-imitating decision system covering vision,
hearing, movement, cover, squad coordination, suppression, and personality-driven behavior.

### SAIN Tick Flow

```
GameWorld.DoWorldTick()          ← EFT's native game tick
  │
  ▼
WorldTickPatch (Harmony postfix)
  │
  ▼
GameWorldComponent.WorldTick(dt)
  │
  ├── ManualUpdate(time, dt)
  │   ├── Extract finder update
  │   ├── Door handling
  │   ├── Location data
  │   └── Iterate all PlayerComponents
  │
  ├── BotManagerComponent.ManualUpdate(time, dt)
  │   ├── BotSpawnController.Update          ← Bot spawn/despawn lifecycle
  │   ├── TimeVision.Update                  ← Time-of-day vision
  │   ├── WeatherVision.Update               ← Weather effects on vision
  │   ├── BotSquads.Update                   ← Squad coordination
  │   │
  │   └── ForEach BotComponent:
  │       BotComponent.ManualUpdate(time, dt)
  │         ├── TickClassGroup(_alwaysTickClasses)       ← Every frame
  │         │   SAINActivationClass, SAINAILimit,
  │         │   CurrentTargetClass, SAINEnemyController,
  │         │   SAINDecisionClass
  │         │
  │         ├── TickClassGroup(_tickWhenActiveClasses)   ← When bot is active
  │         │   SAINBotUnstuckClass
  │         │
  │         ├── TickClassGroup(_tickWhenNoSleepClasses)  ← When bot not in standby
  │         │   Vision, Hearing, Mover, Medical, Info,
  │         │   Cover, Steering, Talk, Memory, Suppression,
  │         │   Search, Grenade, Extract, Flashlight, Aiming
  │         │
  │         ├── HandleDumbShit()                          ← Cleanup/fixes
  │         │
  │         └── TickClassGroup(_tickWhenCombatClasses)   ← When in combat
  │               SAINShootData, AimDownSightsController,
  │               SAINFriendlyFireClass
  │
  └── TickSoundCaches()                  ← Sound propagation (30Hz/15Hz)
```

### SAIN Component Architecture

```
SAINPlugin (BepInEx entry point)
  │
  ├── GameWorldComponent              ← Attached to GameWorld, owns global tick
  │   ├── PlayerComponent[]           ← All players (including bots)
  │   ├── BotManagerComponent         ← Orchestrates all bots
  │   └── Sound propagation system
  │
  ├── BotManagerComponent             ← Singleton, manages all bot instances
  │   ├── BotSpawnController          ← Spawn/despawn, BotDictionary
  │   ├── Vision coroutines (global)
  │   │   ├── VisionRaycastJob        ← 30Hz multi-threaded raycasts
  │   │   ├── DirectionDataJob        ← O(P²) direction data
  │   │   └── EnemyPlaceRaycastJob    ← Enemy position verification
  │   ├── BotSquads                   ← Squad management
  │   ├── TimeVision / WeatherVision  ← Environmental modifiers
  │   └── BotComponent[]              ← Per-bot SAIN instance
  │
  └── BotComponent                    ← Per-bot AI brain replacement
      ├── SAINActivationClass         ← Active/StandBy state
      ├── SAINAILimit                 ← Distance-based tiering
      ├── SAINEnemyController         ← Enemy list management
      ├── SAINDecisionClass           ← Decision manager (10Hz)
      ├── SAINBotLookClass            ← EFT look sensor bridge
      ├── SAINBotHearingClass         ← Sound detection
      ├── SAINMoveClass               ← Movement/steering
      ├── CoverFinderComponent        ← Dynamic cover analysis
      ├── SAINMemoryClass             ← Tactical memory
      ├── SAINSuppressionClass        ← Suppression effects
      ├── SAINShootData               ← Firing data
      └── ... other subsystems
```

### SAIN Bot Tick Groups


| Group                     | Classes                                                                                                                                       | When                  |
| ------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- | --------------------- |
| `_alwaysTickClasses`      | SAINActivationClass, SAINAILimit, CurrentTargetClass, SAINEnemyController, SAINDecisionClass                                                  | Every frame           |
| `_tickWhenActiveClasses`  | SAINBotUnstuckClass                                                                                                                           | Bot is active         |
| `_tickWhenNoSleepClasses` | Vision, Hearing, Mover, Medical, Info, Cover, Steering, Talk, Memory, Suppression, Search, Grenade, Extract, Flashlight, Aiming (~18 classes) | Bot is not in standby |
| `_tickWhenCombatClasses`  | SAINShootData, AimDownSightsController, SAINFriendlyFireClass                                                                                 | Bot is in combat      |


### SAIN Layer/Action System

SAIN defines bot behavior through **CustomLayers** (registered via BigBrain's `BrainManager`)
and **BotActions** (extending `CustomLogic<T>`):

```
SAINLayer (abstract, extends CustomLayer)
  │
  ├── CombatSquadLayer (priority: configurable, default ~78 — above solo)
  │   ├── SuppressAction        ← Suppressive fire
  │   ├── RegroupAction         ← Move to squad
  │   └── FollowSearchParty     ← Follow squad search
  │
  ├── CombatSoloLayer (priority: configurable, default ~77)
  │   ├── DogFightAction       ← CQB strafe/shoot
  │   ├── StandAndShootAction   ← Stationary shooting
  │   ├── RushEnemyAction       ← Aggressive push
  │   ├── MoveToEngageAction    ← Approach enemy
  │   ├── SearchAction          ← Hunt for lost enemy
  │   ├── SeekCoverAction       ← Find and move to cover
  │   ├── ShiftCoverAction      ← Change cover position
  │   ├── ThrowGrenadeAction    ← Grenade usage
  │   ├── DoSurgeryAction       ← Self-heal in combat
  │   ├── FreezeAction          ← Hold position
  │   └── MeleeAttackAction     ← Melee combat
  │
  ├── SAINAvoidThreatLayer (priority: 80)
  │   └── (Avoid grenades/artillery)
  │
  ├── ExtractLayer (priority: configurable, default ~74)
  │   └── ExtractAction         ← Move to extract
  │
  └── DebugLayer (priority: 99)
      ├── RunningAction         ← Debug run
      └── CrawlAction           ← Debug crawl
```

**Layer priorities** determine which behavior wins when multiple layers want to be active:

- Higher priority = checked/executed first
- Debug (99) > Avoid Threat (80) > Squad combat (~~78) > Solo combat (~~77) > Extract (~74) — see `[LayerSettings](../OptimizedMod/SAIN/SAIN/Preset/GlobalSettings/Categories/General/LayerSettings.cs)`

### SAIN Decision Making (BotDecisionManager)

Runs at a dynamic frequency (base 10Hz, throttled for distant bots). **Full ranking + BigBrain mapping:** [SAIN_DECISION_AND_LAYER_RANKING.md](SAIN_DECISION_AND_LAYER_RANKING.md).

```
BotDecisionManager.getDecision()   // ChooseEnemy() first; null → all None
  │
  ├── SelfActionDecisions          ← Self-preservation (may force SeekCover + self)
  ├── Tagilla / zombie / dogfight / melee / move-to-cover  ← early exits (see ranking doc)
  ├── SquadDecisions               ← Team coordination (solo combat None)
  │     → Suppress, Help, GroupSearch, PushSuppressedEnemy, …
  └── EnemyDecisions               ← Personal combat (solo ≠ None, squad None)
        → StandAndShoot, Rush, Search, Retreat, …
```

### SAIN AI Limit System

A foundational optimization layer that tiers bots by distance from the nearest human player:


| Tier        | Distance | Vision             | Cover         | Decision Rate | Hearing   |
| ----------- | -------- | ------------------ | ------------- | ------------- | --------- |
| **None**    | < 150m   | Full               | Full (10Hz)   | Full (10Hz)   | Full      |
| **Far**     | 150-250m | Reduced raycasts   | Reduced (5Hz) | Reduced (5Hz) | Reduced   |
| **VeryFar** | 250-400m | Minimal (LoS only) | Disabled      | Slow (3Hz)    | Minimal   |
| **Narnia**  | > 400m   | Bare minimum       | Disabled      | Slow (2Hz)    | Near-zero |


Each subsystem checks `Bot.CurrentAILimit` to decide how aggressively to throttle.

### SAIN Performance Hotspots

Ranked by CPU impact per the performance analysis in `PERFORMANCE_ARCHITECTURE.md`:


| Priority | System               | Location                                  | Default Rate                      | Impact                    |
| -------- | -------------------- | ----------------------------------------- | --------------------------------- | ------------------------- |
| **P1**   | VisionRaycastJob     | `Jobs/VisionRaycastJob.cs`                | 30Hz × Bots × Enemies × Parts × 3 | **2,700+ raycasts/frame** |
| **P1**   | DirectionDataJob     | `Jobs/DirectionDataJob.cs`                | 60Hz, O(P²)                       | **54,000 checks/sec**     |
| **P2**   | EFT Look Sensor      | `Sense/SAINBotLookClass.cs`               | 30Hz, main thread                 | Heavy per-bot             |
| **P2**   | EnemyPlaceRaycast    | `Jobs/EnemyPlaceRaycastJob.cs`            | 60Hz (was infinite)               | Every bot, every frame    |
| **P2**   | SAINBotUnstuckClass  | `Bot/SAINBotUnstuckClass.cs`              | 60Hz per bot                      | 1,800 iterations/sec      |
| **P2**   | SAINEnemyController  | `EnemyControllers/SAINEnemyController.cs` | 60Hz full iteration               | Every frame               |
| **P3**   | CoverFinderComponent | `Components/CoverFinderComponent.cs`      | 10Hz, Physics.OverlapBox          | 35×5×35m box              |
| **P4**   | BotDecisionManager   | `Decision/BotDecisionManager.cs`          | 10Hz                              | Chained decision tree     |
| **P5**   | Sound Propagation    | `Components/GameWorldComponent.cs`        | 30Hz players, 15Hz bots           | Per-player iteration      |
| **P5**   | Squad Visibility     | `Info/BotSquadClass.cs`                   | ~1Hz, blocking raycasts           | 300 raycasts/sec          |


**Key optimization strategies already documented:**

- `WaitForSeconds` caching to eliminate per-iteration allocation
- HashSet snapshot lists for O(1) enumeration without allocation
- NativeArray try-finally disposal for memory leak prevention
- ShallTick() wiring to respect per-class time gating
- Job-system batching for squad visibility checks

---

## SPT-LootingBots

### LootingBots Purpose

SPT-LootingBots (by Skwizzy) adds automated bot looting behavior. Bots scan for nearby corpses,
containers, and loose items, navigate to them, and intelligently loot — comparing value,
swapping gear, and managing inventory. It replaces the vanilla `LootPatrol` and `Utility peace`
layers with a custom `LootingLayer` built on BigBrain.

### LootingBots Architecture

```
LootingBots.cs (BepInEx Plugin Entry Point)
  │
  │  Awake():
  │    ├── RemoveLayer("Utility peace", [...brains])      ← Disable vanilla peaceful looting
  │    ├── RemoveLayer("LootPatrol", [...brains])        ← Disable BSG's looting layer
  │    └── AddCustomLayer(typeof(LootingLayer), [...], priority)
  │         ├── Priority 4: Scavs, Bosses, Followers, Goons
  │         ├── Priority 5: PMCs, Rogues, ArenaFighters
  │         ├── Priority 11: Obdolbs (zombies)
  │         └── Priority 13: SectantWarrior, SectantPriest
  │
  └── Update():
        └── ItemAppraiser.UpdatePricesAsync()  ← Refresh flea/handbook prices

LootingLayer : CustomLayer
  │
  ├── LootingBrain (MonoBehaviour)         ← Per-bot loot state machine
  │   ├── LootingInventoryController       ← Gear swap, item valuation, grid management
  │   ├── ActiveLoot / ActiveLootType      ← Current loot target
  │   ├── IgnoredLootIds / NonNavigableLootIds ← Tracking sets
  │   └── async looting coroutines:
  │       ├── LootCorpseAsync()            ← Strip corpse equipment
  │       ├── LootContainerAsync()         ← Open → examine → take → close
  │       └── LootItemAsync()             ← Pick up loose item
  │
  └── LootFinder (MonoBehaviour)           ← Per-bot loot scanner
      └── Begins coroutine-based scan for nearby lootables
          (filtered by distance, type, LoS, and value)

Global Utilities:
  ├── ScanScheduler              ← Token-based concurrency limiter (max scans at once)
  ├── ActiveBotCache             ← Caps bot count for looting (distance-from-player gating)
  ├── ActiveLootCache            ← Tracks items currently being looted (prevents 2 bots looting same item)
  ├── ItemAppraiser              ← Item pricing (handbook or flea market)
  └── LootingBotsInterop         ← Reflection-based API for other mods
```

### LootingBots Layer/Action System

LootingBots uses one layer with three logic actions:

```
LootingLayer (extends CustomLayer directly)
  │
  │  Priority: 4 (low-priority bots), 5 (PMCs/Rogues),
  │            11 (Obdolbs), 13 (Sectants)
  │
  │  IsActive(): bot is Active AND not healing AND
  │              (brain enabled) AND (scheduled scan OR currently looting)
  │
  ├── GetNextAction():
  │   ├── IsBotLooting?            → LootingLogic       ← Move to & loot item
  │   ├── IsScheduledScan?         → FindLootLogic      ← Scan for new loot
  │   └── Otherwise                → PeacefulLogic      ← Passthrough to vanilla patrol
  │
  └── IsCurrentActionEnding():
      ├── FindLootLogic active?    → ends when scan finishes
      └── LootingLogic active?     → ends when looting completes (resets scan timer)

LootingLogic : CustomLogic
  ├── Update(): TryLoot()
  │   ├── Is close enough? → StartLooting() (async coroutine)
  │   └── Not close?       → TryMoveToLoot() (NavMesh pathfinding, stuck detection)
  ├── Movement thresholds: 2 check intervals, 30 nav attempts, stuck detection at 0.3m threshold
  └── Stop(): resets stuck count, stops looting

FindLootLogic : CustomLogic
  ├── Update(): if scan not running AND ScanScheduler ticket available → BeginSearch()
  └── Stop(): resets scan timer, stops scan

PeacefulLogic : CustomLogic
  └── Update(): delegates to vanilla GClass266.PeacefulNodeClass ← bot patrols normally
```

### LootingBots Decision Flow

```
1. Bot spawns → LootingLayer constructor fires
   → LootingBrain + LootFinder added as MonoBehaviour components
   → LootingBrain.Start() configures cache & scan scheduler

2. LootingBrain.Update() every frame:
   ├── Performance check (every 3s or scan interval):
   │   ├── Too far from player? → disable brain (unless ForceBrainEnabled)
   │   └── ActiveBotCache over capacity? → disable brain
   ├── Door interaction (open nearby doors)
   └── ActiveLoot cleanup (player picked up our target item?)

3. LootingLayer.IsActive() returns true → BigBrain passes control:

4. FindLootLogic.Update():
   ├── Bot has free space?
   └── ScanScheduler.CanStartScan()? → LootFinder.BeginSearch()
       │
       ├── Scans for corpses within DetectCorpseDistance (default 80m)
       ├── Scans for containers within DetectContainerDistance
       ├── Scans for loose items within DetectItemDistance
       ├── Filters: LoS check (opt-in), value threshold, loot type enabled
       └── Sets ActiveLoot on LootingBrain

5. LootingLogic.Update():
   ├── TryMoveToLoot() → NavMesh pathfinding
   ├── Stuck detection → ignore after 2 stuck checks or 30 nav attempts
   └── IsCloseEnough() (<0.85m²) → LootingBrain.StartLooting()

6. Looting transaction (async):
   ├── Corpse:  LootCorpseAsync()  → get priority items → examine delay → add to inventory
   ├── Container: LootContainerAsync() → open → examine delay → loot nested → close
   └── Item:    LootItemAsync()     → pick up directly

7. OnLootTaskEnd():
   ├── Update weapon, grid stats, AI power
   └── Reset for next scan cycle (LootScanInterval, default 15s)
```

### LootingBots Key Classes


| Class                          | Location                                                 | Role                                                                                                                                                    |
| ------------------------------ | -------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `LootingBots`                  | `LootingBots/LootingBots.cs`                             | BepInEx entry point. Registers config, removes vanilla layers, registers `LootingLayer`.                                                                |
| `LootingLayer`                 | `LootingBots/LootingLayer.cs`                            | BigBrain `CustomLayer`. Manages looting state machine — routes to FindLootLogic, LootingLogic, or PeacefulLogic.                                        |
| `LootingBrain`                 | `LootingBots/Components/LootingBrain.cs`                 | `MonoBehaviour` per bot. State machine for active loot, async looting coroutines (corpse/container/item), stuck detection, distance/performance gating. |
| `LootFinder`                   | `LootingBots/Components/LootFinder.cs`                   | `MonoBehaviour` per bot. Coroutine-based scanner that finds nearby corpses, containers, and loose items.                                                |
| `LootingInventoryController`   | `LootingBots/Components/LootingInventoryController.cs`   | Handles gear comparison, equipment swapping, grid space management, and value-based decisions.                                                          |
| `LootingTransactionController` | `LootingBots/Components/LootingTransactionController.cs` | Simulates player-like delays (examine time, transaction delay) between loot actions.                                                                    |
| `ItemAppraiser`                | `LootingBots/Components/ItemAppraiser.cs`                | Queries handbook or flea market for item pricing. Supports async price updates.                                                                         |
| `FindLootLogic`                | `LootingBots/Logic/FindLootLogic.cs`                     | BigBrain `CustomLogic`. Triggers loot scan when bot has free space and a scan ticket is available.                                                      |
| `LootingLogic`                 | `LootingBots/Logic/LootingLogic.cs`                      | BigBrain `CustomLogic`. Handles NavMesh movement to loot, stuck detection (30 nav attempt limit).                                                       |
| `PeacefulLogic`                | `LootingBots/Logic/PeacefulLogic.cs`                     | BigBrain `CustomLogic`. Passthrough to vanilla peaceful patrol behavior when not looting.                                                               |
| `External`                     | `LootingBots/External.cs`                                | Public API for interop: `ForceBotToScanLoot()`, `PreventBotFromLooting()`, `CheckIfInventoryFull()`, `GetNetLootValue()`, `GetItemPrice()`.             |
| `LootingBotsInterop`           | `LootingBots/LootingBotsInterop.cs`                      | Reflection-based interop class for external mods to call LootingBots without a hard dependency.                                                         |
| `ScanScheduler`                | `LootingBots/Utilities/ScanScheduler.cs`                 | Token-based concurrency limiter. Only N bots can scan simultaneously (default 3, configurable).                                                         |
| `ActiveBotCache`               | `LootingBots/Utilities/ActiveBotCache.cs`                | Caps total looting-enabled bots (default 20). Combined with distance-from-player gating.                                                                |
| `ActiveLootCache`              | `LootingBots/Utilities/ActiveLootCache.cs`               | Tracks which loot items are currently being targeted to prevent conflicts.                                                                              |
| `LootUtils`                    | `LootingBots/Utilities/LootUtils.cs`                     | Static helpers for item retrieval, container interaction, and value comparison.                                                                         |
| `BotTypes`                     | `LootingBots/Utilities/BotTypes.cs`                      | `[Flags] enum BotType` filter used by config entries to toggle features per bot type.                                                                   |


### LootingBots Layer Priorities

LootingBots uses different layer priorities depending on bot type, allowing SAIN's combat layers
to take precedence when appropriate:


| Bot Types                       | Brain Names                                                                                                                                                                                     | Priority | Reasoning                                     |
| ------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------- | --------------------------------------------- |
| Scavs, Bosses, Followers, Goons | `Assault`, `CursAssault`, `BossKojaniy`, `BossGluhar`, `BossPartisan`, `BossKolontay`, `BossSanitar`, `BossBully`, `BossBoar`, `BirdEye`, `BigPipe`, `Knight`, `Tagilla`, `Killa`, followers... | **4**    | Low priority — combat overrides looting       |
| PMCs, Rogues, ArenaFighters     | `PMC`, `PmcUsec`, `PmcBear`, `ExUsec`, `ArenaFighter`                                                                                                                                           | **5**    | Slightly above scavs — combat still overrides |
| Zombies (Obdolbs)               | `Obdolbs`                                                                                                                                                                                       | **11**   | High priority — zombies primarily loot        |
| Sectants                        | `SectantWarrior`, `SectantPriest`                                                                                                                                                               | **13**   | Highest priority — cultists loot aggressively |


### LootingBots Interop API

LootingBots exposes a reflection-based interop system (via `LootingBotsInterop.cs`) that allows
other mods to control looting behavior without a hard dependency:


| Method                                         | Purpose                                               |
| ---------------------------------------------- | ----------------------------------------------------- |
| `Init()`                                       | Detects and initializes interop hooks                 |
| `TryForceBotToScanLoot(botOwner)`              | Forces a bot to scan for loot immediately             |
| `TryPreventBotFromLooting(botOwner, duration)` | Stops looting and blocks scans for `duration` seconds |
| `CheckIfInventoryFull(botOwner)`               | Returns `true` if bot has < 2 free grid slots         |
| `GetNetLootValue(botOwner)`                    | Returns total roubles looted by bot this raid         |
| `GetItemPrice(item)`                           | Gets handbook/flea price of an item                   |


### LootingBots Performance Design

LootingBots has built-in performance controls:


| Mechanism             | Default         | Config Key                | Purpose                                                                           |
| --------------------- | --------------- | ------------------------- | --------------------------------------------------------------------------------- |
| **ActiveBotCache**    | 20 bots max     | `MaxActiveLootingBots`    | Caps total bots running looting logic. Bots beyond cap have their brain disabled. |
| **Distance Gating**   | 0 (unlimited)   | `LimitDistanceFromPlayer` | Disables looting brain for bots beyond N meters from nearest human player.        |
| **ScanScheduler**     | 3 concurrent    | `MaxConcurrentScans`      | Token-based concurrency limiter — only N bots scan at once. Prevents scan spikes. |
| **Scan Interval**     | 15 seconds      | `LootScanInterval`        | Time between loot scans per bot.                                                  |
| **Performance Check** | Every 3 seconds | (internal)                | Re-evaluates bot eligibility (distance, capacity) at minimum 3s intervals.        |
| **Loot Timeout**      | 180 seconds     | `LootTimeout`             | Cancels looting via `CancellationTokenSource` if bot takes too long.              |
| **Navigation Limit**  | 30 attempts     | (internal)                | Bot gives up on unreachable loot after 30 navigation attempts.                    |
| **Stuck Detection**   | 0.3m threshold  | (internal)                | Bot ignores loot if it hasn't moved > 0.3m in 2 consecutive checks.               |


**Additional optimizations:**

- Per-bot `HashSet<string>` for `IgnoredLootIds` and `NonNavigableLootIds` to prevent re-scanning
- `ActiveLootCache` prevents 2 bots from targeting the same item simultaneously
- Async looting tasks with `CancellationToken` for clean interruption
- `ListActionPool` for reusable list allocations
- `NonLinqUtils` for allocation-free collection operations

---

## SPT-Waypoints

### Waypoints Purpose

SPT-Waypoints (by DrakiaXYZ) replaces the vanilla Unity NavMesh with an **expanded, hand-authored
NavMesh** per map. This gives bots access to areas they cannot reach with the stock NavMesh
(rooftops, interiors, off-path terrain). It also includes **door link fixes** that correct
broken door navigation data. Does NOT use BigBrain — it's a standalone framework mod that
patches directly into EFT's navmesh and pathfinding systems.

### Waypoints Architecture

```
WaypointsPlugin.cs (BepInEx entry point)
  │
  │  Awake():
  │    ├── Version check + dependency validation
  │    └── Enable 7 Harmony patches:
  │
  ├── WaypointPatch            ← Inject custom NavMesh at BotsController.Init
  │     └── Loads .bundle from mesh/ folder → NavMesh.RemoveAllNavMeshData() → NavMesh.AddNavMeshData()
  │
  ├── FindPathPatch            ← Replace BotPathFinderClass.FindPath
  │     └── Uses NavMesh.CalculatePath() directly (more reliable than EFT's pathfinder)
  │
  ├── DoorLinkPatch            ← Fix door link data for navigation
  ├── DoorLinkStateChangePatch ← Update nav links when door state changes
  ├── SwitchDoorBlockerPatch   ← Handle switch-operated doors
  ├── ExfilDoorBlockerPatch    ← Handle extraction doors
  └── DebugPatch               ← Debug visualization (NavMeshDebugComponent, BotZoneDebugComponent)
```

### Waypoints Key Classes & Patches


| Class/File                 | Location                              | Role                                                                                                 |
| -------------------------- | ------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| `WaypointsPlugin`          | `WaypointsPlugin.cs`                  | BepInEx entry. Loads custom navmesh bundles, enables 7 patches. No BigBrain dependency.              |
| `WaypointPatch`            | `Patches/WaypointPatch.cs`            | Patches `BotsController.Init`. InjectNavmesh() loads map-specific `.bundle` files, replaces NavMesh. |
| `FindPathPatch`            | `Patches/FindPathPatch.cs`            | Patches `BotPathFinderClass.FindPath`. Falls back to direct `NavMesh.CalculatePath()`.               |
| `DoorLinkPatch`            | `Patches/DoorLinkPatch.cs`            | Fixes door navigation links between zones.                                                           |
| `DoorLinkStateChangePatch` | `Patches/DoorLinkStateChangePatch.cs` | Updates nav links when doors change state (open/close).                                              |
| `SwitchDoorBlockerPatch`   | `Patches/SwitchDoorBlockerPatch.cs`   | Handles switch-operated door navigation.                                                             |
| `ExfilDoorBlockerPatch`    | `Patches/ExfilDoorBlockerPatch.cs`    | Handles extraction door nav blockers.                                                                |
| `DebugPatch`               | `Patches/DebugPatch.cs`               | Optional debug visualization for NavMesh and BotZones.                                               |
| `NavMeshDebugComponent`    | `Components/NavMeshDebugComponent.cs` | Renders NavMesh debug overlay.                                                                       |
| `BotZoneDebugComponent`    | `Components/BotZoneDebugComponent.cs` | Renders bot zone debug overlay.                                                                      |
| `DependencyChecker`        | `Helpers/DependencyChecker.cs`        | Validates required mod dependencies at startup.                                                      |
| `Settings`                 | `Helpers/Settings.cs`                 | Config toggle for custom navmesh enabled/disabled.                                                   |


**Key design decisions:**

- Custom navmeshes are shipped as Unity `AssetBundle` files per map (e.g., `customs-navmesh.bundle`)
- Factory maps are standardized to `factory4`, Ground Zero to `sandbox`
- `NavMesh.RemoveAllNavMeshData()` wipes the old navmesh before injection
- `bundle.Unload(false)` keeps loaded assets alive while freeing the bundle
- No BigBrain dependency — operates at the Unity NavMesh level, below any mod layer system

**Relationship to other mods:**

- **SAIN** depends on Waypoints for expanded pathfinding data
- **LootingBots** uses Waypoints' navmesh for loot navigation
- Waypoints is transparent to BigBrain layers — pathfinding "just works better"

---

## SPT-AILimit

### AILimit Purpose

SPT-AILimit (by dvize) is a **performance mod** that controls how many bots are actively
simulated based on distance from human players. Instead of throttling AI subsystems (like SAIN's
AI Limit), AILimit **completely deactivates distant bots** by calling `GameObject.SetActive(false)`.
This eliminates all CPU cost from bots beyond a configurable per-map distance.

### AILimit Architecture

```
AILimitPlugin.cs (BepInEx entry point)
  │
  │  Awake():
  │    ├── Config entries: PluginEnabled, BotLimit (default 10), TimeAfterSpawn (10s)
  │    ├── Per-map distance configs (Factory 80m, Labs 250m, all others 400m)
  │    ├── ConfigManager.Initialize() → wires SettingChanged events
  │    ├── NewGamePatch.Enable()      → AILimitComponent.Enable() on GameWorld.OnGameStarted
  │    └── Patch2.Enable()            → Skip Player.ComplexUpdate for inactive GameObjects
  │
  └── [BepInDependency("com.fika.core", SoftDependency)]  ← Fika multiplayer support

AILimitComponent : MonoBehaviour
  │
  │  Start():
  │    ├── SetupBotDistanceForMap()    ← Per-map distance from config
  │    ├── GetPlayers()                ← Find all non-AI players
  │    └── Subscribe to bot spawn/despawn events
  │
  └── Update():
       ├── Frame counter increments
       ├── Every N frames (configurable, default 300):
       │   ├── Calculate distance from every bot to nearest human player
       │   ├── Sort bots by distance (closest first)
       │   ├── For the closest BotLimit bots within botDistance:
       │   │   → Activate: GameObject.SetActive(true), BotStandBy.Activate()
       │   └── For all other bots:
       │       → Deactivate: GameObject.SetActive(false), clear DecisionQueue
       └── Every frame after deactivation: enforce deactivated bots stay disabled

PlayerInfo / botPlayer (nested classes)
  ├── PlayerInfo: maps player.Id → Player + botPlayer
  └── botPlayer: Id, Distance, eligibleNow (after spawn timer), Timer
```

### AILimit Key Classes


| Class              | Location                  | Role                                                                                                                                        |
| ------------------ | ------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- |
| `AILimitPlugin`    | `Plugin.cs`               | BepInEx entry. Config entries for bot limit, per-map distances, spawn timer, frame interval.                                                |
| `AILimitComponent` | `Component.cs`            | `MonoBehaviour` on GameWorld. Updates every N frames: sorts bots by distance, activates closest N, deactivates rest via `SetActive(false)`. |
| `ConfigManager`    | `ConfigManager.cs`        | Static event wiring for live config changes via F12 menu.                                                                                   |
| `SettingsHandler`  | `SettingsHandler.cs`      | Handles per-map distance changes at runtime.                                                                                                |
| `NewGamePatch`     | `Plugin.cs` (inner class) | Patches `GameWorld.OnGameStarted` to call `AILimitComponent.Enable()`.                                                                      |
| `Patch2`           | `Plugin.cs` (inner class) | Patches `Player.ComplexUpdate` to skip deactivated GameObjects.                                                                             |


**Key design decisions:**

- Binary on/off per bot — no partial throttling. Inactive bots cost zero CPU.
- Distance-based sorting: closest bots to humans get priority
- Per-map distance configs: Factory 80m, Labs 250m, others 400m
- `TimeAfterSpawn` (default 10s): new bots are eligible only after timer expires
- `FramesToCheck` (default 300 = ~5s at 60fps): re-evaluation interval
- Soft Fika dependency for multiplayer compatibility
- `BotStandBy` integration: sets `CanDoStandBy = true` before deactivation

**Relationship to other mods:**

- **SAIN**: SAIN's internal AI Limit (None/Far/VeryFar/Narnia) throttles subsystems; AILimit completely disables bots. They can coexist — SAIN handles nearby bots, AILimit eliminates distant ones.
- **botplacementsystem**: Both handle bot counts but at different levels. AILimit activates/deactivates; ABPS controls spawning.
- **No BigBrain dependency** — operates below the layer system via GameWorld MonoBehaviour.

---

## botplacementsystem (ABPS)

### ABPS Purpose

botplacementsystem (ABPS, by acidphantasm) is a **bot spawn and placement control mod**
with both client-side patches and server-side configuration. It controls map bot limits (per-map
soft caps), PMC spawn behavior (distance checks, anywhere spawning), scav spawn caps (per-zone,
hotzone support), progressive/regressive boss spawn chances, and bot despawn mechanics.

### ABPS Architecture

```
Client/Plugin.cs (BepInEx entry point)
  │
  │  Awake():
  │    ├── Static config fields: per-map limits, despawn settings, spawn distances, boss chances
  │    ├── AbpsConfig.InitAbpsConfig(Config) ← Load server config into static fields
  │    └── Enable 13 Harmony patches:
  │         ├── BossSpawnScenarioStopPatch        ← Override boss spawn scenario stop
  │         ├── BossSpawnScenarioSpawnProgressPatch ← Override spawn progress
  │         ├── BossProgressiveRegressivePatch    ← Custom progressive/regressive chances
  │         ├── BotOwnerCreationPatch             ← Hook bot creation
  │         ├── BotsControllerInitPatch           ← Set max bot count
  │         ├── SetMaxBotCountPatch               ← Override max bot count
  │         ├── PmcSpawnHookPatch                 ← PMC spawn distance checks
  │         ├── AssaultGroupPatch                 ← Scav spawn group behavior
  │         ├── NonWavesSpawnSystemPatch          ← Non-wave spawn control
  │         ├── TryToSpawnInZonePatch             ← Zone-based spawn limits
  │         ├── IsPlayerEnemyPatch                ← Hostility control
  │         ├── PlayerOnDeadPatch                 ← Death cleanup
  │         └── MenuLoadPatch                     ← Config reload on menu
  │
  └── [BepInDependency("com.fika.headless", SoftDependency)]

Server/ (server-side config)
  ├── Models/AbpsConfig.cs        ← Full config model
  ├── Controllers/MapSpawns.cs    ← Per-map spawn configuration
  ├── Controllers/PmcSpawns.cs    ← PMC-specific spawns
  ├── Controllers/ScavSpawns.cs   ← Scav-specific spawns
  ├── Controllers/BossSpawns.cs   ← Boss spawn configuration
  ├── Controllers/VanillaAdjustments.cs ← Vanilla behavior tuning
  └── Routers/StaticRouters.cs    ← API endpoints for config
```

### ABPS Key Configuration


| Category         | Key Settings                                                                                                                   | Purpose                                                     |
| ---------------- | ------------------------------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------- |
| **Map Limits**   | Per-map bot caps (Customs, Factory, Interchange, Labs, Lighthouse, Reserve, Ground Zero, Shoreline, Streets, Woods, Labyrinth) | Hard caps on total bots per map                             |
| **Despawn**      | `DespawnFurthest`, `DespawnPmcs`, `DespawnDistance`, `DespawnTimer`                                                            | Auto-despawn distant bots                                   |
| **PMC Spawns**   | `PmcSpawnAnywhere`, per-map `PmcSpawnDistanceCheck`                                                                            | Allow PMCs to spawn anywhere on map if distance checks pass |
| **Scav Spawns**  | `SoftCap`, `ZoneScavCap`, `PScavChance`, per-map `ScavSpawnDistanceCheck`                                                      | Zone-based scav caps with distance checks                   |
| **Hotzones**     | `EnableHotzones`, `HotzoneScavCap`, `HotzoneScavChance`                                                                        | Extra scav spawns in hotzones                               |
| **Boss Chances** | `RegressiveChances`, `ProgressiveChances`, `ChanceStep`, `MinChance`, `MaxChance`                                              | Dynamic boss spawn probability                              |


**Key design decisions:**

- Client+Server architecture: server config → client reads via `AbpsConfig`
- Per-map granularity for all limits and distances
- Soft Fika dependency for headless multiplayer
- 13 Harmony patches for deep spawn system overrides
- Boss progressive/regressive: chances increase after boss kills, decrease after boss spawns

**Relationship to other mods:**

- **SAIN**: ABPS controls *who* spawns and *where*; SAIN controls *how they behave*
- **SPT-AILimit**: ABPS caps total bots; AILimit activates/deactivates after spawn. Complementary.
- **Unda**: Both handle PMC spawns. Unda replaces the PMC wave generator server-side; ABPS patches spawn hooks client-side.
- **No BigBrain dependency** — pure spawn system modification.

---

## spt-unda

### Unda Purpose

spt-unda (by Barlog_M) is a **server-side PMC spawn overhaul**. It replaces the vanilla
PMC wave generation system by converting PMCs into "boss" spawns with dynamically-sized groups.
It opens all bot zones on every map, removes marksman scopes, and generates PMC groups
(1 to configurable max size) spread across the map. Server-side only — no client plugin.

### Unda Architecture

```
Unda.cs (SPT server mod entry point)
  │
  └── ModMetadata + IOnLoad: registers as SPT server singleton

Data.cs (server-side service)
  │
  │  FillInitialData():
  │    ├── For each map:
  │    │   ├── Get all spawn zones (excluding marksman snipers)
  │    │   ├── ReviewZones: if ≤5 zones, pad with "BotZone" entries (9 total)
  │    │   ├── MakeAllZonesOpen: set location.Base.OpenZones = "zone1,zone2,..."
  │    │   └── Read MinPlayers/MaxPlayers from location data
  │    └── Store in GeneralLocationInfo dictionary

PmcWaveGeneratorEx (extends PmcWaveGenerator)
  │
  │  ApplyWaveChangesToMap():
  │    ├── DeleteAllPmcBosses(): remove existing pmcBEAR/pmcUSEC boss spawns
  │    ├── DeleteAllCustomWaves(): clear custom wave config
  │    └── GeneratePmcBossWaves():
  │         ├── maxPmcAmount = MinPlayers - 1
  │         ├── SplitMaxAmountIntoGroups(maxPmcAmount, MaxPmcGroupSize)
  │         └── For each group: GeneratePmcAsBoss(groupSize, difficulty)
  │              ├── Random pmcBEAR or pmcUSEC
  │              ├── Main boss + supports (if groupSize > 1)
  │              ├── IgnoreMaxBots = true, SpawnMode = ["pve"]
  │              └── 100% spawn chance, all zones open
  │
  └── RaidTimeAdjustmentServiceEx: extended raid time adjustments

ModConfig.cs: Debug flag, MaxPmcGroupSize, PmcBotDifficulty
ModData.cs:  Loads config from mod's config file
Model.cs:    GeneralLocationInfo (MinPlayers, MaxPlayers)
```

### Unda Architecture (continued)


| Class                         | Location                                      | Role                                                                                           |
| ----------------------------- | --------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| `Unda` / `ModMetadata`        | `BarlogM-Unda/Unda.cs`                        | SPT server mod entry. Registers as singleton `IOnLoad`.                                        |
| `Data`                        | `BarlogM-Unda/Data.cs`                        | Populates zone and player data per map. Opens all zones, pads small maps.                      |
| `PmcWaveGeneratorEx`          | `BarlogM-Unda/PmcWaveGeneratorEx.cs`          | Extends SPT's `PmcWaveGenerator`. Deletes vanilla PMC boss spawns, creates new dynamic groups. |
| `RaidTimeAdjustmentServiceEx` | `BarlogM-Unda/RaidTimeAdjustmentServiceEx.cs` | Extended raid time handling.                                                                   |
| `ModConfig`                   | `BarlogM-Unda/ModConfig.cs`                   | Config: Debug toggle, MaxPmcGroupSize, PmcBotDifficulty.                                       |
| `ModData`                     | `BarlogM-Unda/ModData.cs`                     | Loads and provides mod config.                                                                 |
| `GeneralLocationInfo`         | `BarlogM-Unda/Model.cs`                       | Data model: MinPlayers, MaxPlayers per map.                                                    |


**Key design decisions:**

- Server-side only — no client plugin, no Harmony patches, no BigBrain dependency
- PMCs treated as "boss" spawns with `IgnoreMaxBots = true` — bypasses bot count limits
- Group sizes dynamically split: 1 to `MaxPmcGroupSize` per group
- `minPlayers - 1` formula for total PMCs (leaves room for the human player)
- Small maps (< 5 zones) get padded with "BotZone" entries for full coverage
- All zones opened via `OpenZones` string — bots can spawn anywhere
- `BossChance = 100` + `SpawnMode = ["pve"]` ensures reliable spawning

**Relationship to other mods:**

- **botplacementsystem**: Both modify PMC spawning. Unda replaces the wave generator server-side; ABPS patches spawn hooks client-side. They may conflict if both modify the same spawn mechanics.
- **SAIN**: No direct integration. SAIN handles spawned bot behavior regardless of spawn source.
- **No BigBrain dependency** — pure server-side spawn generation.

---

## OptimizationCore — New Performance Infrastructure

OptimizationCore is a new shared performance library introduced in `OptimizedMod/`. It wraps the
entire AI stack with a frame-budgeted, player-centric execution model designed to keep Lighthouse
at 29+ bots and 60 FPS. It replaces the distance-only `SPT-AILimit` with perception-aware LOD
and adds offline combat resolution and audio spoofing for AI-vs-AI engagements the player cannot see.

### Design Principles


| Principle                 | Description                                                                                                                                           |
| ------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Player-centric**        | Only bots the player can see or hear receive full AI processing. All others are throttled or resolved statistically.                                  |
| **Fake it when unseen**   | AI-vs-AI combat outside the player's perception is resolved via statistical rolls, not real simulation. Audio cues are spoofed to maintain immersion. |
| **Hard budget cap**       | A 2ms-per-frame ceiling prevents AI from starving the render pipeline, regardless of bot count.                                                       |
| **Perception-driven LOD** | Bots are tiered by what the player actually perceives (camera frustum + raycast, gunfire/sprint audibility), not arbitrary distance rings.            |


### 1. AIFrameBudgetScheduler

**Purpose:** Enforces a 2ms hard cap on AI processing per frame. Bots are processed in priority order
(Visible → Audible → Occluded) within the budget window. Offline squads (AI-vs-AI outside player
range) are processed separately at the beginning of the frame. Any bot that would exceed the budget
is deferred to the next frame.

**Key File:** `OptimizedMod/OptimizationCore/AIFrameBudgetScheduler.cs`

**Key Methods and Interfaces:**


| Member                                                       | Role                                                            |
| ------------------------------------------------------------ | --------------------------------------------------------------- |
| `MaxAIBudgetMs = 2.0f`                                       | Hard ceiling on AI processing time per frame                    |
| `RegisterBot(IBudgetedAI, PerceptionTier)`                   | Registers a bot in its tier list (Visible/Audible/Occluded)     |
| `UpdateBotTier(IBudgetedAI, PerceptionTier, PerceptionTier)` | Moves a bot between tier lists when perception changes          |
| `RegisterOfflineSquad(IOfflineSquad)`                        | Registers an offline squad for statistical combat               |
| `ProcessOfflineSquads()`                                     | Ticks all offline squads first, respecting the budget           |
| `ProcessTier(List<IBudgetedAI>)`                             | Processes bots in one tier list, stopping if budget is exceeded |
| `GetBudgetReport()`                                          | Returns debug string with budget usage and tier counts          |


**Integration:**

- All bot components implement `IBudgetedAI` and register with the scheduler
- The scheduler replaces the old per-mod tick loops (SAIN's `BotManagerComponent.ManualUpdate`,
LootingBots' per-frame `Update()`) with a unified budget-aware loop
- `PerceptionSystem` feeds tier changes to the scheduler via `UpdateBotTier()`

### 2. PerceptionSystem

**Purpose:** Determines what the player can actually perceive, replacing the distance-only
`SPT-AILimit` approach. Uses camera frustum culling + raycast for visibility and gunfire/sprint
hearing checks for audibility. Results are cached per bot at configurable intervals (0.5s for
visibility, 1.0s for audibility) to amortize cost.

**Key File:** `OptimizedMod/OptimizationCore/PerceptionSystem.cs`

**Supporting Files:**

- `PerceptionTier.cs` — enum: `Visible`, `Audible`, `Occluded`
- `IBudgetedAI.cs` — interface exposing `ProcessAITick()` and `CurrentTier { get; set; }`

**Key Methods and Interfaces:**


| Member                                         | Role                                                                            |
| ---------------------------------------------- | ------------------------------------------------------------------------------- |
| `VisibilityCheckInterval = 0.5f`               | Minimum time between visibility re-evaluations per bot                          |
| `AudibilityCheckInterval = 1.0f`               | Minimum time between audibility re-evaluations per bot                          |
| `MaxHearingDistance = 200f`                    | Maximum distance for gunfire detection                                          |
| `SprintHearingDistance = 60f`                  | Maximum distance for sprint footsteps detection                                 |
| `GunfireHearingDuration = 3f`                  | How long after firing a bot remains "audible"                                   |
| `EvaluateBot(Vector3, int, bool, bool, float)` | Returns the bot's `PerceptionTier` based on position, sprint state, and gunfire |
| `IsVisible(Vector3)`                           | Camera frustum test + raycast against HighPolyWithTerrainNoGrassMask            |
| `ClearCache(int)`                              | Clears cached tier/timing data for a despawned bot                              |


**Visibility check flow:**

```
1. CalculateFrustumPlanes(playerCamera) → 6 planes (updated every frame)
2. Per bot (throttled to VisibilityCheckInterval):
   a. TestPlanesAABB(frustum, botBounds) → fail → not visible
   b. Physics.Raycast(camera→bot, HighPolyWithTerrainNoGrassMask)
      → hit.collider.distance >= direction.magnitude - 0.5f → visible
      → otherwise → occluded
```

**Audibility check flow:**

```
1. Per bot (throttled to AudibilityCheckInterval):
   a. recentlyFired AND timeSinceLastFire < 3s AND distance < 200m → audible
   b. isSprinting AND distance < 60m → audible
   c. otherwise → not audible
```

**Integration:**

- Replaces `SPT-AILimit` entirely in the optimized stack — AILimit's distance-based
`SetActive(false)` is replaced by perception-driven tier assignment
- Feeds tier data to `AIFrameBudgetScheduler.UpdateBotTier()` so processing priority
follows what the player actually perceives
- `IBudgetedAI.CurrentTier` is updated by the perception system and read by the scheduler

### 3. OfflineCombatResolver

**Purpose:** Resolves AI-vs-AI combat outside the player's perception range using statistical
rolls instead of full simulation. Two opposing `IOfflineSquad` instances are compared by squad
power (weapon damage × armor mitigation × health), a randomized win ratio determines casualties
per side, and the result includes combat duration, weapon types, shot density, and whether it
was an ambush (power ratio > 2:1).

**Key File:** `OptimizedMod/OptimizationCore/OfflineCombatResolver.cs`

**Supporting Files:**

- `IOfflineSquad.cs` — interface: `SquadId`, `SquadPosition`, `Members`, `IsInCombat`, `TickOffline()`
- `OfflineCombatTypes.cs` — `OfflineBotStats` (WeaponDamageOutput, ArmorMitigation, HealthFactor,
EffectiveRange) and `OfflineCombatResult` (CasualtiesSideA/B, WinningSquadId, CombatDuration,
ShotDensity, CombatZoneCenter, IsAmbush)

**Key Methods and Interfaces:**


| Member                                                | Role                                                                                    |
| ----------------------------------------------------- | --------------------------------------------------------------------------------------- |
| `ResolveCombat(IOfflineSquad, IOfflineSquad)`         | Resolves combat between two squads and returns an `OfflineCombatResult`                 |
| `CalculateSquadPower(IReadOnlyList<OfflineBotStats>)` | Sums `WeaponDamageOutput × ArmorMitigation × HealthFactor` across all squad members     |
| `OfflineBotStats`                                     | Stat block per bot: weapon damage, armor, health, effective range                       |
| `OfflineCombatResult`                                 | Resolution output: casualties, winner, duration, weapon types, zone center, ambush flag |


**Combat resolution algorithm:**

```
1. Calculate power per squad: Σ(member.WeaponDamage × ArmorMitigation × Health)
2. Roll with ±30% randomness: power × Random(0.7, 1.3)
3. winRatio = rollA / (rollA + rollB)
4. squadAWins = winRatio > 0.5
5. Casualties: sideA loses (1 - winRatio) × count, sideB loses winRatio × count
6. CombatDuration = Lerp(3s, 30s, 1 - |winRatio - 0.5| × 2)  ← closer fight = longer
7. ShotDensity = Lerp(0.5, 3.0, min(botCount, 10) / 10)  ← more bots = more gunfire
8. IsAmbush = |powerA - powerB| / max(powerA, powerB) > 0.5  ← 2:1 power ratio = ambush
```

**Integration:**

- Called by `AIFrameBudgetScheduler` during `ProcessOfflineSquads()` for bots the player cannot see
- Result is passed to `CombatAudioSpoofer.ScheduleCombatAudio()` so the player still hears distant
firefights
- Squads implement `IOfflineSquad` — the `TickOffline()` method updates squad state based on the
last `OfflineCombatResult` (removing casualties, tracking winners)

### 4. CombatAudioSpoofer

**Purpose:** Generates fake gunfire audio at combat zone locations so the player hears realistic
distant firefights even when the AI combat is resolved statistically (no real AI simulation).
Distance attenuates volume, and a muffled pass is applied beyond 200m. Ambush combats skip the
trailing gunshot burst.

**Key File:** `OptimizedMod/OptimizationCore/CombatAudioSpoofer.cs`

**Key Methods and Interfaces:**


| Member                                           | Role                                                                                                                   |
| ------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------- |
| `MaxAudioDistance = 500f`                        | Beyond this distance, no audio is generated                                                                            |
| `ScheduleCombatAudio(OfflineCombatResult)`       | Starts a coroutine that plays gunfire at the combat zone for the result's duration                                     |
| `PlayCombatSequence(OfflineCombatResult, float)` | Coroutine: plays shots at `ShotDensity` interval with random position jitter (±5-20m), then a final burst of 1-3 shots |
| `PlayGunshot(Vector3, float, bool)`              | Plays a gunshot at a position with volume attenuation and optional muffled pass (volume × 0.3)                         |


**Audio behavior:**

```
1. distance < 500m → schedule audio
2. Shoot interval = 1 / max(ShotDensity, 0.2) seconds
3. Each shot: random position within 5-20m of combat zone center
4. Volume = 1 - (distance / 500m), muffled if distance > 200m
5. End of combat: 1-3 trailing shots (unless ambush)
```

**Integration:**

- Fed by `OfflineCombatResolver` results via `AIFrameBudgetScheduler`
- Completely decoupled from real AI processing — audio is generated purely from the statistical
combat result
- The player hears gunfire that matches the resolved combat's weapon types, duration, and
intensity without any real AI simulation cost

---

## Directory Map

> **All source code is under `OptimizedMod/`.** The directory names below reflect the original mod identities; actual files live at `OptimizedMod/BigBrain/`, `OptimizedMod/SAIN/`, etc. spt-unda is not included in the fork.

```
Tarkov AI/
├── INDEX.md                         ← AI-agent entry point (root level)
├── docs/                            ← All project documentation
│   ├── ARCHITECTURE.md              ← This file
│   ├── INTEGRATION.md               ← Cross-mod integration documentation
│   ├── PERFORMANCE_ARCHITECTURE.md  ← Optimization architecture
│   ├── PERFORMANCE_PLAN.md          ← Phase execution plan
│   └── OPTIMIZED_MOD_README.md      ← Optimized fork stack guide
│
└── OptimizedMod/                     ← ALL source code
    ├── BigBrain/...                  ← Framework (originally SPT-BigBrain)
    ├── SAIN/...                      ← Combat AI
    ├── LootingBots/...               ← Looting AI
    ├── Waypoints/...                 ← Expanded NavMesh
    ├── AILimit/...                   ← Distance-based deactivation
    ├── ABPS/...                      ← Spawn control (originally botplacementsystem)
    ├── MoreBotsAPI/...               ← Custom bot type API
    └── OptimizationCore/             ← Shared performance library (NEW)
        ├── AIFrameBudgetScheduler.cs ← 2ms budget cap, tiered processing
        ├── PerceptionSystem.cs       ← Player-centric visibility + audibility
        ├── OfflineCombatResolver.cs  ← Statistical AI-vs-AI combat
        ├── CombatAudioSpoofer.cs     ← Fake gunfire audio at combat zones
        ├── PerceptionTier.cs         ← Visible/Audible/Occluded enum
        ├── IBudgetedAI.cs            ← ProcessAITick() interface
        ├── IOfflineSquad.cs          ← TickOffline() interface
        └── OfflineCombatTypes.cs     ← OfflineBotStats, OfflineCombatResult
```

