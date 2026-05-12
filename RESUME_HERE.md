---
purpose: Resume point
created: 2026-05-12T05:56Z (UTC+7)
from_conversation: "Project progress inquiry → documented summary for resume"
branch: main
commit_head: 3fda695
---

# Resume Point: May 12, 2026

## Conversation Summary

**Asked:** What is the current overall progress of the project?
**Answered:** SPT Tarkov AI Performance Overhaul — ~86% complete (12/14 major tasks done). All 4 optimization phases implemented. Core remaining gap: runtime validation on Windows SPT.

---

## Current Git State

**Branch:** `main`
**HEAD:** `3fda695` — "docs: document SMART Phase 1+2 shipped components, PMC No-Combat findings, and spawn-event telemetry"

### Modified files (unstaged, pending commit)

| File | Status |
|---|---|
| `INDEX.md` | Modified (staged) |
| `OptimizedMod/SAIN/SAIN/Components/BotManagerComponent.cs` | Modified |
| `docs/PROGRESS.md` | Modified |
| 3 other files (REDACTED paths) | Modified |

### Recent commits (top 5)

| Commit | Message |
|---|---|
| `3fda695` | docs: document SMART Phase 1+2 shipped components, PMC No-Combat findings, and spawn-event telemetry |
| `322d5f4` | feat: smart demat/remat, spawn-perf telemetry, bot pool counters, multi-map triage docs |
| `86af707` | Vision job buffer + schema 8 telemetry, preset distance knobs, stack docs |
| `a9f4154` | feat: SAINPerfLog, offline squad slice, and agent docs |
| `5fe059d` | feat: Rogue ExUsec base-defense squad coordination |

---

## Latest Work (last session: May 6)

SMART Phase 1+2 was the most recent deliverable — the dematerialization/rematerialization system:

- **Smart demat/remat lifecycle** (`Components/SmartDematSystems.cs`) — LOS + hearing gates for AILimit-parked bots, `DematParkReason` arbitration
- **Auto materialization** (`Components/AutoSquadMaterialization.cs`) — cap-gated remat from offline combat results
- **BotSpawnPoolBridge** — coordinates spawn waves with pool rematerialization
- **Spawn-event telemetry** (`Components/SpawnEventCsvLogger.cs`) — per-event CSV with AutoMat tagging
- **PMC No-Combat investigation** — paused; pattern observed (PMCs rarely enter SAIN combat layers despite non-zero vision work) but full-raid playtest cycles too slow
- **BigBrain schema v8** — `VisionRayEffective*` counters added for diagnostics
- **Multi-map triage** — Lighthouse/Customs/Interchange/Reserve/Factory vision pipeline analysis

---

## Architecture Quick Reference

### AI processing pipeline

```
BudgetScheduler.ProcessFrame(allBots) — 2ms HARD CAP
  Phase 0: ResolveOfflineSquadCombat()  [≤1 Hz, statistical]
  Visible tier:   process until ~45% budget
  Audible tier:   process until ~88% cumulative
  Occluded tier:  remainder budget

PerceptionSystem determines tier:
  Visible:  camera frustum + 1 raycast (cached 0.5s)
  Audible:  gunfire within 500m OR sprinting within 60m (cached 1.0s)
  Occluded: everything else
```

### Mod stack (layered)

| Layer | Mods | Role |
|---|---|---|
| 4 — Performance | OptimizationCore, SAINPerfLog | Budget, LOD, telemetry |
| 3 — Behavior | SAIN (combat), LootingBots (looting) | Bot actions |
| 2 — Framework | BigBrain | Layer/action system |
| 1 — Infra | Waypoints, AILimit, ABPS, MoreBotsAPI | NavMesh, deactivation, spawn |
| 0 — Engine | SPT Core / EFT | BotOwner, GameWorld, etc. |

---

## Where to Resume

### Most impactful next work (ranked)

1. **Runtime profiling baseline** — requires Windows SPT. This is the single bottleneck blocking everything else (pool verification, integration test, PMC no-combat diagnosis). Without this, all remaining items are untested.
2. **CombatAudioSpoofer BetterAudio wiring** — the audio spoofer logic is coded but needs version-correct EFT/BetterAudio API binding and a live raid test.
3. **Phase 4 pool live verification** — `BotGameObjectPool` Harmony intercepts on `GameObject.Destroy` need in-game validation.
4. **PMC combat layer debugging** — if playtestable, resume the full-raid investigation into why PMCs rarely enter SAIN combat layers.

### Key docs to re-read first

| Priority | Doc | Purpose |
|---|---|---|
| 1 | `INDEX.md` | Root hub — repo map, conventions, documentation map |
| 2 | `docs/AGENTS.md` | Agent read order, build commands, hot paths |
| 3 | `docs/PERFORMANCE_PLAN.md` | Phased execution plan with status |
| 4 | `docs/PROGRESS.md` | Session log — read from bottom for latest state |
| 5 | `docs/ARCHITECTURE.md` | Per-mod internals deep-dive (~1100 lines) |
| 6 | `docs/MOD_STACK.md` | Dependency graph, data flow, init order |
| 7 | `docs/SAIN_AILIMIT_DEMATERIALIZATION.md` | Full demat/remat change record |

### Key source files to open

| File | Why |
|---|---|
| `OptimizedMod/SAIN/SAIN/Components/BotManagerComponent.cs` | Main lifecycle manager — wires scheduler, demat/remat, diagnostics |
| `OptimizedMod/SAIN/SAIN/Components/SmartDematSystems.cs` | SMART demat/remat core |
| `OptimizedMod/SAIN/SAIN/Components/BotGameObjectPool.cs` | Phase 4 bot pool |
| `OptimizedMod/SAIN/SAIN/Components/AIFrameBudgetScheduler.cs` | 2ms hard cap scheduler |
| `OptimizedMod/SAIN/SAIN/Components/CombatAudioSpoofer.cs` | Pending BetterAudio wiring |
| `OptimizedMod/OptimizationCore/` | Shared performance library (8 files) |

---

## Agent Resume Instructions

Copy the following block to restore context quickly:

```
You are resuming work on the SPT Tarkov AI Performance Overhaul project at E:\spt-tarkov-ai.
This is a C# (.NET Standard 2.1) modding project for Single Player Tarkov (SPT).

RESUME POINT: May 12, 2026 — ~86% complete, 12/14 tasks done.

CURRENT STATE:
- Branch: main (HEAD at 3fda695)
- All phases 1-4 implemented, SMART Phase 1+2 shipped
- 9 client mods compile with 0 errors
- Files with unstaged changes: INDEX.md, BotManagerComponent.cs, PROGRESS.md + 3 others
- The single critical block: runtime profiling needs Windows SPT

MOST RECENT WORK: SMART demat/remat lifecycle, auto materialization, spawn-event CSV telemetry, PMC No-Combat investigation (paused).

TO CONTEXTUALIZE:
1. Read RESUME_HERE.md fully
2. Read INDEX.md for repo map
3. Read docs/PROGRESS.md from bottom up for latest session log
4. Read docs/AGENTS.md for onboarding
5. Read docs/OPTIMIZED_MOD_README.md for build/deploy commands
6. If debugging vision or runtime issues, read docs/VISION_BLINDNESS_AND_STUTTER.md
7. If working on demat/remat, read docs/SAIN_AILIMIT_DEMATERIALIZATION.md

DO NOT re-derive architecture from source files — the docs exist and are authoritative.
DO NOT modify files without user instruction.
```
