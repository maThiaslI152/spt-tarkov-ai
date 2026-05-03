# SAINPerfLog — standalone raid telemetry (implemented)

> **Last updated:** 2026-05-03  
> **Status:** Shipped in-repo under `OptimizedMod/SAINPerfLog/`. This document is the **canonical summary** of what was built and how it relates to **SAIN**. For original design rationale and migration notes, see [SAIN_PERFLOG_STANDALONE_PLAN.md](SAIN_PERFLOG_STANDALONE_PLAN.md).

**Agents:** [INDEX.md](../INDEX.md) · [AGENTS.md](AGENTS.md) (builds + read order).

---

## Summary

**SAINPerfLog** is a **separate BepInEx plugin** from **SAIN**. It owns:

| Concern | Owner |
|--------|--------|
| Per-raid performance CSV (timestamped files, no cross-raid overwrite) | **SAINPerfLog** (`RaidPerfCsvLogger`) |
| Optional sparse BigBrain aggregate snapshot CSV | **SAINPerfLog** |
| **F12** read-only lines (FPS rolling average, `AIFrameBudgetScheduler` stats, active CSV paths) | **SAINPerfLog** (`PerfLogPlugin`, category **`SAINPerfLog (F12)`**) |
| **Diagnostic logging** toggle for spammy `[SAIN DIAG]` traces | **SAINPerfLog** (same F12 category) |
| `AIFrameBudgetScheduler`, perception tiers, bot ticks | **SAIN** (unchanged responsibility) |
| Gating diagnostic spam inside SAIN when the perf plugin is present | **SAIN** via [`SainPerfLogInterop`](../OptimizedMod/SAIN/SAIN/Interop/SainPerfLogInterop.cs) |

**SAIN** no longer ships a **“SAIN Performance”** BepInEx config section, `SAINPerformanceMonitor`, or in-SAIN CSV ownership.

---

## Plugin identity

| | |
|---|---|
| **Project** | `OptimizedMod/SAINPerfLog/SAINPerfLog.csproj` |
| **Assembly** | `SAINPerfLog.dll` |
| **BepInPlugin GUID** | `me.sol.sain.perflog` |
| **SAIN dependency** | `[BepInDependency("me.sol.sain", SoftDependency)]` — logger can load after SAIN; raid hook reads live scheduler state |

---

## Deploy layout (typical SPT client)

Install **both** DLLs for full behavior:

- `BepInEx/plugins/SAIN/SAIN.dll` (or your pack layout)
- `BepInEx/plugins/SAINPerfLog/SAINPerfLog.dll`

You may update **`SAINPerfLog.dll` alone** when only telemetry/F12 changes, as long as the **interop contract** below stays compatible.

---

## BepInEx configuration

| Category | Purpose |
|----------|---------|
| **SAINPerfLog** | Auto logging on/off, perf CSV interval, BigBrain snapshot options, output directory relative to BepInEx root, optional `*_latest.csv` aliases |
| **SAINPerfLog (F12)** | **F12 Status Lines** (read-only refresh), **Diagnostic Logging** (spam to `LogOutput.log`), read-only FPS / budget / bot / active path lines |

Open with **F12 → Configuration Manager → SAINPerfLog** (plugin list).

---

## Output files

Default directory (configurable): `BepInEx/LogOutput/sain_perf/` (relative to BepInEx root unless overridden).

| Artifact | Pattern / notes |
|----------|-----------------|
| Per-raid perf CSV | `sain_perf_{UtcTimestamp}_{LocationId}_{SessionToken}.csv` |
| Per-raid BigBrain snapshot CSV | `sain_bigbrain_{UtcTimestamp}_{LocationId}_{SessionToken}.csv` (when snapshots enabled) |
| Optional aliases | `sain_perf_latest.csv`, `sain_bigbrain_latest.csv` when **Write latest aliases** is enabled |

Writers attach on raid world creation and **close on `GameWorld.OnDispose`** (hideout skipped; Fika client path skipped when applicable).

---

## SAIN ↔ SAINPerfLog contract (interop)

SAIN must **not** reference `SAINPerfLog.dll` (avoids circular project references). Instead:

1. **SAINPerfLog** exposes a **public static bool** field on the plugin type: **`DiagnosticLoggingEnabled`**, updated every frame from the BepInEx config entry **Diagnostic Logging** (`SAINPerfLog (F12)`).
2. **SAIN** reads it through **`SainPerfLogInterop.IsDiagnosticLoggingEnabled`**, which resolves plugin `me.sol.sain.perflog` via `Chainloader.PluginInfos` and reflects that field by name.

**If you rename or remove that field**, diagnostic gating in SAIN falls back to **off** until SAIN’s interop is updated.

Call sites (representative): `AIFrameBudgetScheduler`, `SAINAILimit`, `BotManagerComponent`, `SquadCombatCoordinator`.

---

## Raid lifecycle hook

Harmony postfix on **`GameWorldUnityTickListener.Create`** (type resolved with `AccessTools.TypeByName` for visibility across references). Adds or reuses **`RaidPerfCsvLogger`** on the tick listener `GameObject` and calls `Initialize(gameWorld)`.

Load order: `[HarmonyAfter("me.sol.sain")]` on the patch class so SAIN’s world/bot wiring exists before sampling.

---

## Build

From repo root (also rebuilds **SAIN** because the project references it):

```bash
dotnet build OptimizedMod/SAINPerfLog/SAINPerfLog.csproj -c Release
```

Artifacts:

- `OptimizedMod/SAIN/SAIN/bin/Release/netstandard2.1/SAIN.dll`
- `OptimizedMod/SAINPerfLog/bin/Release/netstandard2.1/SAINPerfLog.dll`

(post-build copy targets depend on your `SPTRoot` / csproj props.)

---

## Migration from legacy behavior

Older in-SAIN flows used a single **`BepInEx/LogOutput/sain_perf.csv`** path with overwrite/truncation risk across raids. **Do not** expect that fixed path anymore. Use **`LogOutput/sain_perf/`** timestamped files (and optional latest aliases).

---

## Related documentation

| Doc | Role |
|-----|------|
| [INDEX.md](../INDEX.md) | Workspace entry; “Fix a performance issue” task lists F12 location |
| [SAIN_PERFLOG_STANDALONE_PLAN.md](SAIN_PERFLOG_STANDALONE_PLAN.md) | Original plan, mermaid, validation checklist |
| [PROGRESS.md](PROGRESS.md) | Session history + checklist row for perf logging |
| [PERFORMANCE_ARCHITECTURE.md](PERFORMANCE_ARCHITECTURE.md) | Budget scheduler + logging section (SAINPerfLog-oriented) |
| [OPTIMIZED_MOD_README.md](OPTIMIZED_MOD_README.md) | Quick start, F12 section |
| [STATUS_BIGBRAIN_AND_ROGUE.md](STATUS_BIGBRAIN_AND_ROGUE.md) | BigBrain diagnostics vs **SAINPerfLog** toggles (onboarding) |
