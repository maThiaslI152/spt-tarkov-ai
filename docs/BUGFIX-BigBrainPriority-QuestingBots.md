# BUGFIX — BigBrain Priority Arbitration (QuestingBots vs SAIN Combat)

> Last updated: 2026-05-03
> Scope: `OptimizedMod/SAIN/SAIN/`

---

## Problem

In raids with QuestingBots enabled, some bots stayed in quest/navigation behavior during nearby combat and showed run-stop jitter or delayed combat reaction.

The issue was not always AI frame budget exhaustion. Perf CSVs often showed healthy budget headroom while behavior still looked wrong, indicating arbitration or gating faults rather than raw CPU starvation.

---

## Root Cause

Multiple factors combined:

1. **BigBrain arbitration mismatch**
   - Active layer selection is strictly numeric priority among currently active layers.
   - If a non-combat layer remains active with higher/equal effective priority, SAIN combat can be delayed or pre-empted.

2. **Quest gating too dependent on `GoalEnemy`**
   - `CanBotQuest` combat checks could miss valid threat states when no current `GoalEnemy` was set.
   - This allowed quest continuation in cases where combat pressure still existed.

3. **Under-fire recency logic**
   - Under-fire recency handling needed to align with elapsed-time semantics (`Time.time - UnderFireTime`) used elsewhere in SAIN behavior.

---

## Fixes Implemented

## 1) Hardened quest/combat gating (`SAINExternal`)

File: `OptimizedMod/SAIN/SAIN/Interop/SAINExternal.cs`

- `IsBotInCombat` now checks in this order:
  - `Memory.IsUnderFire`
  - recent under-fire via `Time.time - UnderFireTime <= threshold`
  - QuestingBots threat signals (when loaded)
  - then enemy visibility/recent seen/recent heard
- Added null guard for `component?.BotOwner?.Memory`.
- Added `QuestingBotsThreatSignal` reason.
- Added conservative QB threat signals:
  - `HumanEnemyInLineofSight`
  - `ActiveHumanEnemy && !AtPeace`
  - recent known human threats (`KnownEnemies`) by seen/heard windows.

Result: `CanBotQuest` now blocks quest behavior more reliably during real combat pressure, even when `GoalEnemy` is transiently null.

## 2) Minimal BigBrain priority diagnostics

File: `OptimizedMod/SAIN/SAIN/Components/BotManagerComponent.cs`

- Added `MaybeLogBigBrainArbitrationHints(currentTime)` in `ManualUpdate`.
- Diagnostics are:
  - **gated** by `SAINPerformanceMonitor.DiagnosticLogging`
  - **gated** by `ModDetection.QuestingBotsLoaded`
  - **rate-limited** (3s interval)
  - **proximity-filtered** (near human players only)
  - emitted only when active layer appears mismatched versus threat signals.
- Logs include:
  - `BrainManager.GetActiveLayerName(botOwner)`
  - `SAINLayersActive`
  - `GoalEnemy`
  - `CurrentCombatDecision`
  - `CurrentSquadDecision`

Result: enough signal to confirm BigBrain arbitration issues in-raid without adding broad behavior spam.

## 3) Squad coordination conflict guard (related behavior stabilizer)

Files:
- `OptimizedMod/SAIN/SAIN/Layers/Combat/Squad/SquadCombatCoordinator.cs`
- `OptimizedMod/SAIN/SAIN/Layers/Combat/Squad/CombatSquadLayer.cs`

- Squad coordinator skips squad decision writes when member solo combat decision is active.
- Squad layer remains active only when solo combat decision is `None`.

Result: reduces run-stop jitter and prevents squad writes from stomping solo combat decisions.

---

## Verification

- Build: `dotnet build OptimizedMod/SAIN/SAIN.csproj -c Release` -> success.
- Existing warnings remain pre-existing cleanup warnings; no new errors introduced by this fix set.

Recommended runtime verification:

1. Enable F12 -> SAIN Performance -> `Diagnostic Logging`.
2. Run raid with QuestingBots enabled and reproduce previous passive-combat scenario.
3. Check `BepInEx/LogOutput.log` for `[SAIN DIAG][BigBrain]` lines.
4. Confirm active layer and SAIN combat signals align with expected behavior.

---

## Current Status

- **Implemented in code:** yes
- **Build verified:** yes
- **In-raid validation pass:** pending (manual raid repro/validation)

