# BUGFIX — Multi-map runtime finding: `GoalHumanFinal*` visibility failure + non-combat layer arbitration

> **Date:** 2026-05-05  
> **Status:** Fix landed 2026-05-06 — same-frame vision handoff after `VisionRaycastJob` batch (`FinalizeVisionHandoffFromRayBatch`); per-enemy try/catch so the coroutine cannot die globally. `EnemyInfo.SetVisible(true)` sync was **removed** 2026-05-06 after playtest regression reports (prefer SAIN `IsVisible` OR only). **Playtest validation** still required.  
> **See also:** [STATUS-SAIN-PMC-NoCombat-Layers-Paused.md](STATUS-SAIN-PMC-NoCombat-Layers-Paused.md) — Customs `edd84743` retest: BotMind questing off, PMC SAIN combat signals still absent; Bloodhound OK; investigation paused due to slow raid cycles.  
> **Scope:** SAIN player-visibility finalization path + BigBrain arbitration under combat pressure
> **Data source:** `E:\SPT 4.0 Dev\BepInEx\LogOutput\sain_perf\`

---

## Executive finding

Across multiple non-Factory maps, bots often enter combat pressure states and keep AI-vs-AI behavior, but **do not convert player-facing visibility into `GoalHumanFinalVisibleCount` / `GoalHumanFinalCanShootCount`**.  
At the same time, **`Looting` / `BotMind_Questing`** still appears during combat-pressure windows in mismatch samples.

This is now reproducible beyond Lighthouse.

---

## Sessions analyzed (schema 8)

| Session | Map | Key result |
| --- | --- | --- |
| `fdfafc2a` | Lighthouse | `GoalHumanSum=39`, `FinalVisible=0`, `FinalCanShoot=0`, `MismatchSum=22` |
| `4ad5ffb0` | Lighthouse | `GoalHumanSum=47`, `FinalVisible=0`, `FinalCanShoot=0` |
| `61e73492` | Customs (`bigmap`) | `GoalHumanSum=81`, `FinalVisible=0`, `FinalCanShoot=0`, `VisionEffectiveVisionMax=0` |
| `a5193d01` | Interchange | `GoalHumanSum=29`, `FinalVisible=0`, `FinalCanShoot=0` |
| `c7462f44` | Reserve (`RezervBase`) | `GoalHumanSum=168`, `FinalVisible=1`, `FinalCanShoot=1` (partial improvement, still poor conversion) |
| `aae87b9f` | Factory (control) | `GoalHumanSum=76`, `FinalVisible=14`, `FinalCanShoot=14` (mostly healthy) |

---

## What this implies

### 1) Not a scheduler budget starvation issue

From paired `sain_perf_*.csv`:

- `BudgetExhaustedNow` stays `0`
- `ProcessedBots ~= TotalOnline`
- `BudgetMs` is low versus `BudgetLimitMs`

So the failure is not explained by AI budget exhaustion.

### 2) Vision rays are being attempted, but conversion is poor

In failing runs, `VisionRayAttempt*` is non-zero, and some runs have non-zero `VisionRayEffective*`, yet `GoalHumanFinal*` remains near-zero.  
This points to a likely issue in the **handoff/finalization path** of player visibility/can-shoot state rather than a pure “no rays scheduled” condition.

### 3) Arbitration remains a secondary contributor

Mismatch rows still show `thirdPartyOrVanilla` and combat-pressure non-SAIN-combat cases, with layer histograms dominated by `Looting` / `BotMind_Questing` during pressure windows.  
This matches observed “move-stop” / “stuck in non-combat layer” behavior.

---

## Hypotheses to test in code (next patch pass)

1. **Primary (fix 2026-05-06):** Finalization **lagged** the ray batch: `EnemyPartsClass.Update` / `EnemyVisionClass.UpdateVisibleState` ran on the throttled `SAINEnemyController` cadence (~10–20 Hz) while `VisionRaycastJob.AnalyzeHits` wrote `EnemyPartDataClass` immediately — same-frame handoff via `FinalizeVisionHandoffFromRayBatch` (with per-enemy exception containment so the job coroutine cannot stop for all bots).
2. **Secondary:** Layer arbitration under combat pressure still allows non-combat layers to win in specific windows (unchanged by this patch; Phase F).
3. **Map sensitivity:** Occlusion/collider/trigger conditions amplify symptoms on Lighthouse/Customs/Interchange — re-validate after playtest.

---

## Patch priorities

1. **Shipped:** Same-frame `GoalHuman*` handoff after `VisionRaycastJob` (`VisionRaycastJob.cs` — `FinalizeVisionHandoffFromRayBatch` + coroutine-safe try/catch). No forced `EnemyInfo.SetVisible(true)`.
2. Keep strict combat-pressure gating against looting/quest layers during active threat windows (Phase F).
3. Re-run validation on Lighthouse + one control map (Factory or Reserve); compare `GoalHumanFinal*` to pre-fix CSVs.

---

## Verification criteria for the fix

For each validation raid:

- `GoalHumanCount > 0` should produce non-trivial `GoalHumanFinalVisibleCount` and `GoalHumanFinalCanShootCount`
- `VisionRayAttempt*` and `VisionRayEffective*` should correlate with `GoalHumanFinal*` increases
- `MismatchCombatSignals` and `thirdPartyOrVanilla` exemplars should trend down during sustained pressure
- `BudgetExhaustedNow` remains near zero (no regression in scheduler budget behavior)

