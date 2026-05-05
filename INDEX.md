# Workspace Index — SPT Tarkov AI Optimization

> **Purpose:** Entry point for AI coding agents working on this workspace. Read this first to
> understand what exists, where things are, and which document to open for a given task.

**Companion:** [docs/AGENTS.md](docs/AGENTS.md) — read order by task, build commands, high-signal
code paths, telemetry locations, and doc-update checklist.

---

## Quick Reference


| Question                                      | Answer                                                                                                                                                                                                                                                                                                       |
| --------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **What is this workspace?**                   | Optimization of SPT Tarkov AI mods. All source code lives in `OptimizedMod/` (6 forked mods: BigBrain, SAIN, LootingBots, Waypoints, AILimit, ABPS + MoreBotsAPI + new OptimizationCore library).                                                                                                            |
| **What is OptimizedMod?**                     | Performance-optimized forks of 6 mods + MoreBotsAPI + new OptimizationCore library (budget scheduler, perception LOD, offline combat). **SMART slice:** auto `OfflineSquad` registration + statistical combat + procedural distant audio + **SMART `demat_*`** (distance/LOS) + **`auto_*` first-engagement casualty reconcile** — see [docs/SMART_OFFLINE_COMBAT.md](docs/SMART_OFFLINE_COMBAT.md). |
| **Language**                                  | C# (.NET Framework), BepInEx plugin, Harmony patching, Unity Engine, SPT server DI                                                                                                                                                                                                                           |
| **Core dependency**                           | **BigBrain** for behavior mods (SAIN, LootingBots). **Waypoints** for pathfinding. Others are standalone.                                                                                                                                                                                                    |
| **Entry point**                               | Client mods: BepInEx `[BepInPlugin]`. Server mods: SPT DI `[Injectable]`.                                                                                                                                                                                                                                    |
| **Raid perf CSV + F12 scheduler/diagnostics** | **SAINPerfLog** (`me.sol.sain.perflog`) — separate DLL from **SAIN**; perf rows attribute **non-SAIN frame cost** vs scheduler (`NonSainFrameMs`, spawn/despawn/pool/GC deltas); optional **`sain_spawn_events_*.csv`** (F12); see [docs/SAIN_PERFLOG.md](docs/SAIN_PERFLOG.md)                                                                                                                                                                                       |
| **SAIN `VisionRaycastJob` (batched LOS / vision / shoot)** | **Playtest:** fixes **AI blindness** and **hearing-only** fights. **Telemetry:** `VisionRayTarget*` = strict first-hit body collider (often **0** indoors with high `Blocked*`); **`VisionRayEffective*`** (schema **8+**) matches gameplay success (`null` OR target); **`GoalHumanSainParts*`** = end-to-end visibility. Distance: preset **`VisionSinglePartBeyondDistanceMeters`** + optional **`VisionUseFullPartsForHumanBeyondDistance`**. See [docs/VISION_BLINDNESS_AND_STUTTER.md](docs/VISION_BLINDNESS_AND_STUTTER.md), `OptimizedMod/SAIN/SAIN/Classes/BotManager/Jobs/VisionRaycastJob.cs`. |
| **Fork customized SAIN preset (where / what)** | This repo’s **SAIN** fork bootstraps a custom preset **`Optimized (Harder PMCs)`** on first run (player-centric LOD tuning). It lives next to `SAIN.dll`: `BepInEx/plugins/SAIN/Presets/Optimized (Harder PMCs)/`. **Vanilla SAIN or an unpacked fork DLL that was never rebuilt from this tree will not create that folder.** See [docs/SAIN_FORK_PRESET.md](docs/SAIN_FORK_PRESET.md). |
| **`SPTQuestingBots/` folder** | **Reference only** for this project: study QuestingBots architecture and **copy behavior** into `OptimizedMod/` where useful. **Not** part of the shipped optimized stack; no expectation it is built or deployed with `OptimizedMod/`. See [docs/SPTQuestingBots.md](docs/SPTQuestingBots.md). |
| **SMART Phase 1+2 (demat / offline / remat)** | **Shipped (v1) in SAIN:** `SmartDematerializeGate`, LOS + hearing `demat_*` remat, `DematParkReason` arbitration with AILimit, pool idempotency + Destroy prefix, `AutoSquadMaterialization` (live-bot kills from first auto-vs-auto roll when player nears; cap-gated), perf CSV **`AutoMatAppliedDelta`**, optional spawn-event **`AutoMat`**. Execution plan (reference): [.cursor/plans/smart_demat_remat_65de6b98.plan.md](.cursor/plans/smart_demat_remat_65de6b98.plan.md). Narrative: [docs/SAIN_AILIMIT_DEMATERIALIZATION.md](docs/SAIN_AILIMIT_DEMATERIALIZATION.md). |
| **Materialize / dematerialize / AI pool (runtime seam)** | **Dematerialize:** AILimit → `BotDematerializationController.RequestDematerialize` → `BotGameObjectPool.ReturnToPool` (parked bot). **Rematerialize / mat:** `SmartDematSystems` (LOS + hearing gates) → proximity + squad logic → `OfflineSquadMaterialization` / `AutoSquadMaterialization` (cap-gated) / pool dequeue → GameObject **Activate**. **Bridge:** `BotSpawnPoolBridge` coordinates spawn waves with pool remat. **Pool:** `BotGameObjectPool` (idempotent return, Destroy-prefix safety, `ActivePooledCount` / hit/miss telemetry). **Roadmap:** Phase **G** caps per-frame activation spikes (`BotSpawnController`, pool remat, `NonSainFrameMs` / spawn CSV columns). See [.cursor/plans/sain-optimization-fix-plan_81542674.plan.md](.cursor/plans/sain-optimization-fix-plan_81542674.plan.md) and [docs/SAIN_AILIMIT_DEMATERIALIZATION.md](docs/SAIN_AILIMIT_DEMATERIALIZATION.md). |
| **SAIN optimization plan (2026-05 — active D→H)** | **Agreed scope:** **1B** SAIN + interop (balanced triage). **Phase D** — `GoalHumanFinal*` visibility handoff (`EnemyVisionClass`, part decay, `EnemyInfo`); same-frame `FinalizeVisionHandoffFromRayBatch` shipped 2026-05-06. **Phase E** — schedulable budget **domains** beyond the 2 ms `BotComponent.ManualUpdate` cap. **Phase F** — combat-pressure interop (LootingBots / `SAINExternal` seams). **Phase G** — spawn / **pool remat** smoothing (`BotSpawnController`, staggered activation); telemetry columns (`NonSainFrameMs`, spawn/despawn/pool/GC deltas) and `SpawnEventCsvLogger` shipped. **Phase H** — long-term unified multi-mod orchestrator RFC. **Demat/remat compat:** D–H must preserve the SMART + AILimit contract (no bypassing `BotDematerializationController`, pool idempotency, `SmartDematerializeGate` / remat ordering, or `BotSpawnController.IsWildSpawnStrictlyExcluded` semantics) — see plan section **Demat / remat compatibility** and [.cursor/plans/smart_demat_remat_65de6b98.plan.md](.cursor/plans/smart_demat_remat_65de6b98.plan.md). Baseline triage: [docs/BUGFIX-MultiMap-GoalHumanFinalVisibility-And-Arbitration.md](docs/BUGFIX-MultiMap-GoalHumanFinalVisibility-And-Arbitration.md). Full plan: [.cursor/plans/sain-optimization-fix-plan_81542674.plan.md](.cursor/plans/sain-optimization-fix-plan_81542674.plan.md). |


---

## Documentation Map

All narrative documentation lives in `**docs/`**. `**INDEX.md`** (this file) is the root hub; `**docs/AGENTS.md**` is the task-oriented companion for agents.


| Document                                                                                         | Content                                                                                          | Read when                                                                |
| ------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------ |
| **[INDEX.md](INDEX.md)**                                                                         | Workspace overview, concepts, file map, layer priorities                                         | First file every session                                                 |
| **[docs/AGENTS.md](docs/AGENTS.md)**                                                             | Read order by goal, builds, hot paths, telemetry paths, doc checklist                            | Right after INDEX for implementation work                                |
| **[docs/MOD_STACK.md](docs/MOD_STACK.md)**                                                       | Complete mod inventory, dependency graph, layered stack diagram, data flow diagrams              | First understanding of the full stack and how mods connect               |
| **[docs/MOD_BUILD_AND_DEPLOY.md](docs/MOD_BUILD_AND_DEPLOY.md)**                                 | Which `.csproj` → which DLL, what to rebuild when code changes, BepInEx vs `SPT/user/mods` paths, QuestingBots + server staging     | Deploying builds; `SPTRoot` / manual copy layout; per-mod rebuild scope  |
| **[docs/INTEGRATION.md](docs/INTEGRATION.md)**                                                   | Dependencies, layer priority, interop, compatibility matrix                                      | Cross-mod changes, new mods, priority conflicts                          |
| **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)**                                                 | Per-mod internals: ticks, layers, hotspots                                                       | Deep dive on one mod                                                     |
| **[docs/SAIN_DECISION_AND_LAYER_RANKING.md](docs/SAIN_DECISION_AND_LAYER_RANKING.md)**           | `BotDecisionManager` order vs squad/solo BigBrain priorities                                     | Tuning combat vs squad; why squad evaluates before solo                  |
| **[docs/OPTIMIZED_MOD_README.md](docs/OPTIMIZED_MOD_README.md)**                                 | Fork stack guide: scheduler, perception, offline combat, builds                                  | Stack-wide behavior and configuration                                    |
| **[docs/PERFORMANCE_ARCHITECTURE.md](docs/PERFORMANCE_ARCHITECTURE.md)**                         | Optimization architecture (budget, LOD, offline)                                                 | System design for performance                                            |
| **[docs/AI_BUDGET_LOD_PLAN.md](docs/AI_BUDGET_LOD_PLAN.md)**                                     | Budget vs fidelity goals, Analogy to Anomaly, **gaps** (whole-bot skips, CSV vs feel), checklist | Tuning `MaxAiBudgetMilliseconds`, interpreting perf CSV, reducing jitter |
| **[docs/PERFORMANCE_PLAN.md](docs/PERFORMANCE_PLAN.md)**                                         | Phased execution plan + status                                                                   | Roadmap / phase tracking                                                 |
| **[docs/SAIN_PERFLOG.md](docs/SAIN_PERFLOG.md)**                                                 | **Shipped** SAINPerfLog: per-raid CSV, F12, SAIN `SainPerfLogInterop` contract                   | Telemetry, F12 copy, output paths                                        |
| **[docs/BUGFIX-SAINPerfLog-DistanceEngagementTelemetry.md](docs/BUGFIX-SAINPerfLog-DistanceEngagementTelemetry.md)** | SAINPerfLog BigBrain snapshot schema v4: player-distance buckets + engagement-at-distance counters (including ExUsec and can-shoot-now) | Auditing up-close vs far AI behavior; validating player engagement response by distance |
| **[docs/SAIN_FORK_PRESET.md](docs/SAIN_FORK_PRESET.md)**                                         | **Customized SAIN preset** for this fork: on-disk layout, default selection, **parameters → NPC behavior** (performance, hearing, PMC baseline) | Tuning fork preset; understanding what each slider does to bots          |
| **[docs/SAIN_PERFLOG_STANDALONE_PLAN.md](docs/SAIN_PERFLOG_STANDALONE_PLAN.md)**                 | Original split plan (history)                                                                    | Compare plan vs `SAIN_PERFLOG.md`                                        |
| **[docs/SMART_OFFLINE_COMBAT.md](docs/SMART_OFFLINE_COMBAT.md)**                                 | Shipped offline **slice** vs full SMART roadmap                                                  | `OfflineSquad`*, distant combat, audio spoof                             |
| **[docs/STATUS_BIGBRAIN_AND_ROGUE.md](docs/STATUS_BIGBRAIN_AND_ROGUE.md)**                       | BigBrain + Rogue: fixed vs open, strip scope                                                     | Onboarding, “Rogue-only vs global strips”                                |
| **[docs/ROGUE_BASE_DEFENSE_PLAN.md](docs/ROGUE_BASE_DEFENSE_PLAN.md)**                           | Rogue (`ExUsec`) base defense, loot suppression, **CombatSquadLayer bootstrap** (first coordination tick) | Lighthouse exUsec squads; coordinator + `ShouldBootstrapRogueDefenseCombatLayer` |
| **[docs/BIGBRAIN_LAYER_MATRIX.md](docs/BIGBRAIN_LAYER_MATRIX.md)**                               | Layer registration + vanilla strip inventory                                                     | Auditing `RemoveLayers` / arbitration                                    |
| **[docs/BUGFIX-BigBrainPriority-QuestingBots.md](docs/BUGFIX-BigBrainPriority-QuestingBots.md)** | QuestingBots vs SAIN combat gating                                                               | Passive combat with QB                                                   |
| **[docs/BUGFIX-SAINAILimit-Audibility.md](docs/BUGFIX-SAINAILimit-Audibility.md)**               | Audibility / tier bug (detect loop, Big Pipe)                                                    | Hearing tier / combat freeze                                             |
| **[docs/BUGFIX-SAINLayerPriority.md](docs/BUGFIX-SAINLayerPriority.md)**                         | Combat layer priority vs BotMind                                                                 | Stuck patrol, ignores fire                                               |
| **[docs/BUGFIX-AILimitSAIN-Deadlock.md](docs/BUGFIX-AILimitSAIN-Deadlock.md)**                   | AILimit `SetActive` + SAIN `BotActive` latch; perf CSV evidence; phased SMART + pool roadmap     | Bots ignore combat while on quest/loot; `SainBotsSampled` ≪ `SainBotsTotal` |
| **[docs/BUGFIX-AIWorkStop-LayerCacheAndSquad.md](docs/BUGFIX-AIWorkStop-LayerCacheAndSquad.md)** | Fix for the AI work-stop loop: SAINLayer cache race, squad layer lost after combat engagement, player engagement propagation within squads | Bots oscillating between fighting and looting; squad coordination lost after first contact; no reactive squad awareness when member is shot |
| **[docs/BUGFIX-VisibleBots-TelemetryAndVisibility.md](docs/BUGFIX-VisibleBots-TelemetryAndVisibility.md)** | Fix for `VisibleBots=0` in CSV telemetry (force-tick bots excluded from tier counting) + unused QueryParameters in visibility raycast | CSV shows `VisibleBots=0` during combat despite bots fighting; `TotalOnlineBots` correct but tier breakdown all zeros |
| **[docs/VISION_BLINDNESS_AND_STUTTER.md](docs/VISION_BLINDNESS_AND_STUTTER.md)** | **Canonical:** AI blindness + hear-only, stutter, **`VisionRayTarget*` vs `VisionRayEffective*`** semantics, preset-driven **>distance single-part** vision. **Shipped:** buffer alignment, per-raid counters, schema **8** effective-success columns. | Interpreting BigBrain CSV vision columns; tuning human fidelity vs CPU |
| **[docs/BUGFIX-MultiMap-GoalHumanFinalVisibility-And-Arbitration.md](docs/BUGFIX-MultiMap-GoalHumanFinalVisibility-And-Arbitration.md)** | Runtime evidence across Lighthouse/Customs/Interchange/Reserve/Factory: `GoalHumanFinal*` remains near zero on most non-Factory maps while combat pressure and vision attempts exist; recurring `Looting`/`BotMind_Questing` mismatches under pressure | Pre-fix triage baseline for the next visibility-finalization and arbitration patch pass |
| **[docs/STATUS-SAIN-PMC-NoCombat-Layers-Paused.md](docs/STATUS-SAIN-PMC-NoCombat-Layers-Paused.md)** | **Paused investigation (2026-05):** Customs session `edd84743` — BotMind questing off, no `BotMind_Questing` in CSV, but PMCs still lack SAIN combat signals; Bloodhound/boss engages; raid iteration cost noted | Resuming PMC combat-layer work without re-deriving context from chat |
| **[docs/BUGFIX-VisionRaycastJob-ABRollback.md](docs/BUGFIX-VisionRaycastJob-ABRollback.md)** | A/B rollback of `VisionRaycastJob` cadence to isolate vision acquisition failure (`GoalHuman*` visibility pipeline counters remain zero) | Bots enter combat but still do not visually acquire/shoot; need reversible timing-path validation |
| **[docs/SAIN_PERFLOG.md](docs/SAIN_PERFLOG.md)**                                                 | Standalone SAINPerfLog telemetry and schema reference (**current BigBrain `SchemaVersion` = 8**, includes `VisionRay*Total` + `VisionRayEffective*Total`) | Reading raid telemetry, interpreting vision pipeline counters, validating detection failure stage |
| **[docs/SAIN_AILIMIT_DEMATERIALIZATION.md](docs/SAIN_AILIMIT_DEMATERIALIZATION.md)**             | **Full change record:** Phases 1–3, file inventory, runtime order, build/verify, open follow-ups | Implementing or auditing pool / dematerialize / AILimit / proximity remat |
| **[.cursor/plans/smart_demat_remat_65de6b98.plan.md](.cursor/plans/smart_demat_remat_65de6b98.plan.md)** | **Execution plan:** SMART Phase 1+2 (balanced): LOS+distance demat, remat on near/LOS, `auto_*` materialize; **conflict matrix + concrete resolutions** (AILimit arbitration, pool Destroy idempotency, ABPS caps, tick order) | Implementing full SMART demat/remat; resolving integration risks before code |
| **[.cursor/plans/sain-optimization-fix-plan_81542674.plan.md](.cursor/plans/sain-optimization-fix-plan_81542674.plan.md)** | **Active roadmap (2026-05):** Phases **D–H** — visibility finalization → budget domains → interop (1B) → **spawn/pool activation smoothing** → unified orchestrator RFC; ties **demat/mat/pool** to perf CSV exit criteria. **Demat / remat compatibility** subsection binds D–G to the shipped SMART + AILimit demat/pool/remat slice ([.cursor/plans/smart_demat_remat_65de6b98.plan.md](.cursor/plans/smart_demat_remat_65de6b98.plan.md), [docs/SAIN_AILIMIT_DEMATERIALIZATION.md](docs/SAIN_AILIMIT_DEMATERIALIZATION.md)). | Executing or tracking the post-SMART-stack optimization pass; linking visibility bugs to scheduler and spawn spikes |
| **[docs/PROGRESS.md](docs/PROGRESS.md)**                                                         | Session log + completion checklist                                                               | What shipped this week / open items                                      |
| **[docs/SPTQuestingBots.md](docs/SPTQuestingBots.md)**                                           | QuestingBots architecture (**reference clone** — study / copy behavior, not stack deploy)        | QB patterns, SAIN interop when QB installed separately                   |
| **[docs/MoreBotsAPI.md](docs/MoreBotsAPI.md)**                                                   | Custom spawn types, prepatch, server                                                             | MoreBotsAPI                                                              |
| **[docs/RESEARCH.md](docs/RESEARCH.md)**                                                         | Research notes and filtered recommendations                                                      | Design rationale                                                         |
| **[docs/discussion1.md](docs/discussion1.md)**                                                   | Early architecture discussion                                                                    | Historical trade-offs                                                    |


**Mod-local readmes (not in `docs/`):** `OptimizedMod/SAIN/README.md`, `OptimizedMod/LootingBots/README.md`, `OptimizedMod/LootingBots/using_looting_bots_interop.md`, `OptimizedMod/MoreBotsAPI/README.md`.

---

## Repository Map

> **All source code lives in `OptimizedMod/`.** Ignore empty legacy top-level folders outside `OptimizedMod/` if your checkout still has them. Optional sibling **`SPTQuestingBots/`** is a **study / reference** checkout (read code, copy patterns); it is **not** co-shipped with the optimized mod stack unless you build QuestingBots yourself.

```
Tarkov AI/
├── INDEX.md                         ← Root hub (this file)
├── .cursor/plans/                   ← Execution plans: SMART demat/remat; SAIN optimization D–H (visibility, budget domains, interop, spawn/pool, orchestrator RFC)
├── docs/                            ← Narrative docs (~20 topic files + agent companion)
│   ├── AGENTS.md                    ← Task read order, builds, hot paths (AI agents)
│   ├── ARCHITECTURE.md / INTEGRATION.md / OPTIMIZED_MOD_README.md
│   ├── PERFORMANCE_*.md / AI_BUDGET_LOD_PLAN.md
│   ├── SAIN_PERFLOG.md (+ _STANDALONE_PLAN history)
│   ├── SMART_OFFLINE_COMBAT.md / STATUS_BIGBRAIN_AND_ROGUE.md / ROGUE_BASE_DEFENSE_PLAN.md
│   ├── BIGBRAIN_LAYER_MATRIX.md / BUGFIX-*.md / PROGRESS.md
│   └── RESEARCH.md / SPTQuestingBots.md / MoreBotsAPI.md / discussion1.md
│
├── OptimizedMod/                     ← ALL mod source
│   ├── SAIN/                         ← Combat AI — **shipping code: SAIN/SAIN/**
│   │   ├── SAIN.csproj               ← Builds inner `SAIN/SAIN/` tree
│   │   ├── SAIN/SAIN/SAINPlugin.cs   ← [BepInPlugin] entry, preset init
│   │   ├── SAIN/SAIN/Plugin/BigBrainHandler.cs
│   │   ├── SAIN/SAIN/Interop/        ← SAINExternal, SainPerfLogInterop (no hard ref to PerfLog DLL)
│   │   ├── SAIN/SAIN/Layers/
│   │   ├── SAIN/SAIN/Components/     ← BotManager, AIFrameBudgetScheduler, offline squad/audio, GameWorld
│   │   ├── SAIN/SAIN/Classes/Bot/
│   │   ├── SAIN/SAIN/Classes/BotManager/Jobs/
│   │   ├── SAIN/SAIN/Preset/
│   │   └── SAINServerMod/            ← Server-side SAIN project (when used)
│   │
│   ├── BigBrain/                     ← Framework: BrainManager, CustomLayer, CustomLogic
│   │   ├── BigBrainPlugin.cs         ← [BepInPlugin] entry, 6 Harmony patches
│   │   ├── Brains/BrainManager.cs    ← Singleton registry for layers/logic (IDs start at 9000)
│   │   ├── Brains/CustomLayer.cs     ← Abstract base: IsActive(), GetNextAction(), IsCurrentActionEnding()
│   │   ├── Brains/CustomLogic.cs     ← Abstract base: Update(), Start(), Stop()
│   │   ├── Internal/                 ← Wrappers bridging Custom* to EFT's native brain types
│   │   └── Patches/                  ← Harmony patches into EFT brain activation/update
│   │
│   ├── LootingBots/                  ← Looting AI (+ LootingBotsServerMod/ when used)
│   │   ├── LootingBots/LootingBots.cs     ← [BepInPlugin] entry, config, layer registration
│   │   ├── LootingBots/LootingLayer.cs    ← CustomLayer; priority via BigBrainLootLayerPriority (default 62)
│   │   ├── LootingBots/Logic/        ← CustomLogic implementations (FindLoot, Loot, Peaceful)
│   │   ├── LootingBots/Components/   ← MonoBehaviour components (LootingBrain, LootFinder)
│   │   ├── LootingBots/External.cs   ← Public interop API (ForceBotToScanLoot, etc.)
│   │   └── LootingBots/Utilities/    ← ScanScheduler, ActiveBotCache, ActiveLootCache, etc.
│   │
│   ├── Waypoints/                    ← Expanded NavMesh + door fixes
│   │   ├── WaypointsPlugin.cs        ← [BepInPlugin] entry, 7 Harmony patches
│   │   └── Patches/                  ← WaypointPatch (inject NavMesh), FindPathPatch, Door*.cs
│   │
│   ├── AILimit/                      ← Distance-based bot deactivation (retained for config compat)
│   │   ├── Plugin.cs                 ← [BepInPlugin] entry, config, 2 patches
│   │   └── Component.cs              ← AILimitComponent: sorts bots; SAIN dematerialize + pool or legacy SetActive
│   │
│   ├── ABPS/                         ← Bot spawn control (client+server)
│   │   ├── Client/Plugin.cs          ← [BepInPlugin] entry, 13 Harmony patches
│   │   └── Server/Controllers/       ← Map, PMC, Scav, Boss spawn config
│   │
│   ├── MoreBotsAPI/                  ← API for dynamic bot count scaling
│   │   ├── Plugin/Plugin.cs          ← Client BepInEx plugin
│   │   └── Server/                   ← Server-side registration
│   │
│   ├── SAINPerfLog/                  ← Standalone raid telemetry (per-raid CSV, F12, optional BigBrain snapshots)
│   │   ├── SAINPerfLog.csproj
│   │   ├── PerfLogPlugin.cs          ← [BepInPlugin]; diagnostic toggle exposed for SAIN via reflection
│   │   └── Components/               ← e.g. RaidPerfCsvLogger
│   │
│   └── OptimizationCore/           ← Shared perf/reference types (SAIN may mirror patterns; build when changing shared contracts)
│       ├── AIFrameBudgetScheduler.cs ← See also shipped `SAIN/SAIN/Components/AIFrameBudgetScheduler.cs`
│       ├── PerceptionSystem.cs       ← Player-centric visibility (frustum + raycast) and audibility
│       ├── OfflineCombatResolver.cs  ← Statistical AI-vs-AI combat using bot stats
│       ├── CombatAudioSpoofer.cs     ← Fake gunfire audio at combat zones with distance attenuation
│       ├── PerceptionTier.cs         ← Enum: Visible, Audible, Occluded
│       ├── IBudgetedAI.cs            ← Interface: ProcessAITick(), CurrentTier
│       ├── IOfflineSquad.cs          ← Interface: TickOffline(), SquadId, Members, SquadPosition
│       └── OfflineCombatTypes.cs     ← OfflineBotStats, OfflineCombatResult data models
│
└── build-output/                    ← Compiled DLLs for testing
```

---

## Key Concepts (for AI Agents)

### 1. BigBrain is the Foundation

All behavior mods register through BigBrain's `BrainManager`. The two core abstractions are:


| Abstraction      | Purpose                                                   | Lifecycle Methods                                                                 |
| ---------------- | --------------------------------------------------------- | --------------------------------------------------------------------------------- |
| `CustomLayer`    | A behavior mode (e.g., "CombatSolo", "Looting")           | `IsActive()` → `GetNextAction()` → `IsCurrentActionEnding()` → `Start()`/`Stop()` |
| `CustomLogic<T>` | An action within a layer (e.g., "DogFight", "LootCorpse") | `Start()` → `Update(T data)` → `Stop()`                                           |


**How they connect:** A `CustomLayer` returns a `CustomLayer.Action` from `GetNextAction()`.
The action specifies a `CustomLogic` type. BigBrain's internal `CustomLayerWrapper` bridges
these to EFT's native brain system.

### 2. Layer Priority Arbitrates Behavior

When multiple layers want to be active, BigBrain checks them in **descending priority order**
(higher number = checked first). The first layer where `IsActive()` returns `true` takes control.

```
Priority 99: SAIN DebugLayer         (always wins if debug mode)
Priority 80: SAIN AvoidThreatLayer   (grenade/artillery nearby)
Priority ~78: SAIN CombatSquadLayer  (defaults from LayerSettings; max 79 so below AvoidThreat)
Priority ~77: SAIN CombatSoloLayer
Priority ~74: SAIN ExtractLayer      (below combat; above default loot)
Priority ~62: LootingBots LootingLayer (BepInEx cfg; max 72 in fork so below SAIN extract)
```

**Key rule:** BigBrain picks the **highest-priority active layer**. With fork defaults, SAIN combat (~77–78) and extract (~74) beat LootingBots loot (~62); loot runs when SAIN combat layers are inactive (`IsActive()` false). Misconfigured priorities can change that — see `docs/INTEGRATION.md`.

### 3. Bot Tick Architecture (SAIN)

SAIN's per-bot tick is organized into **4 groups** with different activation conditions:


| Group                     | Activation Condition | Key Members                                                                |
| ------------------------- | -------------------- | -------------------------------------------------------------------------- |
| `_alwaysTickClasses`      | Every frame          | SAINActivationClass, SAINAILimit, SAINEnemyController, SAINDecisionClass   |
| `_tickWhenActiveClasses`  | Bot is active        | SAINBotUnstuckClass                                                        |
| `_tickWhenNoSleepClasses` | Bot not in standby   | Vision, Hearing, Mover, Cover, Steering, Memory, Suppression (~18 classes) |
| `_tickWhenCombatClasses`  | Bot in combat        | SAINShootData, AimDownSightsController, SAINFriendlyFireClass              |


### 4. Performance Architecture (SAIN)

SAIN has a **3-tier AI Limit system** based on distance from the nearest human player:


| Tier    | Distance | Vision Rate | Cover    | Decision Rate |
| ------- | -------- | ----------- | -------- | ------------- |
| None    | < 150m   | 30Hz        | 10Hz     | 10Hz          |
| Far     | 150-250m | Reduced     | 5Hz      | 5Hz           |
| VeryFar | 250-400m | Minimal     | Disabled | 3Hz           |
| Narnia  | > 400m   | Near-zero   | Disabled | 2Hz           |


#### Customized SAIN preset (this fork only)

The **OptimizedMod** SAIN plugin **creates and maintains** a custom preset for this optimization stack (harder-PMC baseline + performance / hearing tweaks). It is **not** part of stock SAIN; you only get it after deploying the **DLL built from this repository** to `BepInEx/plugins/SAIN/` and launching the game at least once.

- **Preset folder:** `BepInEx/plugins/SAIN/Presets/Optimized (Harder PMCs)/` (folder name matches `SAINPlugin.ForkOptimizedPresetName`).
- **Active selection:** `BepInEx/plugins/SAIN/Presets/ConfigSettings.json` (when the fork selects the custom preset as default on first boot).
- **Details:** [docs/SAIN_FORK_PRESET.md](docs/SAIN_FORK_PRESET.md).

### 5. Performance Architecture (LootingBots)

LootingBots has **3 performance gates**:


| Gate            | Default      | Mechanism                                            |
| --------------- | ------------ | ---------------------------------------------------- |
| ActiveBotCache  | 20 bots max  | Bots beyond cap have looting brain disabled entirely |
| Distance gating | 0 (off)      | Bots beyond N meters from player are disabled        |
| ScanScheduler   | 3 concurrent | Token-based limiter — only N bots scan at once       |


### 6. Mod Categories

The 6 forked mods (plus OptimizationCore) form **four layers** of the AI stack:


| Layer                  | Mods                           | Function                                                                                                                   |
| ---------------------- | ------------------------------ | -------------------------------------------------------------------------------------------------------------------------- |
| **Infrastructure**     | Waypoints, AILimit             | NavMesh data, bot activation. Below behavior mods.                                                                         |
| **Behavior Framework** | BigBrain                       | `BrainManager`, `CustomLayer`/`CustomLogic` abstractions.                                                                  |
| **Behavior Mods**      | SAIN, LootingBots              | Combat AI and looting AI. Extend BigBrain.                                                                                 |
| **Spawn/Placement**    | ABPS (botplacementsystem fork) | Controls what bots spawn, where, and how many. Client+Server.                                                              |
| **Performance/Scale**  | OptimizedMod/OptimizationCore  | Frame budget scheduling, player-centric perception LOD, offline combat resolution, audio spoofing. Wraps the entire stack. |


### 7. No-BigBrain Mods

Three mods operate **without BigBrain** because they work at a lower level:


| Mod                           | Integration Mechanism                                                               | Why No BigBrain                                          |
| ----------------------------- | ----------------------------------------------------------------------------------- | -------------------------------------------------------- |
| **Waypoints**                 | Direct Harmony patches into `BotsController.Init` and `BotPathFinderClass.FindPath` | Operates at Unity NavMesh level, below brain abstraction |
| **AILimit**                   | `MonoBehaviour` on GameWorld; SAIN `Dematerialization` + pool when present, else `SetActive(false)` | Operates below brain tick; optional SAIN project ref     |
| **botplacementsystem (ABPS)** | 13 Harmony patches into bot spawn system                                            | Controls bot spawning, not bot behavior                  |


---

## Common Agent Tasks

### Task: Add a new bot behavior

1. Skim `docs/AGENTS.md` read-order row for architecture
2. Read `docs/ARCHITECTURE.md` → SAIN Layer/Action System section
3. Read `docs/INTEGRATION.md` → SAIN ↔ BigBrain → Registration Flow
4. Create a new class extending `SAINLayer` or `CustomLayer`
5. Register via `BrainManager.AddCustomLayer()` in plugin init
6. Choose appropriate priority (above/below existing layers)

### Task: Fix a performance issue

1. Read `docs/AI_BUDGET_LOD_PLAN.md` if symptoms are jitter / “CSV looks fine but feels wrong”
2. Read `docs/ARCHITECTURE.md` → SAIN Performance Hotspots section
3. Read `docs/PERFORMANCE_ARCHITECTURE.md` for the full optimization architecture and hotspot analysis
4. Identify the hotspot from the ranked priority table
5. Check the AI Limit tiering system (`Bot.CurrentAILimit`) for throttling hooks
6. Use `WaitForSeconds` caching, `ShallTick()` gating, or coroutine interval adjustment
7. For per-raid telemetry, use `OptimizedMod/SAINPerfLog/` output files (`BepInEx/LogOutput/sain_perf/`) instead of legacy `sain_perf.csv` overwrite workflow
8. For **live scheduler counters** and the **diagnostic logging** toggle, use **F12 → `SAINPerfLog` → `SAINPerfLog (F12)`** (SAIN itself no longer exposes a **SAIN Performance** section)

### Task: Understand how SAIN and LootingBots coexist

1. Read `docs/INTEGRATION.md` → SAIN ↔ LootingBots section
2. Key insight: Numeric priority determines behavior (fork default ~62 loot vs higher SAIN combat).
3. Interop API: LootingBots exposes `External.PreventBotFromLooting()` and `ForceBotToScanLoot()`

### Task: Add a new mod to the workspace

1. Fork the mod into `OptimizedMod/<ModName>/`
2. Read `docs/INTEGRATION.md` → Adding a New Mod → Template
3. Document in `docs/ARCHITECTURE.md` (new section before Directory Map)
4. Document in `docs/INTEGRATION.md` (new section before Mod Compatibility Matrix)
5. Update the compatibility matrix and known integration points table
6. Update `INDEX.md` (documentation map + repository map) and `docs/AGENTS.md` if onboarding paths change

### Task: Understand BigBrain's internal mechanics

1. Read `docs/ARCHITECTURE.md` → BigBrain section
2. Key classes: `BrainManager`, `CustomLayer`, `CustomLogic`, `CustomLayerWrapper`, `CustomLogicWrapper`
3. IDs start at 9000 (`START_LAYER_ID`, `START_LOGIC_ID`)
4. Patches: 6 Harmony patches into bot brain lifecycle

### Task: Reduce bot CPU usage across all mods

1. **AILimit**: Configure per-map distances and `BotLimit` (default 10) for binary bot deactivation
2. **SAIN AI Limit**: Enable internal throttling tiers (Far/VeryFar/Narnia) in preset settings
3. **ABPS**: Reduce `SoftCap` and per-map limits, enable `DespawnFurthest`
4. **Combined approach**: AILimit for farthest, SAIN limit for mid-range, ABPS for total cap
5. **OptimizedMod approach**: Use `AIFrameBudgetScheduler` (2ms hard cap with Visible/Audible/Occluded
  priority tiers) + `PerceptionSystem` (camera frustum + raycast, replacing distance-only AILimit) +
   `OfflineCombatResolver` (statistical combat for bots outside player view) + `CombatAudioSpoofer`
   (fake audio instead of real AI processing for distant combat)
6. See `docs/PERFORMANCE_ARCHITECTURE.md` for identified code-level hotspots
7. For the **implemented offline slice** (auto squad registration, procedural distant audio, roadmap): `docs/SMART_OFFLINE_COMBAT.md`

### Task: Tune PMC spawns with ABPS

1. **ABPS** (client-side): Set per-map `PmcSpawnDistanceCheck` and map limits
2. Configure `SoftCap` and per-map limits in `OptimizedMod/ABPS/Server/config.json`
3. See `docs/INTEGRATION.md` → `botplacementsystem (ABPS) ↔ Ecosystem` for the interaction analysis

### Task: Debug bot pathfinding issues

1. Check if **Waypoints** custom NavMesh is loaded (verify `EnableCustomNavmesh` is enabled in config)
2. Check `OptimizedMod/Waypoints/Patches/FindPathPatch.cs` — it overrides `BotPathFinderClass.FindPath`
3. Enable debug components via `DebugPatch` for NavMesh/BotZone visualization
4. If navmesh bundles are missing, check `OptimizedMod/Waypoints/navmesh/` for `.bundle` files

---

## Performance Baselines

Real-world frame time measurements for calibration. These baselines help quantify the gap
between current performance and the 16.7ms target (60 fps). Each row is a concrete datapoint
from actual gameplay.


| #     | Map    | Bots         | Scenario | Frame Time | FPS               | CPU          | GPU        | Date       |
| ----- | ------ | ------------ | -------- | ---------- | ----------------- | ------------ | ---------- | ---------- |
| **1** | Custom | 5-6 fighting | Combat   | ~20ms      | ~15 (↓45 from 60) | Ryzen 5 5600 | RX 5700 XT | 2026-05-02 |
| 2     | *TBD*  | *TBD*        | *TBD*    | *TBD*      | *TBD*             | *TBD*        | *TBD*      | —          |
| 3     | *TBD*  | *TBD*        | *TBD*    | *TBD*      | *TBD*             | *TBD*        | *TBD*      | —          |


**Interpretation of Baseline 1:**

- With only 5-6 bots fighting on a custom map, frame time already exceeds the 16.7ms budget by ~3ms
- At typical bot counts (15-20 on Streets/Lighthouse), this scales non-linearly due to O(N²) vision checks
- Target: bring 5-6 bot combat frame time under 16.7ms → then scale to 15-20 bots
- See `docs/PERFORMANCE_ARCHITECTURE.md` for the optimization architecture and performance plan

**OptimizedMod target:** Lighthouse with 29+ bots at 60 FPS (≤16.7ms frame time) using the full
OptimizationCore stack — budget scheduler at 2ms cap, perception LOD (camera frustum + raycast
instead of distance-only checks), offline combat resolution for AI-vs-AI, and audio spoofing.

**How to add a baseline:** Run a raid, note map + approximate bot count + scenario (idle/combat/looting),
record frame time (BepInEx.FPSCounter or SAIN debug overlay), and add a row above.

---

## File Location Quick Reference

> All code paths are relative to `OptimizedMod/`. All document paths are relative to `docs/`.


| Need                                   | File(s)                                                                   | Section                                                                                             |
| -------------------------------------- | ------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------- |
| What to compile + where to deploy after updates | `docs/MOD_BUILD_AND_DEPLOY.md` | `What to rebuild when code changes`, `Recommended compile set for the optimized runtime stack`, `Quick reference table (OptimizedMod)` |
| SAIN layer registration code           | `SAIN/SAIN/Plugin/BigBrainHandler.cs`                                     | `BrainAssignment.Init()`                                                                            |
| Lighthouse exUsec squad + loot suppress | `SAIN/SAIN/Layers/Combat/Squad/SquadCombatCoordinator.cs`, `CombatSquadLayer.cs` | `CoordinateSquad`, `ShouldBootstrapRogueDefenseCombatLayer` (rogue defense bootstrap); see [docs/ROGUE_BASE_DEFENSE_PLAN.md](docs/ROGUE_BASE_DEFENSE_PLAN.md) |
| AI work-stop loop + squad fix          | `SAIN/SAIN/Layers/SAINLayer.cs`, `CombatSquadLayer.cs`, `SquadCombatCoordinator.cs`, `BotDecisionManager.cs`, `Squad.cs`, `BotSquadClass.cs` | `CheckIsActiveWithCache()`, `IsActive()`, `DistributeTargets()`, `getDecision()`, `SetSquadDecision()`, `ReportPlayerEngagement()`; see [docs/BUGFIX-AIWorkStop-LayerCacheAndSquad.md](docs/BUGFIX-AIWorkStop-LayerCacheAndSquad.md) |
| 0 Visible bots CSV + visibility raycast | `AIFrameBudgetScheduler.cs`, `SAIN/SAIN/Classes/Bot/SAINAILimit.cs`       | `ProcessFrame()` force-tick tier classification, `CheckPlayerCanSeeBot()` `QueryParameters`; see [docs/BUGFIX-VisibleBots-TelemetryAndVisibility.md](docs/BUGFIX-VisibleBots-TelemetryAndVisibility.md) |
| Distance + vision telemetry (Schema v8) | `SAINPerfLog/Components/RaidPerfCsvLogger.cs`, `SAIN/SAIN/Classes/BotManager/Jobs/VisionRaycastJob.cs` | Includes `Dist*`, `Engaged*`, `CanShootNow*`, `GoalHuman*`, low-level `VisionRay*Total` attempt/null/target/blocked counters, and `VisionRayEffective*`; see [docs/SAIN_PERFLOG.md](docs/SAIN_PERFLOG.md), [docs/BUGFIX-VisionRaycastJob-ABRollback.md](docs/BUGFIX-VisionRaycastJob-ABRollback.md) |
| Multi-map `GoalHumanFinal*` failure triage | `SAIN/SAIN/Classes/BotManager/Jobs/VisionRaycastJob.cs`, `SAIN/SAIN/Classes/Bot/EnemyClasses/Vision/`, `SAIN/SAIN/Interop/SAINExternal.cs`, `SAINPerfLog/Components/RaidPerfCsvLogger.cs` | Runtime baseline and mismatch exemplars across maps before fix pass; see [docs/BUGFIX-MultiMap-GoalHumanFinalVisibility-And-Arbitration.md](docs/BUGFIX-MultiMap-GoalHumanFinalVisibility-And-Arbitration.md) |
| Looting suppression under combat pressure | `LootingBots/LootingBots/LootingLayer.cs` | `IsActive()` hard-gates looting when SAIN combat pressure is true (reflection call to `SAINExternal.IsBotUnderCombatPressure`) to prevent non-combat layer takeover during engagements |
| Vision acquisition A/B rollback process | `SAIN/SAIN/Classes/BotManager/Jobs/VisionRaycastJob.cs` | Restored per-frame cadence for `EnemyVisionJob`/`UpdateEFTVision` as controlled test; see [docs/BUGFIX-VisionRaycastJob-ABRollback.md](docs/BUGFIX-VisionRaycastJob-ABRollback.md) |
| LootingBots layer registration         | `LootingBots/LootingBots/LootingBots.cs`                                  | `Awake()`                                                                                           |
| How a CustomLayer works                | `BigBrain/Brains/CustomLayer.cs`                                          | Full file (49 lines)                                                                                |
| How a CustomLogic works                | `BigBrain/Brains/CustomLogic.cs`                                          | Full file (27 lines)                                                                                |
| BigBrain wrapper internals             | `BigBrain/Internal/CustomLayerWrapper.cs`, `CustomLogicWrapper.cs`        | Whole files                                                                                         |
| SAIN tick entry point                  | `SAIN/SAIN/Components/GameWorldComponent.cs`                              | `WorldTick()`                                                                                       |
| SAIN bot tick groups                   | `SAIN/SAIN/Components/BotComponent.cs`                                    | `ManualUpdate()`, `TickClassGroup()`                                                                |
| SAIN performance architecture          | `docs/PERFORMANCE_ARCHITECTURE.md`                                        | Full document                                                                                       |
| SAIN AI limit tiers                    | `SAIN/SAIN/Classes/Bot/SAINAILimit.cs`                                    | `AILimitSetting` enum                                                                               |
| SAINAILimit audibility bugfix          | `docs/BUGFIX-SAINAILimit-Audibility.md`                                   | Full document — 3 bugs, fixes, verification guide                                                   |
| SAIN layer priority bugfix             | `docs/BUGFIX-SAINLayerPriority.md`                                        | Full document — priority conflict with BotMind, fix and verification                                |
| LootingBots interop API                | `LootingBots/LootingBots/External.cs`                                     | All public methods                                                                                  |
| LootingBots scan scheduling            | `LootingBots/LootingBots/Utilities/ScanScheduler.cs`                      | `CanStartScan()`                                                                                    |
| LootingBots loot state machine         | `LootingBots/LootingBots/Components/LootingBrain.cs`                      | `Update()`, async loot methods                                                                      |
| All mod dependency declarations        | Each mod's plugin `.cs` file                                              | `[BepInDependency]` attributes                                                                      |
| Waypoints NavMesh injection            | `Waypoints/Patches/WaypointPatch.cs`                                      | `InjectNavmesh()`                                                                                   |
| Waypoints pathfinding override         | `Waypoints/Patches/FindPathPatch.cs`                                      | `PatchPrefix()`                                                                                     |
| AILimit bot activation logic           | `AILimit/Component.cs`                                                    | `Update()`, `UpdateBots()`                                                                          |
| AILimit config (distances/limits)      | `AILimit/Plugin.cs`                                                       | Config.Bind entries                                                                                 |
| ABPS spawn patches                     | `ABPS/Client/Plugin.cs`                                                   | `Awake()` → patch list                                                                              |
| ABPS server config                     | `ABPS/Server/Models/AbpsConfig.cs`                                        | Full config model                                                                                   |
| OptimizationCore budget scheduler      | `OptimizationCore/AIFrameBudgetScheduler.cs`                              | `Update()`, `ProcessTier()`, `RegisterBot()`                                                        |
| OptimizationCore perception            | `OptimizationCore/PerceptionSystem.cs`                                    | `EvaluateBot()`, `IsVisible()`                                                                      |
| OptimizationCore offline combat        | `OptimizationCore/OfflineCombatResolver.cs`                               | `ResolveCombat()`, `CalculateSquadPower()`                                                          |
| OptimizationCore audio spoofing        | `OptimizationCore/CombatAudioSpoofer.cs`                                  | `ScheduleCombatAudio()`, `PlayCombatSequence()`                                                     |
| SAIN shipped offline combat + audio    | `SAIN/SAIN/Components/OfflineCombatResolver.cs`, `CombatAudioSpoofer.cs`  | Runtime resolver + procedural fallback audio                                                        |
| SAIN auto offline squads (SMART slice) | `SAIN/SAIN/Components/OfflineSquadWorldSync.cs`, `BotManagerComponent.cs` | `TrySync`, `RegisterOfflineSquad`; see [docs/SMART_OFFLINE_COMBAT.md](docs/SMART_OFFLINE_COMBAT.md) |
| AILimit ↔ SAIN deadlock + dematerialize seam | `SAIN/SAIN/Components/AIFrameBudgetScheduler.cs`, `BotDematerializationController.cs`, `BotGameObjectPool.cs`, `OfflineSquadMaterialization.cs`, `AILimit/Component.cs` | `RecheckActivation` each frame; `Pool`, `Dematerialization`; proximity `demat_*` remat; see [docs/SAIN_AILIMIT_DEMATERIALIZATION.md](docs/SAIN_AILIMIT_DEMATERIALIZATION.md), [docs/BUGFIX-AILimitSAIN-Deadlock.md](docs/BUGFIX-AILimitSAIN-Deadlock.md) |
| Spawn waves + pool remat FPS (roadmap Phase G) | `SAIN/SAIN/Classes/BotManager/BotSpawnController.cs`, `SAIN/SAIN/Components/BotGameObjectPool.cs`, `SAINPerfLog/Components/RaidPerfCsvLogger.cs` (spawn/pool/`NonSainFrameMs` columns) | Stagger activation / defer non-critical init on pooled rematerialize; see [.cursor/plans/sain-optimization-fix-plan_81542674.plan.md](.cursor/plans/sain-optimization-fix-plan_81542674.plan.md) Phase **G** and section **Demat / remat compatibility** (preserve demat→pool→remat invariants). |
| SAIN offline→online materialization    | `SAIN/SAIN/Components/OfflineSquadMaterialization.cs`                     | **`demat_*`** (AILimit-parked bots): proximity remat + `TryBeginMaterialize` **shipped**. **`auto_*`** (statistical distant fights): spawn / `OfflineCombatResult` handoff **not** implemented — [docs/SAIN_AILIMIT_DEMATERIALIZATION.md](docs/SAIN_AILIMIT_DEMATERIALIZATION.md) |
| SMART demat/remat decision system (Phase 1+2) | `SAIN/SAIN/Components/SmartDematSystems.cs`                     | Centralized LOS + hearing gates for `demat_*` demat; `DematParkReason` arbitration; hearing recheck before remat — [docs/SAIN_AILIMIT_DEMATERIALIZATION.md](docs/SAIN_AILIMIT_DEMATERIALIZATION.md) |
| Auto materialization from `auto_*` combat | `SAIN/SAIN/Components/AutoSquadMaterialization.cs`                     | Live-bot materialization from offline `auto_*` combat when player nears; cap-gated (`MaxAutoMatPerFrame`); perf CSV `AutoMatAppliedDelta` |
| Spawn↔pool bridge for wave coordination | `SAIN/SAIN/Components/BotSpawnPoolBridge.cs`                             | Coordinates `BotSpawnController` waves with pool remat; avoids duplicate accounting in `OnBotActivated`/`RemoveBot` |
| Spawn event CSV logger (F12 4.)         | `SAINPerfLog/Components/SpawnEventCsvLogger.cs`                          | Per-event spawn/despawn/pool-return rows with `AutoMat` tagging; see [docs/SAIN_PERFLOG.md](docs/SAIN_PERFLOG.md) |
| SAIN ↔ QuestingBots / combat API       | `SAIN/SAIN/Interop/SAINExternal.cs`                                       | `IsBotInCombat`, combat pressure helpers                                                            |
| SAIN ↔ SAINPerfLog (reflection)        | `SAIN/SAIN/Interop/SainPerfLogInterop.cs`                                 | Diagnostic logging gate; no assembly reference to PerfLog                                           |
| Raid CSV logger implementation         | `SAINPerfLog/Components/RaidPerfCsvLogger.cs`                             | Per-raid append, flush on raid end                                                                  |
| OptimizationCore interfaces            | `OptimizationCore/IBudgetedAI.cs`, `IOfflineSquad.cs`                     | `ProcessAITick()`, `TickOffline()`                                                                  |
| OptimizationCore types/enums           | `OptimizationCore/PerceptionTier.cs`, `OfflineCombatTypes.cs`             | `PerceptionTier` enum, `OfflineBotStats`, `OfflineCombatResult`                                     |


---

## Layer Priority Reference

Complete priority hierarchy across all mods:


| Priority  | Layer                | Mod         | Bot Types                                                      |
| --------- | -------------------- | ----------- | -------------------------------------------------------------- |
| 99        | DebugLayer           | SAIN        | All                                                            |
| 80        | SAINAvoidThreatLayer | SAIN        | All                                                            |
| ~78       | CombatSquadLayer     | SAIN        | All (`LayerSettings` default; cap 79 to stay below AvoidThreat 80) |
| ~77       | CombatSoloLayer      | SAIN        | All                                                            |
| ~74       | ExtractLayer         | SAIN        | PMCs, Scavs (configurable; below combat, above typical loot) |
| ~62 (cfg) | LootingLayer         | LootingBots | All registered brains (`BigBrainLootLayerPriority` default 62) |


---

## Quick Answers

**Q: Why can't SAIN and LootingBots run at the same time?**
A: They can coexist on every bot; BigBrain activates **one** layer per decision cycle based on
`IsActive()` and numeric priority. With fork defaults, SAIN combat/extract priorities beat LootingBots'
loot layer (~62). When combat SAIN layers go inactive, loot/peace can run.

**Q: Where are performance settings stored?**
A: SAIN: `OptimizedMod/SAIN/Preset/GlobalSettings/Categories/General/PerformanceSettings.cs`. LootingBots:
`MaxActiveLootingBots`, `LimitDistanceFromPlayer`, `MaxConcurrentScans` config entries in
`OptimizedMod/LootingBots/LootingBots/LootingBots.cs`.

**Q: How do I make a bot more aggressive?**
A: Modify SAIN's personality settings or per-bot difficulty settings in the SAIN preset system.
See `docs/PERFORMANCE_ARCHITECTURE.md` for the optimization architecture and preset configuration details.

**Q: How do I make bots loot more?**
A: Tweak LootingBots config: lower `LootScanInterval` (default 15s), increase `MaxActiveLootingBots`
(default 20), lower value thresholds, or enable more loot types. Config entries in `LootingBots.cs`.

**Q: What happens when a new mod's layer conflicts with existing layers?**
A: BigBrain checks layers in priority order. The first active layer wins. To ensure your layer
runs, set its priority appropriately. Use `BrainManager.GetActiveLayer()` and
`BrainManager.GetActiveLayerName()` to debug which layer is active.

**Q: How does AILimit differ from SAIN's internal AI Limit?**
A: AILimit parks distant bots via SAIN dematerialization + pool when SAIN is loaded (legacy `SetActive(false)` fallback), at per-map distances (80-400m). SAIN's AI Limit throttles subsystems while keeping bots active. They can complement each other.

**Q: How do ABPS and bot limits interact?**
A: ABPS handles bot spawn caps and distance checks. AILimit further deactivates distant spawned bots
at the GameObject level. SAIN's internal AI Limit throttles subsystems for mid-range bots. They
operate at different layers and can complement each other.

**Q: What is the current progress of AI object recycle spawn/despawn?**
A: **Shipped:** SAIN + AILimit **dematerialize → pool → rematerialize** for `demat_*`: `BotDematerializationController.RequestDematerialize` → `BotGameObjectPool.ReturnToPool` → proximity / squad remat via `OfflineSquadMaterialization` (and related activation paths). **Telemetry shipped:** interval perf CSV deltas (`SpawnsThisInterval`, `DespawnsThisInterval`, `Pool*ThisInterval`) and optional per-event `sain_spawn_events_*.csv` from SAINPerfLog F12. **Still open:** (1) full SMART `auto_*` offline-to-online spawn/state handoff from statistical results (no spawn-from-stats yet); (2) **Phase G** of [.cursor/plans/sain-optimization-fix-plan_81542674.plan.md](.cursor/plans/sain-optimization-fix-plan_81542674.plan.md) — cap per-frame **materialize/activate** work after waves using `BotSpawnController` + pool telemetry (`NonSainFrameMs` spikes), **without** breaking the demat→pool→remat contract (see that plan’s **Demat / remat compatibility** section). See [docs/SAIN_AILIMIT_DEMATERIALIZATION.md](docs/SAIN_AILIMIT_DEMATERIALIZATION.md), [docs/SMART_OFFLINE_COMBAT.md](docs/SMART_OFFLINE_COMBAT.md), [docs/PROGRESS.md](docs/PROGRESS.md).

**Q: How do I reduce CPU usage from bots?**
A: Three complementary approaches from the original mods: (1) AILimit — completely disable distant bots,
(2) SAIN AI Limit settings — throttle vision/hearing for far bots, (3) ABPS — reduce map bot caps and
enable despawn. For the optimized fork stack, use the **OptimizationCore budget scheduler** (2ms hard
cap per frame with Visible/Audible/Occluded priority tiers) and **PerceptionSystem** (player-centric
camera frustum + raycast checks instead of distance-only AILimit). Also see `docs/PERFORMANCE_ARCHITECTURE.md`
for the optimization architecture.

**Q: How do SAIN's performance optimizations affect LootingBots?**
A: They don't directly. But both mods add per-bot CPU overhead. The performance architecture guide
(`docs/PERFORMANCE_ARCHITECTURE.md`) notes that SAIN changes are "fully compatible" with LootingBots
since they're separate systems, but the combined load should be considered on high-bot-count maps.