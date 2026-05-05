# STATUS — PMC-scale bots rarely enter SAIN combat layers (investigation paused)

> **Date:** 2026-05-05  
> **Status:** **Documented / paused** — full-raid playtest cycles (menu → load → raid → extract/end) are too slow for tight iteration right now. Resume when ready to spend time in-raid or add **lighter** diagnostics (logging-only bursts, Factory smoke, automated asserts on CSV).  
> **Related:** [BUGFIX-MultiMap-GoalHumanFinalVisibility-And-Arbitration.md](BUGFIX-MultiMap-GoalHumanFinalVisibility-And-Arbitration.md), [BUGFIX-AILimitSAIN-Deadlock.md](BUGFIX-AILimitSAIN-Deadlock.md), [SAIN_DECISION_AND_LAYER_RANKING.md](SAIN_DECISION_AND_LAYER_RANKING.md), [SAIN_PERFLOG.md](SAIN_PERFLOG.md)

---

## Symptom (player-facing)

On **large maps** (e.g. Customs `bigmap`), **typical PMC / assault-style bots** often behave as if they are **not in SAIN combat**: little or no SAIN-style combat layers, weak or absent sustained player engagement, **Looting** and patrol-type layers dominating telemetry.

**Contrast:** **Bloodhounds** (and similar **boss / event** spawns) can **spawn and engage normally** — they use different brains / objectives and are **not** a proof that the PMC `BotDecisionManager` → SAIN combat layer path is healthy.

---

## Latest evidence session (2026-05-04, Customs)

| Field | Value |
| --- | --- |
| **SessionId** | `edd84743` |
| **LocationId** | `bigmap` (Customs) |
| **Artifacts** | `BepInEx/LogOutput/sain_perf/sain_bigbrain_20260504_215233_unknown_edd84743.csv`, `sain_perf_20260504_215233_unknown_edd84743.csv`, `sain_spawn_events_20260504_215233_unknown_edd84743.csv` |
| **Log** | `BepInEx/LogOutput.log` — Bloodhounds / Goons spawn (`BossNotifier`), bundle `ttc_common_bloodhound_merc_tracker` |

### BigBrain snapshot (schema 8) — recurring pattern

- **`LayerHistogram`:** dominated by **`Looting`**, **`PtrlBirdEye`**, **`Pmc`**, etc. **No** SAIN-named combat layers in the histogram for sampled intervals.
- **`SignalGoalEnemy`:** **0**; **`SignalCombatNonNone`:** **0**; **`GoalHumanCount`:** **0** across those rows.
- **`SignalPressure`:** sometimes **1** with **`MismatchCombatSignals`** — exemplars include **`combatPressureNonSainCombat`** and **`thirdPartyOrVanilla`**, e.g. bots on **`arenaFighterEvent`** / **`Pmc`** or **`Looting`** while pressure signals fire. That reads as **vanilla or non–SAIN-combat classification** under stress, not “SAIN combat layer active.”
- **Scheduler pairing:** `SainBotsTotal` and `SainBotsSampled` stayed **aligned** (e.g. 17/17); perf CSV showed **`SkippedBots = 0`** in the same window — **not** the classic AILimit latch signature (`SainBotsSampled` ≪ `SainBotsTotal`).

### BotMind A/B (same era)

`com.blackhorse311.botmind.cfg` had **`Enable Questing = false`**, **`Enable MedicBuddy = false`**, **`PMCs Do Quests = false`**. Telemetry **no longer** showed **`BotMind_Questing`** in `LayerHistogram` — so **questing was successfully removed as the dominant layer**; the **PMC combat gap remained**.

> **Note:** BepInEx startup log may still print a **LootingBots** interaction line about “Questing modules remain active” — that string is **not** a guaranteed read of live config; trust **`LayerHistogram`** + cfg file.

---

## What is tentatively ruled out (for this symptom on `edd84743`)

| Hypothesis | Verdict |
| --- | --- |
| **BotMind questing stealing the brain** | **Weakened** — no `BotMind_Questing` in layer histogram with questing disabled. |
| **Primary AILimit ↔ SAIN scheduler deadlock** (sampled bots ≪ total) | **Weakened for this session** — sampled equals total; skips not showing the old pattern. |
| **“No vision work at all”** | **Weakened** — `VisionRayAttempt*` / `VisionRayEffective*` are non-trivial; the remaining gap is **classification / goal / combat layer**, not “rays never run.” |

---

## What still fits the data (resume here)

1. **`BotDecisionManager` / `ChooseEnemy` / `CurrentCombatDecision`** path never yields a state that activates **SAIN combat layers** for many PMCs (see prior analysis: `CombatSoloLayer` expectations vs **`GoalEnemy`** / decision `None`).
2. **Layer arbitration** — **Looting** (and friends) still wins or holds bots that should flip under sustained pressure (Phase **F** in the optimization plan).
3. **`GoalHuman*` finalization vs human-as-enemy** — overlaps [BUGFIX-MultiMap-GoalHumanFinalVisibility-And-Arbitration.md](BUGFIX-MultiMap-GoalHumanFinalVisibility-And-Arbitration.md); same-frame handoff shipped but **PMC engagement** problem **persisted** in this Customs run.

---

## Secondary log finding (optional cleanup)

- **LootingBots** logged **`NullReferenceException`** while a bot (`Bot22`) tried to loot — separate ticket; could disturb individual bot state but does not by itself explain map-wide combat classification.

---

## Faster iteration ideas (when work resumes)

- Prefer **short raids** or a **control map** (e.g. Factory) for binary checks, then one Customs confirm.
- Add **targeted logging** (single bot profile, `ChooseEnemy` null reason, squad order flags) so one raid answers a **narrow** question without needing multiple full cycles.
- Parse **`sain_bigbrain_latest.csv`** + session id from log to avoid hunting filenames.

---

## Open verification

- After any code change: compare **`LayerHistogram`**, **`SignalCombatNonNone`**, **`GoalHumanCount`**, and mismatch **`MismatchReasonHistogram`** on **Customs** vs **Factory** for the same build.
