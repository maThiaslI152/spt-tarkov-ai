# Agent guide — SPT Tarkov AI workspace

Short onboarding for **AI coding agents** (Cursor, CLI, etc.). The canonical map is **[INDEX.md](../INDEX.md)** at the repo root; this file only adds **workflow**, **read order**, and **guardrails**.

---

## Ground rules

| Rule | Detail |
|------|--------|
| **Source of truth** | All mod source lives under **`OptimizedMod/`**. Ignore empty legacy folders at repo root if present. |
| **SAIN shipping tree** | Editable SAIN code is under **`OptimizedMod/SAIN/SAIN/`** (inner project). The root **`OptimizedMod/SAIN/SAIN.csproj`** builds that tree. |
| **Do not treat `bin/` / `obj/` as docs** | Build outputs are not authoritative; prefer `.cs` sources. |
| **Harmony + BepInEx** | Client plugins use `[BepInPlugin]` and Harmony patches; some mods also ship **server** projects (`*ServerMod`, `ABPS/Server`, `MoreBotsAPI/Server`). |

---

## Suggested read order (by task)

| Goal | Read first | Then |
|------|------------|------|
| **Any new task** | [INDEX.md](../INDEX.md) — quick reference + file map | Topic doc from INDEX “Documentation Map” |
| **Cross-mod behavior / priorities** | [INTEGRATION.md](INTEGRATION.md) | [BIGBRAIN_LAYER_MATRIX.md](BIGBRAIN_LAYER_MATRIX.md), [STATUS_BIGBRAIN_AND_ROGUE.md](STATUS_BIGBRAIN_AND_ROGUE.md) |
| **SAIN internals (layers, ticks)** | [ARCHITECTURE.md](ARCHITECTURE.md) | `OptimizedMod/SAIN/SAIN/Plugin/BigBrainHandler.cs`, `BotComponent.cs` |
| **Fork default SAIN preset / first boot** | [SAIN_FORK_PRESET.md](SAIN_FORK_PRESET.md) — disk path, selection rules, **parameters → NPC behavior** | `OptimizedMod/SAIN/SAIN/SAINPlugin.cs` (`EnsureForkOptimizedPreset`, `ForkOptimizedPresetName`) |
| **Performance, budget, LOD** | [PERFORMANCE_ARCHITECTURE.md](PERFORMANCE_ARCHITECTURE.md) | [AI_BUDGET_LOD_PLAN.md](AI_BUDGET_LOD_PLAN.md) — *gaps vs goals, CSV interpretation* |
| **Raid CSV + F12 telemetry** | [SAIN_PERFLOG.md](SAIN_PERFLOG.md) | `OptimizedMod/SAINPerfLog/`, `OptimizedMod/SAIN/SAIN/Interop/SainPerfLogInterop.cs` |
| **Blind AI + stutter (vision job vs layers)** | [VISION_BLINDNESS_AND_STUTTER.md](VISION_BLINDNESS_AND_STUTTER.md) | `VisionRaycastJob.cs`, `LootingLayer.cs`, `SAINLayer.cs`, `AIFrameBudgetScheduler.cs` |
| **Offline squads / distant combat slice** | [SMART_OFFLINE_COMBAT.md](SMART_OFFLINE_COMBAT.md) | `OfflineSquadWorldSync.cs`, `OfflineCombatResolver.cs`, `CombatAudioSpoofer.cs` |
| **Frozen bots / AILimit + SAIN deadlock** | [BUGFIX-AILimitSAIN-Deadlock.md](BUGFIX-AILimitSAIN-Deadlock.md) | [SAIN_AILIMIT_DEMATERIALIZATION.md](SAIN_AILIMIT_DEMATERIALIZATION.md) (full inventory), then `AIFrameBudgetScheduler.cs` (`RecheckActivation`), `BotDematerializationController.cs`, `OfflineSquadMaterialization.cs`, `AILimit/Component.cs` |
| **Full SMART demat/remat + `auto_*` materialize (Phase 1+2)** | [INDEX.md](../INDEX.md) → [.cursor/plans/smart_demat_remat_65de6b98.plan.md](../.cursor/plans/smart_demat_remat_65de6b98.plan.md) (conflict matrix + **R1–R7 resolutions**) | After [SMART_OFFLINE_COMBAT.md](SMART_OFFLINE_COMBAT.md); implement gate, AILimit arbitration, pool idempotency, ABPS cap defer, `TryGetFromPool` wiring |
| **Rogue Lighthouse behavior** | [STATUS_BIGBRAIN_AND_ROGUE.md](STATUS_BIGBRAIN_AND_ROGUE.md) | [ROGUE_BASE_DEFENSE_PLAN.md](ROGUE_BASE_DEFENSE_PLAN.md) — coordinator + squad-layer bootstrap (`ShouldBootstrapRogueDefenseCombatLayer`) |
| **What shipped vs pending** | [PROGRESS.md](PROGRESS.md) | [PERFORMANCE_PLAN.md](PERFORMANCE_PLAN.md) |

**Bugfix deep dives** (narrow regressions): `BUGFIX-*.md` in `docs/` — linked from INDEX.

---

## Build commands (verified layout)

```bash
# Primary SAIN client DLL (most AI changes)
dotnet build OptimizedMod/SAIN/SAIN.csproj -c Release

# AILimit (references SAIN for dematerialization types)
dotnet build OptimizedMod/AILimit/AILimit.csproj -c Release

# Standalone perf logger (telemetry / F12 toggles)
dotnet build OptimizedMod/SAINPerfLog/SAINPerfLog.csproj -c Release

# Reference library (patterns may be mirrored inside SAIN; still build when touching shared types)
dotnet build OptimizedMod/OptimizationCore/OptimizationCore.csproj -c Release
```

Other mods: each has a `.csproj` or `.sln` under `OptimizedMod/<Mod>/` (BigBrain, Waypoints, AILimit, LootingBots, ABPS, MoreBotsAPI).

**DLL → folder map (install layout):** [MOD_BUILD_AND_DEPLOY.md](MOD_BUILD_AND_DEPLOY.md) — output names, `BepInEx/plugins` vs `SPT/user/mods`, server staging folders. **`SPTQuestingBots/`** is documented there only if you build QB separately; this repo keeps it for **study / behavior reference**, not as part of the `OptimizedMod/` Release stack.

---

## High-signal code entry points

| Concern | Path |
|---------|------|
| SAIN plugin boot | `OptimizedMod/SAIN/SAIN/SAINPlugin.cs` |
| BigBrain layer registration / vanilla strips | `OptimizedMod/SAIN/SAIN/Plugin/BigBrainHandler.cs` |
| Per-frame bot scheduling | `OptimizedMod/SAIN/SAIN/Components/BotManagerComponent.cs`, `AIFrameBudgetScheduler.cs` |
| Perception tier + tick gating | `OptimizedMod/SAIN/SAIN/Classes/Bot/SAINAILimit.cs` |
| World tick injection | `OptimizedMod/SAIN/SAIN/Components/GameWorldComponent.cs`, `Patches/GameWorld/WorldTickPatch.cs` |
| QuestingBots / combat gating interop | `OptimizedMod/SAIN/SAIN/Interop/SAINExternal.cs`; optional read-only QB clone for patterns: [`SPTQuestingBots/`](../SPTQuestingBots/) ([SPTQuestingBots.md](SPTQuestingBots.md)) |
| SAIN squad vs solo **decision** order + BigBrain layer ranks | [SAIN_DECISION_AND_LAYER_RANKING.md](SAIN_DECISION_AND_LAYER_RANKING.md), `BotDecisionManager.cs`, `CombatSquadLayer.cs`, `CombatSoloLayer.cs` |
| Perf log reflection gate | `OptimizedMod/SAIN/SAIN/Interop/SainPerfLogInterop.cs` |

---

## Telemetry paths (runtime)

| Artifact | Typical location |
|----------|------------------|
| Per-raid perf CSV | `BepInEx/LogOutput/sain_perf/` — `sain_perf_*.csv` includes spawn vs tick columns; optional `sain_spawn_events_*.csv` (F12 **Spawn Event Log**); see [SAIN_PERFLOG.md](SAIN_PERFLOG.md) |
| Verbose diagnostics | `BepInEx/LogOutput/LogOutput.log` — enable only via **SAINPerfLog** F12 diagnostic toggle when chasing `[SAIN DIAG]` |

---

## When updating documentation

If you add a feature or change integration:

1. Update **[INDEX.md](../INDEX.md)** “Documentation Map” and/or “Repository Map” if paths or behavior changed.
2. Update **[PROGRESS.md](PROGRESS.md)** for shipped vs open items when appropriate.
3. Prefer **one canonical doc per topic**; use short bugfix/plan docs for narrow PRs and link them from INDEX.
