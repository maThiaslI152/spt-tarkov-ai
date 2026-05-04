# BigBrain layer matrix (SAIN)

> **Purpose:** Static inventory of how SAIN registers custom layers and strips vanilla layers per brain / role. Use with `[SAIN DIAG][BigBrain]` logs and `SAINPerfLog` CSV under `BepInEx/LogOutput/sain_perf/`.  
> **Code:** [`OptimizedMod/SAIN/SAIN/Plugin/BigBrainHandler.cs`](../OptimizedMod/SAIN/SAIN/Plugin/BigBrainHandler.cs), [`LayerSettings.cs`](../OptimizedMod/SAIN/SAIN/Preset/GlobalSettings/Categories/General/LayerSettings.cs), [`VanillaBotSettings.cs`](../OptimizedMod/SAIN/SAIN/Preset/GlobalSettings/Categories/General/VanillaBotSettings.cs).

## Init and lifecycle

- `BigBrainHandler.Init()` → `BrainAssignment.Init()` runs once from [`SAINPlugin.Awake`](../OptimizedMod/SAIN/SAIN/SAINPlugin.cs).
- **Changing** `LayerSettings` priorities or `VanillaBotSettings` toggles requires a **full game restart** to re-register layers (no preset-time reapply in code today).

## Custom layer stack (all brains that receive SAIN layers)

Per registration path, layers are added in this **order** (BigBrain uses numeric **priority** for arbitration; higher wins among active layers):

| Layer type | Default priority | Source |
|------------|------------------|--------|
| `DebugLayer` | 99 | `BigBrainHandler` |
| `SAINAvoidThreatLayer` | 80 | `BigBrainHandler` |
| `ExtractLayer` | `SAINExtractLayerPriority` (default **65**) | Most paths; **omitted** for bosses/followers/goons |
| `CombatSquadLayer` | `SAINCombatSquadLayerPriority` (default **70**) | All paths below |
| `CombatSoloLayer` | `SAINCombatSoloLayerPriority` (default **69**) | All paths below |

### Registration by bot category

| Category | Brain name(s) | `WildSpawnType` roles | Extract layer |
|----------|---------------|------------------------|---------------|
| PMC | `PmcBear`, `PmcUsec` | — | Yes |
| Scav | `CursAssault`, `Assault` | — | Yes |
| Raider / `pmcBot` | `PMC` | `pmcBot` | Yes |
| Raider assault group | `PMC` | `assaultGroup` | Yes (via scav path + `AddCustomLayersToRaiders`) |
| PMC BEAR/USEC as raider brain | `PMC` | `pmcBEAR`, `pmcUSEC` | Yes (when `INCLUDE_RAIDER_BRAIN_FOR_PMCS`) |
| Rogue | `ExUsec` | — | Yes |
| Bloodhound | `ArenaFighter` | — | Yes |
| Boss | All `AIBrains.Bosses` | — | **No** |
| Follower | All `AIBrains.Followers` | — | **No** |
| Goon | `Knight`, `BirdEye`, `BigPipe` | — | **No** |
| Other (`Obdolbs`, …) | `AIBrains.Others` | — | Yes |

## Vanilla strip vs restore (`VanillaBotSettings`)

When the matching **Vanilla X** flag is **false**, SAIN **removes** the listed vanilla layer names for that category and **restores** SAIN custom layer names. When the flag is **true**, the inverse applies (vanilla on, SAIN layers removed for that scope).

Shared strip list: `_commonVanillaLayersToRemove` in `BigBrainHandler` (plus per-category extras in each `ToggleVanillaLayersFor*` method).

### `_commonVanillaLayersToRemove` (shared)

Applied wherever the spread operator `.. _commonVanillaLayersToRemove` is used for that brain list.

| Layer name string |
|-------------------|
| `Help`, `AdvAssaultTarget`, `AssaultEnemyFar`, `Hit`, `Simple Target`, `Pmc`, `AssaultHaveEnemy`, `Assault Building`, `Enemy Building`, `PushAndSup`, `Pursuit` |
| Plus **stationary / patrol-style** strips (see source; extend when logs show new vanilla `Name()` strings). |

### Per-category extra strips (in addition to common list)

| Toggle | Method | Extra layer names (high level) |
|--------|--------|--------------------------------|
| Vanilla PMCs | `ToggleVanillaLayersForPMCs` | `Request`, `KnightFight`, `PmcBear`, `PmcUsec` + common |
| Vanilla Scavs | `ToggleVanillaLayersForScavs` | `PmcBear`, `PmcUsec` + common |
| Vanilla Rogues | `ToggleVanillaLayersForRogues` | Same pattern as PMC-ish lists + common |
| Vanilla Bloodhounds | `ToggleVanillaLayersForBloodHounds` | Same as rogues-style list + common |
| Vanilla Bosses | `ToggleVanillaLayersForBosses` | Boss fight layers (`KnightFight`, `BirdEyeFight`, `BossBoarFight`, …) + common |
| Vanilla Followers | `ToggleVanillaLayersForFollowers` | Follower fight layers + common |
| Vanilla Goons | `ToggleVanillaLayersForGoons` | `KnightFight`, `BirdEyeFight`, `Kill logic` + common |
| Vanilla Others | `ToggleVanillaLayersForOthers` | `Request`, `KnightFight`, `PmcBear`, `PmcUsec` + common |
| Raiders (`pmcBot`) | `ToggleVanillaLayersForRaiders` | Same as PMC list + common, keyed on brain `PMC` + role |

`ToggleVanillaLayersForRaiders([WildSpawnType.pmcBot], false)` is always invoked from `ToggleVanillaLayersForAllBots` (not gated on a Vanilla toggle — see `BigBrainHandler`).

## Third-party BigBrain consumers (detected / relevant)

| Mod | Detection | Notes |
|-----|-------------|--------|
| QuestingBots | `ModDetection.QuestingBotsLoaded` | Defaults in [`SPTQuestingBots/Shared/Config/config.json`](../SPTQuestingBots/Shared/Config/config.json): **`with_sain`** quest **68** / follow **69** / regroup **72** (below SAIN extract **~74** and combat **~77–78**). |
| LootingBots | `ModDetection.LootingBotsLoaded` | Active layer name `"Looting"`; [`SAINLayer`](../OptimizedMod/SAIN/SAIN/Layers/SAINLayer.cs) unpause logic. Default loot priority **~62** vs QB quest **68** vs SAIN combat **~77–78**. |
| BigBrain | BepInDependency | Version pin: `AssemblyInfoClass.BigBrainVersion`. |

## In-raid repro matrix (manual)

| Bot type | Map suggestion | QuestingBots | Vanilla toggle for class | Evidence |
|----------|----------------|--------------|---------------------------|----------|
| PMC | Any | Off / On | Vanilla PMCs off | `LogOutput.log` + `sain_perf/*.csv` |
| Scav | Customs / Factory | Off / On | Vanilla Scavs off | same |
| Raider | Reserve / Labs | Off / On | — | same |
| Rogue (`exUsec`) | Lighthouse | Off / On | Vanilla Rogues off | same |

Enable **F12 → SAINPerfLog → Diagnostic Logging** for `[SAIN DIAG][BigBrain]` lines. Optional **verbose sample** logs every human-proximate bot’s active layer on the same interval.

## Gap log (fill from live raids)

When `[SAIN DIAG][BigBrain]` shows a vanilla or unexpected layer name that is **not** stripped, add the exact `BrainManager.GetActiveLayerName` string to `_commonVanillaLayersToRemove` or the appropriate `ToggleVanillaLayersFor*` list in `BigBrainHandler.cs`, then document it here.

| Active layer string (from log) | Map / bot | Fixed in code? |
|--------------------------------|-------------|----------------|
| *(example)* | | |

## Related docs

- [`BUGFIX-BigBrainPriority-QuestingBots.md`](BUGFIX-BigBrainPriority-QuestingBots.md)
- [`INTEGRATION.md`](INTEGRATION.md) — SAIN ↔ BigBrain
- [`SAIN_PERFLOG_STANDALONE_PLAN.md`](SAIN_PERFLOG_STANDALONE_PLAN.md)
