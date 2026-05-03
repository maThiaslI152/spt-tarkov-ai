# Status — BigBrain Arbitration, Diagnostics, and Rogue Scope

> Last updated: 2026-05-05  
> Audience: operators and contributors debugging “wrong layer”, passive combat, or Rogue vs Scav confusion.

---

## Problems we were solving

1. **BigBrain arbitration** — Multiple mods and vanilla EFT layers compete by **numeric priority** and `IsActive()`. Wrong winner → bots quest/loot/patrol while SAIN combat should drive, or jitter when layers flip.
2. **Hard to observe** — Perf CSV can look healthy while behavior is wrong; need **log lines** tied to active layer + threat state.
3. **Vanilla posture layers** — Names like **`StationaryWS`** / patrol-style layers could stay active if not in SAIN’s remove list, blocking SAIN combat layers for **any** brain that still had those vanilla layers registered.
4. **Concept mix-up** — **Rogue base defense** (ExUsec squad policy) vs **global BigBrain strip lists** (shared across Scav, Rogue, PMC, …) are different things; documenting both avoids “did we turn Rogues on for Scavs?” confusion.

---

## Features and documentation shipped

| Item | What it is |
|------|------------|
| **[BIGBRAIN_LAYER_MATRIX.md](BIGBRAIN_LAYER_MATRIX.md)** | Single reference: custom layer registration, priorities, vanilla strip matrix, repro checklist, gap log table for new layer names from logs. |
| **Expanded `[SAIN DIAG][BigBrain]`** | Requires **SAINPerfLog** + **Diagnostic Logging**. Logs `brain=`, `reason=`, `pressure=`, `SAINActiveLayer=`, active layer string, goal, combat/squad decisions. Not gated on QuestingBots alone. |
| **F12 → `3. BigBrain verbose sample`** | Optional **Info** lines for every human-proximate bot on the diag interval (see **SAINPerfLog** F12 category). |
| **`SAINExternal.IsBotUnderCombatPressure(BotOwner)`** | Public predicate aligned with quest-blocking combat checks (under-fire, QB signals when loaded, goal-enemy windows). |
| **Extra vanilla strips** | `_commonVanillaLayersToRemove` extended (e.g. `StationaryWS`, `StationaryWeapon`, `PatrolAssault`, `SimplePatrol`) — **build-dependent** exact names; extend via gap log when logs show misses. |
| **[INTEGRATION.md](INTEGRATION.md)** | States that **`BigBrainHandler.Init()` runs only at game start** — changing layer priorities / vanilla toggles needs a **full restart** to re-register. |
| **[ROGUE_BASE_DEFENSE_PLAN.md](ROGUE_BASE_DEFENSE_PLAN.md)** | **Posture vs BigBrain layers** section: Rogue-only **behavior** policy vs **global** strip list that also affects Scav brains. |

---

## Rogue-only vs global (read this once)

| Topic | Rogues (`ExUsec`) | Scavs |
|--------|-------------------|--------|
| **Rogue base defense** (squad leader, defense loop, no-loot on base, Lighthouse scope) | **Yes** — when settings + spawn context match. | **No** — out of scope for that feature; regression target is unchanged Scav behavior. |
| **Vanilla layer strip** (`BigBrainHandler` shared list for brains that use it) | **Yes** — same list as other factions using that path. | **Yes** — Scav brains use the shared removals too; that is **not** Rogue-specific policy. |
| **“Stationary / Patrol / Standby” in Rogue plan** | **Behavioral posture** for defense coordination. | **Not** “apply Rogue defense to Scavs.” |

---

## Build / deploy

- `dotnet build OptimizedMod/SAIN/SAIN.csproj -c Release`
- `dotnet build OptimizedMod/SAINPerfLog/SAINPerfLog.csproj -c Release` (diagnostics toggle + verbose sample live here)

---

## Open follow-ups (runtime)

- Confirm in-raid that `[SAIN DIAG][BigBrain]` matches expectations on Lighthouse / with QuestingBots.
- Fill **gap log** rows in [BIGBRAIN_LAYER_MATRIX.md](BIGBRAIN_LAYER_MATRIX.md) when logs show a vanilla layer name that is still winning arbitration.
- Optional future work: safe **combat-layer hysteresis** only if `GetNextAction` can stay correct when `CurrentCombatDecision` is `None` (not implemented — see prior audit notes).

---

## Related index entries

- [BUGFIX-BigBrainPriority-QuestingBots.md](BUGFIX-BigBrainPriority-QuestingBots.md) — QB-focused history + diagnostic steps.  
- [SAIN_PERFLOG.md](SAIN_PERFLOG.md) — **canonical** shipped summary: plugin GUID, deploy, F12 categories, interop contract, build.  
- [SAIN_PERFLOG_STANDALONE_PLAN.md](SAIN_PERFLOG_STANDALONE_PLAN.md) — original plan + mermaid (design history).  
- [PROGRESS.md](PROGRESS.md) — checklist and session log.
