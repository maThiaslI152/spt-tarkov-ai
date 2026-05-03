# SPT Questing Bots

## Overview

**Author:** DanW  
**SPT Version:** ~4.0.2  
**Version:** 0.11.0  
**GUID:** `com.danw.questingbots`  
**License:** CC BY-NC-SA 4.0  
**Repository:** https://github.com/maThiaslI152/SPTQuestingBots.git

SPT Questing Bots transforms the Single Player Tarkov experience by giving AI bots dynamic quest objectives, replacing their default patrol behavior with goal-oriented movement across the entire map. Instead of staying in spawn zones, bots now actively traverse the map to complete EFT quests, hunt bosses, chase airdrops, camp, snipe, and extract — mimicking the behavior of real human players.

The mod also provides an advanced **PMC and player-Scav spawning system** that replicates live Tarkov's PvP experience with progressive spawn waves, group formations, and intelligent spawn point selection.

---

## Architecture

The solution is organized into **three main parts**:

### 1. Shared (`Shared/`) — Cross-Cutting Code
- **Role:** Code shared between client and server, including configuration models, quest data, and utility helpers.
- **Key components:**
  - **Configuration (`Shared/Configuration/`):** 30+ config classes covering every aspect of the mod.
    - `ModConfig.cs` — Root configuration object.
    - `QuestingConfig.cs` — Questing system settings.
    - `QuestGenerationConfig.cs` — Quest generation parameters (NavMesh search distances, etc.).
    - `BotSpawnsConfig.cs` / `BotSpawnTypeConfig.cs` — Spawning system configuration.
    - `BotQuestsConfig.cs` / `QuestSettingsConfig.cs` — Quest-specific settings.
    - `BotQuestingRequirementsConfig.cs` — Bot health/stamina/weight requirements for questing.
    - `BotPathingConfig.cs` — Pathfinding configuration.
    - `StuckBotDetectionConfig.cs` / `StuckBotRemediesConfig.cs` — Stuck bot detection and resolution.
    - `UnlockingDoorsConfig.cs` — Door unlocking behavior.
    - `HearingSensorConfig.cs` — Bot hearing sensitivity for suspending questing.
    - `SearchTimeAfterCombatConfig.cs` — Post-combat cooldown before resuming quests.
    - `BreakForLootingConfig.cs` — Looting behavior integration.
    - `MaxFollowerDistanceConfig.cs` — Group cohesion settings.
    - `SprintingLimitationsConfig.cs` — Sprinting control rules.
    - `ExtractionRequirementsConfig.cs` — Extraction conditions.
    - `PMCHostilityAdjustmentsConfig.cs` — PMC hostility settings.
    - `ScavRaidSettingsConfig.cs` — Scav raid configuration.
    - `LightkeeperIslandQuestsConfig.cs` — Lightkeeper Island quest support.
    - `LimitInitialBossSpawnsConfig.cs` — Boss spawn limiting.
    - `BotCapAdjustmentsConfig.cs` — Bot cap management.
    - `AdjustPScavChanceConfig.cs` — Player Scav conversion chances.
    - `BrainLayerPrioritiesConfig.cs` — Brain layer priority configuration.
    - `EftNewSpawnSystemAdjustmentsConfig.cs` — New spawn system tuning.
    - `ZoneAndItemPositionInfoConfig.cs` — Zone/item position data.
    - `DebugConfig.cs` — Debug visualization options.
  - **Quest Data (`Shared/Quests/Standard/`):** Per-map quest definition JSONs for 12 maps:
    - `bigmap.json` (Customs), `factory4_day.json`, `factory4_night.json`, `interchange.json`, `laboratory.json`, `lighthouse.json`, `rezervbase.json` (Reserve), `sandbox.json` (Ground Zero low), `sandbox_high.json` (Ground Zero high), `shoreline.json`, `tarkovstreets.json`, `woods.json`
  - **Config Data (`Shared/Config/`):**
    - `config.json` — Main configuration file.
    - `eftQuestSettings.json` — EFT quest settings/mappings.
    - `zoneAndItemQuestPositions.json` — Zone and item position data.
  - **Helpers (`Shared/Helpers/`):** Router helpers, math helpers, config helpers.
  - **Models (`Shared/Models/`):** `SerializableVector3`, `ModInfo`.
  - **Build Scripts:** `UpdateModInfoBeforeBuild.ps1`, `CreatePackageAfterBuild.ps1`.

### 2. Server (`Server/`) — SPT Server Mod
- **Role:** Handles quest data routing, PMC/PScav spawning logic, brain type management, and configuration.
- **Entry Point:** `QuestingBots_Server.cs` — Implements `IOnLoad` with priority `PreSptModLoader + 1`. Runs mod integrity checks on startup (client library existence, config array validation).
- **Services (`Server/Services/`):**
  - `InitSpawningSystemService.cs` — Initializes the spawning system: enables player scav generation, disables vanilla PMC waves, manages bot caps, configures EFT spawn system adjustments.
  - `BotHostilityAdjustmentService.cs` — Configures PMC hostility toward Scavs, bosses, and other PMCs.
  - `UpdatePMCAndPScavBrainTypesService.cs` — Manages brain type assignments.
  - `TranslationService.cs` — Handles localization.
  - `AutoDisableSpawningSystemService.cs` — Detects incompatible spawning mods (Unda, ABPS) and auto-disables Questing Bots' spawning to prevent conflicts.
  - `AbstractService.cs` — Base class for all services with mod-enable checking.
- **Routers (`Server/Routers/`):** HTTP endpoints for serving quest data, config, and settings to the client:
  - `ConfigRouter.cs` — Serves main configuration.
  - `EFTQuestSettingsRouter.cs` — Serves EFT quest settings.
  - `QuestTemplatesRouter.cs` — Serves quest templates for bot questing.
  - `ZoneAndItemPositionsRouter.cs` — Serves zone/item position data.
  - `ScavRaidSettingsRouter.cs` — Serves Scav raid settings.
  - `USECChanceRouter.cs` — Serves USEC spawn chance data.
- **Router Framework (`Server/Routers/Internal/`):**
  - `RouteManager.cs`, `RouteInfo.cs`, `RequestData.cs` — Routing infrastructure.
  - `AbstractStaticRouter.cs`, `AbstractDynamicRouter.cs` — Base router classes.
  - `AbstractTypedStaticRouter.cs`, `AbstractTypedDynamicRouter.cs` — Typed router variants.
  - `HTTPResponseRepository.cs` — Response caching.
- **Utilities (`Server/Utils/`):**
  - `ConfigUtil.cs` — Configuration loading and management.
  - `LoggingUtil.cs` — Structured logging.
  - `ProfileUtil.cs` — Profile data manipulation.
  - `ObjectCache.cs` — In-memory caching utility.
- **Patches (`Server/Patches/`):**
  - `GenerateBotWavePatch.cs` — Modifies bot wave generation for PScav conversion.
  - `ServiceRepository.cs` — Service collection management.
- **Mod Integrity Tests (`Server/Utils/ModIntegrityTests/`):**
  - `IModIntegrityTest.cs` — Test interface.
  - `ClientLibraryExistsTest.cs` — Verifies client library is present.
  - `ArrayIsValidTest.cs` — Validates configuration arrays.

### 3. Tests (`Tests/`) — Unit & Integration Tests
- `BuildPropertyTests.cs` — Verifies build properties.
- `FileLocationTests.cs` — Validates file path configurations.
- `DI.cs` — Test dependency injection setup.
- `RunFromSptInstallDirectoryService.cs` — Test infrastructure.
- `MockLogger.cs`, `MockConfigUtil.cs` — Test mocks.

---

## Core Systems

### Quest-Selection Algorithm
1. **Filtering:** Filter all quests to those with valid locations on the current map that the bot qualifies for (level, role, etc.)
2. **Scoring (three weighted metrics):**
   - **Distance** — Normalized distance to each quest objective × `distance_weighting` + randomness
   - **Desirability** — Quest's desirability rating ÷ 100 × `desirability_weighting` + randomness
   - **Exfil Direction** — Angle between bot's path and its selected extract, favoring quests that lead toward extraction
3. **Selection:** Highest-scoring quest wins. Bot extracts via SAIN if no quests are available.

### Quest Types
| Type | Description |
|------|-------------|
| **EFT Quests** | Bots complete locations from actual EFT quests (place markers, collect items, kill targets) |
| **Spawn Rush** | At raid start, nearby bots rush toward the player's spawn point |
| **Boss Hunter** | Bots search boss spawn zones (early raid only, level-gated) |
| **Airdrop Chaser** | Bots run to recent airdrops (within 420s of landing) |
| **Spawn Point Wandering** | Fallback: wander between spawn points (disabled by default) |
| **Standard Quests** | Map-specific locations with camping/sniping/patrol behaviors |
| **Custom Quests** | User-defined quests placed in `quests/custom/` directory |

### Quest Data Structure
- **Quests** contain objectives → **Objectives** contain steps → **Steps** define positions and behaviors
- Step types: `MoveToPosition`, `HoldAtPosition`, `Ambush`, `Snipe`, `ToggleSwitch`, `RequestExtract`, `CloseNearbyDoors`
- Properties: repeatable, isCamping, isSniping, pmcsOnly, min/max level, max bots, desirability, raid time constraints, required switches, forbidden weapons, bot role filters, waypoints

### PMC/PScav Spawning System
- **Initial Wave:** Random number of PMCs (between map's min/max player count) spawn at EFT PMC spawn points at raid start
- **Progressive Spawning:** As PMCs die, replacements spawn at safe distances from players
- **Player Scavs:** Spawn on a schedule based on SPT's raid-time-reduction settings, weighted toward middle/late raid
- **Bot Cap Management:** Max alive bots limit prevents overcrowding; advanced system tricks EFT into treating PMCs/PScavs as human players (not counting toward bot caps)
- **Group Spawning:** Configurable group size distribution (solo through 5-man groups)
- **Compatibility:** Auto-disables when Unda or ABPS (Acid's Bot Placement System) are detected

### AI Limiter System
- Disables bots beyond configurable distance from players (200m default)
- Map-specific distances available
- Must be explicitly enabled per map

### Scav Spawn Restrictions
- **Exclusion Radius:** Scales with map size (~100m on Customs, ~17m on Factory)
- **Spawn Rate Limiting:** E.g., 2.5 Scavs/minute after threshold (10 initial Scavs)
- **Max Alive Scavs:** Cap on simultaneous assault-type Scavs (15 default)

---

## Dependencies

| Dependency | Required | Version | Purpose |
|-----------|----------|---------|---------|
| BigBrain | Required | 1.4.0+ | Custom bot behavior layers |
| Waypoints | Required | 1.8.2+ | Expanded patrol paths and NavMesh |
| SAIN | Recommended | 4.4.0+ | AI combat system, extraction |
| Looting Bots | Recommended | 1.6.3+ | Bot looting behavior |

### BigBrain Priority Guidance (with SAIN)
- BigBrain arbitration is numeric priority among layers whose `IsActive()` is true.
- Keep SAIN `LayerSettings` combat priorities (`SAINCombatSquadLayerPriority`, `SAINCombatSoloLayerPriority`) strictly above QuestingBots quest/navigation priorities.
- If QB quest layers are equal/higher, bots can stay in quest movement while SAIN has already removed vanilla assault layers, reducing combat reaction quality.
- Keep SAIN combat/extract higher, and tune QB so quest layers quickly drop out on contact.

---

## Configuration

Configuration is extensive, with 300+ settings organized into:

### Main Options
- `enabled` — Master enable/disable
- `max_calc_time_per_frame_ms` — CPU budget per frame (5ms default)
- `chance_of_being_hostile_toward_bosses` — Per-role boss hostility chances

### Questing Options (`questing.*`)
- Bot type allowlists (PMC, PScav, Scav, Boss)
- Pathfinding intervals, objective completion delays
- Stuck bot detection with jumping/vaulting remedies
- Door unlocking (per-bot-type, key chances, search radii)
- Hearing sensor (footstep/gunfire detection, headset/helmet modifiers)
- Sprinting limitations (stamina thresholds, corner approach, door approach)
- Quest selection weightings (distance, desirability, exfil direction)
- Extraction requirements (min alive time, quest count minimums)

### Spawning Options (`bot_spawns.*`)
- Per-bot-type enable/disable
- Min/max distances from players
- Group size distributions
- Bot difficulty distributions
- Bot cap adjustments (use EFT caps, map-specific overrides)
- Initial boss spawn limiting
- Scav spawn rate control

### Combat Reactivity Knobs (QB)
- **`brain_layer_priorities`**: keep QB quest/navigation priorities below SAIN combat/extract layer priorities.
- **Hearing sensor settings** (`HearingSensorConfig`): tune detection/suspend thresholds so nearby shots and steps suspend questing promptly.
- **Post-combat search cooldown** (`SearchTimeAfterCombatConfig`): tune resume/search delays so bots finish contact/search state before questing resumes.

---

## Known Issues
- Bots can get trapped in specific building areas (Dorms rooms, Lighthouse mountains)
- Bots take direct paths, often running through open areas without cover
- Certain bot brains are blacklisted (e.g., exUSEC near stationary weapons)
- Flickering occurs when EFT spawns bots
- Bots may unlock doors unnecessarily if quest locations can't be resolved
- Player Scav extraction requires SPT 4.0.14+ or SAIN with vanilla Scavs disabled

---

## Incompatibilities
- **AI Limit** — Disables questing bots, breaking their pathfinding (use built-in AI Limiter instead)
- **Unda** — Spawning system auto-disables when detected
- **ABPS (Acid's Bot Placement System)** — Spawning system auto-disables when detected
- **Vagabond** (partial) — Requires ABPS or similar for spawn management when using spawn point reduction
