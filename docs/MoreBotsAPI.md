# MoreBotsAPI

## Overview

**Author:** TacticalToaster  
**SPT Version:** ~4.0.0  
**License:** CC BY-NC-SA 4.0  
**Repository:** https://github.com/maThiaslI152/MoreBotsAPI.git

MoreBotsAPI is a client and server API framework for SPT (Single Player Tarkov) 4.0.X that simplifies the process of creating custom bot types — bosses, factions, and other bot variants. It provides the infrastructure needed to add new `WildSpawnType` enum values on the client side and properly register custom bot data on the server side, handling the complex interoperability between SPT's systems.

---

## Architecture

The solution (`MoreBotsAPI.sln`) consists of **three projects**:

### 1. Prepatch (`Prepatch/`) — Client Pre-patcher
- **Role:** Runs before BepInEx plugin assembly loading to inject custom `WildSpawnType` enum values into `Assembly-CSharp.dll` via Mono.Cecil.
- **Key components:**
  - `PrePatch.cs` — Entry point; discovers and invokes all `WildSpawnTypePatch` implementations from dependent mods.
  - `CustomWildSpawnType.cs` — Data model for a custom bot type (value, name, scav role, brain type, boss/follower flags, excluded difficulties, SAIN settings).
  - `CustomWildSpawnTypeManager.cs` — Registry that manages custom spawn type registration, suitable groups (boss + followers), and SAIN settings.
  - `SAINSettings.cs` — Configuration model for SAIN bot personality/settings.
  - `WildSpawnTypePatch.cs` — Template for dependent mods to implement; uses Mono.Cecil to patch the `WildSpawnType` enum.
  - `ClientInfo.cs` — GUID, plugin name, and version constants.
  - `Utils.cs` — Helper for enum value injection.

### 2. Plugin (`Plugin/`) — Client Plugin (BepInEx)
- **Role:** Runtime plugin that initializes custom bot types, registers patches, and manages in-raid systems.
- **Key components:**
  - `Plugin.cs` — Main BepInEx plugin entry point. Initializes excluded difficulties, bot settings, SAIN compatibility, Fika (co-op) interop, hunt system, faction system, and zone debugging.
  - **Patches (`Plugin/Patches/`):**
    - `TarkovInitPatch.cs` — Hooks game initialization for SAIN integration.
    - `FixRaidEndSpawnTypePatch.cs` — Ensures custom spawn types survive raid-end transitions.
    - `StandartBotBrainActivatePatch.cs` — Enables custom brain layers via BigBrain.
    - `SuitableFollowersListPatch.cs` — Injects custom group formations.
    - `FenceLoyaltyWarnPatch.cs` — Handles Fence loyalty warnings for custom bots.
    - `FactionRaidEndPatch.cs` — Manages faction state at raid end.
    - `BotsGroupIsPlayerEnemyPatch.cs` — Configures enemy relationships.
    - `BotsControllerInitPatch.cs` — Initializes bot controllers with custom data.
  - **Components (`Plugin/Components/`):**
    - `FactionManager.cs` — Manages faction alliances, revenge systems, and enemy/friendly/warn relationships. Persists revenge state across raids via server API calls.
    - `HuntManager.cs` — Singleton that manages bot hunting behavior; assigns hunt targets, manages hunt events, and coordinates group hunting.
    - `BotHuntManager.cs` — Per-bot hunt behavior component.
    - `ZoneDebugComponent.cs` — Debug visualization for bot zones.
  - **Behavior (`Plugin/Behavior/`):**
    - `Layers/HuntTarget.cs` — BigBrain layer for hunt targeting.
    - `Actions/SearchForTargetAction.cs` — BigBrain action for searching.
    - `Actions/HuntTargetAction.cs` — BigBrain action for pursuing targets.
    - `Actions/HuntRegroupAction.cs` — BigBrain action for regrouping.
    - `Actions/GoToCustomAction.cs` — Custom movement action.
  - **Interop (`Plugin/Interop/`):**
    - `SAINInterop.cs` — Integration with SAIN (Solarint's AI Modifications).
    - `FikaInterop.cs` — Integration with Fika (co-op multiplayer mod).
  - **Models (`Plugin/Models/`):**
    - `Faction.cs` — Client-side faction model.
    - `UpdateRevengeRequest.cs` — Revenge update request model.

### 3. Server (`Server/`) — SPT Server Mod
- **Role:** Registers custom bot types in the server database, provides API endpoints for faction management and revenge tracking.
- **Key components:**
  - `Mod.cs` — Server entry point. Defines mod metadata (`com.morebotsapi.tacticaltoaster`, v2.0.1), injects the `MoreBotsAPI` service, registers HTTP routers for factions, revenge, and bot difficulties.
  - **Services (`Server/Services/`):**
    - `CustomBotTypeService.cs` — Loads bot type JSONs from `db/bots/types/`, registers them in the SPT database, supports shared type definitions and type replacement (partial settings override without redefining entire types).
    - `CustomBotConfigService.cs` — Loads bot configuration JSONs from `db/bots/config/`.
    - `ConfigService.cs` — Loads and manages mod configuration (`config.jsonc`).
    - `LoadoutService.cs` — Manages custom bot loadouts/equipment.
    - `FactionService.cs` — Comprehensive faction system that manages alliances, enemies, friendlies, warnings, revenge mechanics, and sub-faction hierarchies. Pre-defines vanilla factions (raiders, rogues, smugglers, bloodhounds, scavs, cultists, infected, USEC, BEAR, and all boss factions).
  - **API Endpoints:**
    - `GET /singleplayer/settings/bot/difficulties` — Returns bot difficulty settings for custom types.
    - `GET /morebotsapi/getfactions` — Returns all faction definitions.
    - `POST /morebotsapi/updaterevenge` — Updates revenge raid counters.
    - `GET /morebotsapi/getrevenges` — Returns active revenge factions for profiles.
  - **Models (`Server/Models/`):** `ProfileData`, `Faction`, `BotTypeConfig`, `BotTypeReplace`, `LoadoutInfo`, `MainConfig`, `UpdateRevengeRequest`, `MoreBotsLoadOrder`.

### 4. Root Project (`MoreBotsPlugin.csproj`)
- References the UNTAR Go Home mod as an example implementation (TacticalToaster's custom UNTAR faction mod).
- Demonstrates how to build a complete custom bot mod on top of MoreBotsAPI.

---

## Integration Points

| System | Integration Type | Details |
|--------|-----------------|---------|
| **SPT** | Required | SPT 4.0.X server and client |
| **BepInEx** | Required | Plugin framework for EFT modding |
| **BigBrain** | Soft Dependency | Custom bot behavior layers |
| **SAIN** | Soft Dependency | AI combat system compatibility via `SAINSettings` |
| **Fika** | Soft Dependency | Co-op multiplayer support |
| **Harmony** | Required | Runtime patching |
| **Mono.Cecil** | Required | Pre-patch IL weaving |

---

## How to Use as a Modder

### Client Side (Pre-patcher)
1. Set hard dependency: `[BepInDependency("com.morebotsapiprepatch.tacticaltoaster")]`
2. Create a `WildSpawnTypePatch` targeting `Assembly-CSharp.dll`
3. Define `CustomWildSpawnType` instances with enum values, boss/follower flags, excluded difficulties
4. Optionally define `SAINSettings` for SAIN compatibility
5. Register via `CustomWildSpawnTypeManager.RegisterWildSpawnType()`
6. Define suitable groups via `CustomWildSpawnTypeManager.AddSuitableGroup()`

### Server Side
1. Set dependency: `"com.morebotsapi.tacticaltoaster"` in mod metadata
2. Implement `IOnLoad` with `TypePriority = OnLoadOrder.PostDBModLoader + 2`
3. Inject `MoreBotsServer.MoreBotsAPI` and call `LoadBots(Assembly.GetExecutingAssembly())`
4. Place bot type data in `db/bots/types/` and config in `db/bots/config/` within your mod folder

### Enum Value Convention
To avoid conflicts, use ASCII-based enum ranges:
1. Pick two uppercase chars (e.g., "TT" for TacticalToaster)
2. Get ASCII decimal: TT = 84,84 → 8484
3. Add two zeros: 848400 → range 848400-848499
4. Do NOT use values 0-200 (reserved for vanilla EFT)

---

## Configuration (`config.jsonc`)

```jsonc
{
  "enableDebugLogs": false,
  "increaseBotCapAmount": 5
}
```

---

## Key Features

- **Custom Bot Type Injection:** Adds new values to `WildSpawnType` enum at the IL level
- **Server-Side Registration:** Properly registers bot types, configs, and difficulties in SPT's database
- **Faction System:** Full faction management with alliances, enemies, friendly fire warnings, and revenge mechanics
- **Revenge System:** Persistent cross-raid revenge tracking for faction-based vendettas
- **Hunt System:** Bot hunting behavior with priority targets and group coordination
- **SAIN Integration:** Built-in SAINSettings model for seamless SAIN compatibility
- **Fika Support:** Co-op multiplayer awareness (replicates revenge state only on server)
- **Type Replacement:** Partial settings override without redefining entire bot types
- **Shared Types:** Multiple bot types can share a single definition file
- **Debug Tools:** Zone outline visualization
