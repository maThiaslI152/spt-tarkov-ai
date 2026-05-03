# AI budget and LOD roadmap (player-visible vs off-screen)

This document captures the **target behavior**, **why STALKER Anomaly comparisons differ**, **how current SAIN code aligns**, and a **checklist** for closing gaps. It complements the implementation detail in [PERFORMANCE_ARCHITECTURE.md](PERFORMANCE_ARCHITECTURE.md).

**Agents:** [INDEX.md](../INDEX.md) · [AGENTS.md](AGENTS.md).

---

## Target goal

| Relationship to player | Expected behavior |
|------------------------|---------------------|
| **Player sees / is in direct engagement** | AI updates often enough that movement and combat feel **continuous** (no run/stop jitter, no “short attention span”). |
| **Player does not see** (occluded, far, low relevance) | AI stays under a **CPU budget** but remains **plausible**: navigation, loot/quest-directed movement, distant fights at reduced fidelity. |

Design principle (same as performance doc): **player-centric LOD** — spend CPU where the player’s experience is formed; fake or simplify where they are not looking.

---

## Why Anomaly ALife can feel “seamless” while Tarkov + frame budget can feel twitchy

- **Anomaly** typically budgets **many cheap offline agents**; combatants that matter often run **full-detail or high-priority** paths so engagement is not starved.
- **SAIN’s scheduler** (`AIFrameBudgetScheduler`) applies a **global per-frame millisecond cap** and **time-sliced round-robin** per perception tier. When the cap is hit, some bots **skip an entire `ManualUpdate` for that frame** — not “run cheaper logic,” but **no tick**. That uneven cadence affects motion and decisions together and reads as **jitter or lost attention**.
- **EFT** still runs vanilla **BotOwner / brain / locomotion** outside SAIN’s budget story; uneven SAIN ticks can **desynchronize** from baseline bot motion.

So the gap is not “budget bad,” but **what we throttle** (whole bot vs subsystem) and **whether visible threats have hard guarantees**.

---

## Current code alignment (verified)

### Already aligned with the LOD philosophy

| Mechanism | Location | Role |
|-----------|----------|------|
| Per-frame AI budget + tiers | [`OptimizedMod/SAIN/SAIN/Components/AIFrameBudgetScheduler.cs`](../OptimizedMod/SAIN/SAIN/Components/AIFrameBudgetScheduler.cs) | `MaxAIBudgetMs` (default **2 ms**), **Visible / Audible / Occluded** lists from `bot.CurrentPerceptionTier`, tier slices (`VisibleTierBudgetFraction` ≈ **0.45**, `AudibleTierCumulativeFraction` ≈ **0.88**), **round-robin** with resume indices, offline squad combat ≤ **1 Hz**. |
| Perception tier → tick rate | [`OptimizedMod/SAIN/SAIN/Classes/Bot/SAINAILimit.cs`](../OptimizedMod/SAIN/SAIN/Classes/Bot/SAINAILimit.cs) | `GetTickIntervalForTier`: Visible **30 Hz**, Audible **10 Hz**, Occluded **5 Hz** via `TickInterval` + `ShallTick()`. |
| Subsystem Hz knobs | [`OptimizedMod/SAIN/SAIN/Preset/GlobalSettings/Categories/General/PerformanceSettings.cs`](../OptimizedMod/SAIN/SAIN/Preset/GlobalSettings/Categories/General/PerformanceSettings.cs) | Vision / look / cover-find frequencies, raycast LOD, far-bot CPU reduction multipliers. |
| Human engagement ordering within tier | [`AIFrameBudgetScheduler.SortTierByCombatPriority`](../OptimizedMod/SAIN/SAIN/Components/AIFrameBudgetScheduler.cs) | Sorts by distance when **GoalEnemy is human** — **priority within tier**, not a skip exemption. |
| Squad vs solo jitter (related) | [`SquadCombatCoordinator`](../OptimizedMod/SAIN/SAIN/Layers/Combat/Squad/SquadCombatCoordinator.cs), [`CombatSquadLayer`](../OptimizedMod/SAIN/SAIN/Layers/Combat/Squad/CombatSquadLayer.cs) | Coordinator **does not** call `SetSquadDecision` when `CurrentCombatDecision != None`; squad layer **only** active when `CurrentCombatDecision == None` and squad decision set — reduces BigBrain priority fights and solo wipes from coordinator. |
| **Preset-driven AI frame budget** | [`PerformanceSettings.MaxAiBudgetMilliseconds`](../OptimizedMod/SAIN/SAIN/Preset/GlobalSettings/Categories/General/PerformanceSettings.cs), [`BotManagerComponent.SyncAiFrameBudgetFromPreset`](../OptimizedMod/SAIN/SAIN/Components/BotManagerComponent.cs) | Each `ManualUpdate`, preset value (clamp **1–10**) syncs to **`AIFrameBudgetScheduler.MaxAIBudgetMs`** (F12 readouts + CSV rows read the same scheduler instance via **SAINPerfLog**). |

### Gaps vs the stated goal (still open)

| Gap | Detail |
|-----|--------|
| **Interpretation: perf CSV vs “feel”** | Sampling **SAINPerfLog** per-raid perf CSV (e.g. every ~5 s) may show **low budget utilization** while subjective jitter persists — causes can include **BigBrain layer arbitration** (e.g. QuestingBots above SAIN combat), **whole-bot skips** between samples, or **vanilla/EFT** load — correlate with **`BudgetExhausted%`** and active brain layer before blaming ms cap alone. |
| **Whole-bot skips** | Under load, bots **may miss full `ManualUpdate`** frames. That conflicts with “player-visible behaves normal” unless visibility tier stays sparse or budget is large — subsystem LOD would be safer for fidelity. |
| **No hard guarantee for visible combatants** | Combat priority is **sort-only**; **no** rule like “always process Visible tier bots targeting player this frame” or “minimum ticks/sec.” |
| **Tier slice caps Visible work** | Visible tier stops increasing elapsed time at **~45%** of `MaxAIBudgetMs` before Audible runs — many Visible bots still **rotate** across frames. |
| **`Enemy` instance mixing in coordinator** | [`CollectAllVisibleEnemies`](../OptimizedMod/SAIN/SAIN/Layers/Combat/Squad/SquadCombatCoordinator.cs) dedupes by profile id; first member’s `Enemy` wins. `Enemy.IsVisible` is **per-owning-bot** — edge cases for suppress/heuristic branches if lists mix instances (noted in squad combat plan). |

---

## Implementation checklist (recommended order)

### Phase A — Observable tuning (low risk) — **done in repo**

1. ~~Add **`MaxAiBudgetMilliseconds`**~~ → implemented with **`[MinMax(1f, 10f, 0.5f)]`**, default **2** (`PerformanceSettings`).
2. ~~Sync scheduler~~ → **`SyncAiFrameBudgetFromPreset()`** in **`Activate`** and **`ManualUpdate`** (`BotManagerComponent`).
3. Verify with **`SainPerfLogInterop`** diagnostics ON (**SAINPerfLog → SAINPerfLog (F12) → Diagnostic Logging**): `BudgetExhaustedLastFrame`, skipped bot counts, exhaustion rate vs jitter (still recommended after preset tweaks).

### Phase B — Fidelity where it matters (medium risk)

4. **Avoid whole-bot starvation** for high-importance cases: e.g. always run **`ManualUpdate`** for bots matching **Visible + human goal enemy / active combat**, or enforce **minimum interval since last process** with a cheap fallback tick (design choice).
5. Optionally make **`VisibleTierBudgetFraction` / `AudibleTierCumulativeFraction`** configurable or dynamic — trade Audible/Occluded freshness vs Visible completeness.

### Phase C — Off-screen “still functional” (larger scope)

6. **Coarse objective layer** for Occluded/low tier: infrequent replan (loot cluster / waypoint / patrol leg), keep NavMesh goals valid without full combat stack — aligns with “walk toward loot, quest” under cap.
7. Keep extending **`PerformanceSettings`** / `ShallTick` patterns rather than skipping frames whenever possible for tiers that must **move believably**.

---

## Verification commands

```bash
dotnet build OptimizedMod/SAIN/SAIN.csproj -c Release
```

In-raid: compare **2 ms vs 4–5 ms** preset budget with same bot density; correlate jitter with diagnostic exhaustion/skips.

---

## Document history

- **2026-05-03**: Initial roadmap — consolidates AI budget jitter mitigation, Anomaly comparison, and player-visible vs off-screen LOD goals; code alignment rechecked against scheduler, `PerformanceSettings`, `BotManagerComponent`, `SAINAILimit`, and squad coordinator/layer guards.
- **2026-05-03 (pre-build pass)**: Phase A marked **implemented** (`MaxAiBudgetMilliseconds` + sync). Gap table updated for CSV interpretation vs BigBrain priority / QuestingBots; pending work remains Phases B–C and QuestingBots **`CanBotQuest` / priority** follow-up (see [INTEGRATION.md](INTEGRATION.md), [SPTQuestingBots.md](SPTQuestingBots.md)).
