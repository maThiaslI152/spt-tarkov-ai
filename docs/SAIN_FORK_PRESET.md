# SAIN fork preset: Optimized (Harder PMCs)

This project **customizes SAIN through a shipped bootstrap preset**: the forked plugin creates (if missing) and can auto-select a custom preset aligned with **player-centric LOD** and the frame-budget stack documented in [AI_BUDGET_LOD_PLAN.md](AI_BUDGET_LOD_PLAN.md). Vanilla SAIN does not include this behavior.

**Agents:** [INDEX.md](../INDEX.md) · [docs/AI_BUDGET_LOD_PLAN.md](AI_BUDGET_LOD_PLAN.md) · [docs/OPTIMIZED_MOD_README.md](OPTIMIZED_MOD_README.md).

## Where it lives on disk

SAIN resolves paths from **`SAIN.dll`’s directory** (see `JsonUtility.GetSAINPluginPath()`).

| Item | Path (always under your SPT install) |
|------|--------------------------------------|
| Plugin root | `BepInEx/plugins/SAIN/` |
| All presets | `BepInEx/plugins/SAIN/Presets/` |
| **This fork’s preset** | `BepInEx/plugins/SAIN/Presets/Optimized (Harder PMCs)/` |
| Editor / selection | `BepInEx/plugins/SAIN/Presets/ConfigSettings.json` |

Example (typical dev layout): `E:\SPT 4.0 Dev\BepInEx\plugins\SAIN\Presets\Optimized (Harder PMCs)\`.

**If that folder does not exist:** you are almost certainly not running the **SAIN.dll built from this repo** (`dotnet build OptimizedMod/SAIN/SAIN.csproj -c Release`, then copy the output DLL into `BepInEx/plugins/SAIN/`). Folders like `* [Modified]` come from the normal SAIN editor when saving a built-in preset; they are unrelated to this bootstrap preset name.

## What it is

On startup, SAIN ensures a **custom** preset named **`Optimized (Harder PMCs)`** exists (constant `SAINPlugin.ForkOptimizedPresetName` in [`OptimizedMod/SAIN/SAIN/SAINPlugin.cs`](../OptimizedMod/SAIN/SAIN/SAINPlugin.cs)).

- **Base:** `SAINDifficulty.harderpmcs` — `Info.json` uses `BaseSAINDifficulty = harderpmcs` so [`PresetHandler.InitPresetFromDefinition`](../OptimizedMod/SAIN/SAIN/Plugin/PresetHandler.cs) still runs `UpdateDefaults` against the built-in **Harder PMCs** in-memory preset (PMC tuning from [`SAINDifficultyClass`](../OptimizedMod/SAIN/SAIN/Plugin/SAINDifficultyClass.cs), not duplicated in plugin code).
- **Fork-only tuning** (`ApplyForkOptimizedTuning`):
  - `GlobalSettings.General.Performance.PerformanceMode = true`
  - `MaxAiBudgetMilliseconds = 3` (clamped 1–10) — slightly more headroom than the default 2 ms for visible-tier round-robin under load (see [AI_BUDGET_LOD_PLAN.md](AI_BUDGET_LOD_PLAN.md)).
  - `Difficulty.HearingDistanceCoef = 0.85` — modest trim vs extreme cuts, to reduce odd behavior when vision is tiered.

We **do not** apply global “very hard” combat scalars or blanket `DifficultyModifier` boosts to every bot type; those fought the player-centric / LOD story.

## Parameters and NPC behavior

This section explains **what changes for bots** (NPCs) in plain language. Values are the fork bootstrap defaults unless you edit the preset in the SAIN editor or `GlobalSettings.json`.

### 1. Fork-only overrides (`ApplyForkOptimizedTuning` in code)

These three are the **only** fields the fork writes programmatically on top of the merged **Harder PMCs** baseline.

| Parameter | Location in preset | Fork value | What it does to NPCs |
|-----------|-------------------|------------|------------------------|
| **Performance mode** | `General.Performance.PerformanceMode` | `true` | **Master switch** for distance-aware SAIN optimizations. When on, bots use the performance code paths below (slower/throttled work for far tiers, cheaper vision jobs, etc.). When off, many of those paths stay at full-rate behavior regardless of other sliders. |
| **Max AI frame budget** | `General.Performance.MaxAiBudgetMilliseconds` | `3` (clamped 1–10) | Caps how many milliseconds of SAIN **`ManualUpdate`** work may run **per Unity frame** across all bots, split by perception tier (`AIFrameBudgetScheduler` in [`BotManagerComponent`](../OptimizedMod/SAIN/SAIN/Components/BotManagerComponent.cs)). **Higher** = more bots get a full tick each frame (smoother motion/decisions under load) at **higher CPU**. **Lower** = more time-slicing / skipped frames (better FPS, risk of jitter). Fork uses **3 ms** vs stock default **2 ms** for a bit more headroom with LOD. |
| **Hearing distance (global)** | `Difficulty.HearingDistanceCoef` | `0.85` | Global multiplier merged into each bot’s **`HearingDistanceModifier`** ([`BotDifficultyClass`](../OptimizedMod/SAIN/SAIN/Classes/Bot/Info/BotDifficultyClass.cs)), then used when deciding if a sound is within effective range ([`HearingAnalysisClass`](../OptimizedMod/SAIN/SAIN/Classes/Bot/Sense/Hearing/HearingAnalysisClass.cs) — see `FinalModifier` / `FinalRange`). **Lower than 1** = bots treat gunfire/footsteps as **audible over a shorter distance** (slightly easier to stay quiet at range; less “pre-aim through audio” when vision ticks are tiered). **Higher** = hear farther (harder). Stacks with bot-type, personality, and map **location** hearing coefficients. |

### 2. Harder PMCs baseline (merged from built-in, not re-listed in fork code)

`Info.json` keeps `BaseSAINDifficulty = harderpmcs`. After load, [`PresetHandler.InitPresetFromDefinition`](../OptimizedMod/SAIN/SAIN/Plugin/PresetHandler.cs) calls `UpdateDefaults` with the in-memory **Harder PMCs** preset, so **BEAR / USEC** get the **`ApplyHarderPMCs`** tuning in [`SAINDifficultyClass`](../OptimizedMod/SAIN/SAIN/Plugin/SAINDifficultyClass.cs) (≈ `CreateHarderPMCsPreset` / `ApplyHarderPMCs`): tighter aim times, higher weapon proficiency, lower scatter, faster precision / slower accuracy pacing, higher gain-sight and visible-distance coefficients, higher aggression, **no** center-mass aim bias by default, etc. **Scavs and other types** keep the normal **Harder PMCs** mix for their spawn category (not the extra global “very hard” blanket the old fork plugin used to apply).

### 3. Other `General.Performance` fields (defaults in JSON; NPC effect when `PerformanceMode` is on)

Defined in [`PerformanceSettings.cs`](../OptimizedMod/SAIN/SAIN/Preset/GlobalSettings/Categories/General/PerformanceSettings.cs). The fork **does not** change these from their preset defaults; they matter once **Performance mode** is on.

| Parameter | Default (typical) | What it does to NPCs |
|-----------|-------------------|------------------------|
| **Vision raycast frequency** | 30 Hz | How often vision raycast jobs run (`VisionRaycastJob`). **Lower** = slower line-of-sight / visibility updates, **less CPU**, bots react to new LOS slightly slower. |
| **Look update frequency** | 30 Hz | How often the EFT look sensor is driven from SAIN (`VisionRaycastJob` / `UpdateEFTVision`). **Lower** = slower target acquisition updates, less CPU. |
| **Cover find frequency** | 10 Hz | Base rate for cover search (`CoverFinderComponent.GetCoverInterval`). **Lower** = bots repick cover **less often** (can feel slower to hug new hard cover). Far / very-far AI limit tiers stretch the interval further. |
| **Max raycasts per body part** | 3 | 1 = LOS only, 2 = LOS+vision, 3 = LOS+vision+shoot. **Lower** = cheaper, may miss some checks. |
| **Single-part vision beyond distance (m)** | 150 (clamped 50–500 in code) | Beyond this range to a target, [`VisionRaycastJob`](../OptimizedMod/SAIN/SAIN/Classes/BotManager/Jobs/VisionRaycastJob.cs) checks **one** body part by default to save CPU. |
| **Vision: full body parts for human beyond distance** | false | When **true**, **human** (PMC/Player) targets still get **full** per-part rays beyond the distance above; **AI** targets stay single-part beyond that distance. |
| **Far / Very far / Narnia bot CPU reduction** | 0.5 / 0.25 / 0 | Multipliers on vision-related cost for AI-limit distance tiers. **Lower** = less vision work for bots far from the player (cheaper; they “see” with less fidelity). |

**Where `PerformanceMode` branches in code (examples):**

- **Combat / search decisions:** [`BotDecisionManager.GetDecisionFrequency`](../OptimizedMod/SAIN/SAIN/Classes/Bot/Decision/BotDecisionManager.cs) — far and very-far bots refresh decisions less often.
- **Cover:** [`CoverFinderComponent.GetCoverInterval`](../OptimizedMod/SAIN/SAIN/Components/CoverFinderComponent.cs) — uses `CoverFindFrequency` and AI-limit tier multipliers.
- **Enemy list maintenance:** [`SAINEnemyController`](../OptimizedMod/SAIN/SAIN/Classes/Bot/EnemyControllers/SAINEnemyController.cs) — full enemy iteration **10 Hz** vs **20 Hz** when performance mode is on.
- **Vision jobs:** [`VisionRaycastJob`](../OptimizedMod/SAIN/SAIN/Classes/BotManager/Jobs/VisionRaycastJob.cs) — uses the raycast / look frequencies, `MaxRaycastsPerEnemy`, **`VisionSinglePartBeyondDistanceMeters`**, and **`VisionUseFullPartsForHumanBeyondDistance`** (always read from preset; not gated on `PerformanceMode` alone).
- **Sound cache cadence:** [`GameWorldComponent.GetBotCacheInterval`](../OptimizedMod/SAIN/SAIN/Components/GameWorldComponent.cs) — can stretch how often bot sound caches run when many SAIN bots are active.

For **budget vs jitter** and tier behavior, see [AI_BUDGET_LOD_PLAN.md](AI_BUDGET_LOD_PLAN.md).

### 4. Global `Difficulty.*` (SAIN global difficulty block)

Stored under `GlobalSettings.Difficulty` in the same `GlobalSettings.json`. The fork only sets **`HearingDistanceCoef`** there; other fields keep merged **Harder PMCs** values. In-game tooltips come from [`DifficultySettings.cs`](../OptimizedMod/SAIN/SAIN/Preset/DifficultySettings.cs) (e.g. `VisibleDistCoef`, `GainSightCoef`, `ScatteringCoef`, `AggressionCoef`, `PRECISION_SPEED_COEF`, `ACCURACY_SPEED_COEF`). Those feed EFT bot difficulty modifiers in [`BotDifficultyClass.applyGlobal`](../OptimizedMod/SAIN/SAIN/Classes/Bot/Info/BotDifficultyClass.cs) — **higher visible / gain-sight** generally means bots spot you sooner/farther; **lower scatter** means tighter shooting; **aggression** affects search and return-fire timing per attribute description.

## When it becomes the active preset

| Situation | Behavior |
|-----------|----------|
| **No `Presets/ConfigSettings.json`** (`EditorDefaultsLoadedFromDisk == false`) | After `PresetHandler.Init()`, if the fork preset exists, SAIN loads it and saves editor defaults so **custom `Optimized (Harder PMCs)`** is selected (`SelectedDefaultPreset = none`). |
| **Preset created this run** and **user already had** `ConfigSettings.json` | Editor selection is **restored** (re-init from saved defaults) so an existing built-in/custom choice is not overwritten. |
| **User already has** a saved preset choice | Unchanged. |

`PresetHandler.InitPresetFromDefinition` gained an optional `exportEditorDefaults` flag so first-time preset creation can defer editor export until the logic above runs (avoids clearing `SelectedCustomPreset` when a built-in difficulty was saved).

## Removing or resetting

- Delete the folder `BepInEx/plugins/SAIN/Presets/Optimized (Harder PMCs)/` (path may match your install layout) to remove the preset; next launch it will be recreated if the bootstrap still runs.
- Pick another preset in the SAIN editor as usual; `ConfigSettings.json` stores the choice.

## Build

```bash
dotnet build OptimizedMod/SAIN/SAIN.csproj -c Release
```
