---
name: SAIN-optimization-fix-plan
overview: "User direction (2026-05-06): expand budgeting beyond SAIN-only ticks (1B SAIN+interop, balanced triage) and roadmap toward unified multi-mod control. Phases D–H must stay compatible with the shipped SMART demat/remat + pool slice (see Demat/remat compatibility). Historical tree-merge appendix remains reference-only."
todos:
  - id: hist-a-b-superseded
    content: "Historical Phases A–B (tree merge / perf monitor): already reflected in current `OptimizedMod/SAIN/SAIN/` — do not re-execute; see appendix."
    status: completed
  - id: c1-csproj-verify-only
    content: "When touching `OptimizedMod/SAIN/SAIN.csproj`, only adjust explicit `Compile Remove` globs for stray root duplicates — do NOT remove inner `SAIN/SAIN/**` from compilation (not applicable; inner tree is the ship set)."
    status: completed
  - id: d1-visibility-finalization
    content: "Phase D — Fix `GoalHumanFinal*` handoff (`EnemyVisionClass`, `EnemyPartDataClass` decay, `EnemyInfo` sync); validate vs `docs/BUGFIX-MultiMap-GoalHumanFinalVisibility-And-Arbitration.md`."
    status: completed
  - id: e1-budget-domains
    content: "Phase E — Introduce schedulable budget domains inside/near `AIFrameBudgetScheduler` (beyond raw `TickInterval` throttling)."
    status: pending
  - id: f1-interop-gates-1b
    content: "Phase F (1B) — Extend `SAINExternal` / LootingBots reflection seams / diagnostics so combat-pressure defers third-party layers without breaking idle quest/loot."
    status: pending
  - id: g1-spawn-smoothing
    content: "Phase G — Spawn/activation smoothing (`BotSpawnController`, pool remat) per perf CSV; preserve SMART demat/remat invariants (pool idempotency, exclusion list, remat before combat-ready)."
    status: pending
  - id: h1-unified-orchestrator-rfc
    content: "Phase H — RFC for unified multi-mod orchestration (multi-release); depends on stable D–G metrics."
    status: pending
isProject: false
---

# SAIN Optimization Fix Plan

## Current direction (2026-05-06) — agreed scope

**User choices**

- **Scope (1B):** SAIN + interop — first pass includes SAIN-owned budgeting/deferral **and** targeted interop so non-SAIN combat pressure does not fight the scheduler (LootingBots / BotMind-style arbitration where we already have reflection or `SAINExternal` seams).
- **Triage bias (balanced):** protect **proximity + combat pressure + GoalEnemy** paths first, but still spread background work so FPS does not collapse on wave spawns.
- **Long-term product goal:** **merge / unify mod control surfaces** (single orchestration layer, shared telemetry, shared tick domains) so one budget policy can govern more than `BotComponent.ManualUpdate` alone.

**Why FPS still drops when SAIN budget looks tiny**

- The **2 ms cap** only bounds work routed through `AIFrameBudgetScheduler.ProcessFrame` → `BotComponent.ManualUpdate`.
- Spawn bursts, EFT native AI, other plugins, physics/render/GC dominate `FrameTimeMs` / `NonSainFrameMs` in your perf CSVs.

**Implementation strategy (phased)**

1. **Phase D — Visibility finalization (highest player-facing priority)** — Fix `GoalHumanFinal*` handoff (`EnemyVisionClass.UpdateVisibleState`, part decay `SUCCESS_PERIOD`, `EnemyInfo` sync) per [docs/BUGFIX-MultiMap-GoalHumanFinalVisibility-And-Arbitration.md](docs/BUGFIX-MultiMap-GoalHumanFinalVisibility-And-Arbitration.md) (repo root). Validate with Lighthouse + one control map.

2. **Phase E — Budget domains inside SAIN** — Introduce explicit domains (e.g. `Perception`, `EnemyList`, `Coordination`, `Background`) with token budgets inside `AIFrameBudgetScheduler` or adjacent coordinator — **move** work into schedulable units instead of only shrinking `TickInterval`.

3. **Phase F — Interop gates (1B)** — Extend existing seams (`SAINExternal`, LootingBots reflection gate, BigBrain mismatch diagnostics) so **combat-pressure windows** defer third-party layers without breaking QB when not under pressure.

4. **Phase G — Spawn / activation smoothing (FPS spike)** — Use `BotSpawnController` + pool telemetry already in `sain_perf_*.csv` to cap **per-frame activation work** (stagger `Activate`, defer non-critical init for pooled remats). This targets your “FPS dies on bot spawn” symptom directly. **Must remain compatible** with the demat/remat + `BotGameObjectPool` pipeline (see **Demat / remat compatibility** below): do not bypass `BotDematerializationController`, break pool idempotency, or weaken `BotSpawnController.IsWildSpawnStrictlyExcluded` semantics shared with SMART gating.

5. **Phase H — Unified mod control (your merge suggestion; multi-release)** — Architectural consolidation: shared **orchestrator** project or `OptimizationCore`-style façade that owns tick registration, budget policy, and cross-mod hooks — **not** a single PR. Prerequisite: stable Phase D–G metrics.

**Exit criteria**

- `GoalHumanFinalVisibleCount` / `GoalHumanFinalCanShootCount` correlate with contact on non-Factory maps.
- `NonSainFrameMs` spikes correlate with spawn waves; after Phase G, worst-frame spikes measurably lower at same bot counts.
- `MismatchCombatSignals` / `thirdPartyOrVanilla` trend down under sustained pressure without starving quest/loot when not pressured.
- **Demat/remat:** After pooled remat, bots reach **combat-ready** state without deadlock (AILimit latch + SAIN `BotActive`); `Pool*ThisInterval` / spawn CSV still interpret correctly.

## Demat / remat compatibility (SMART + AILimit slice)

This plan layers on top of the **shipped** distance/LOS dematerialize path, pool recycle, and proximity/LOS rematerialize behavior. Treat [.cursor/plans/smart_demat_remat_65de6b98.plan.md](.cursor/plans/smart_demat_remat_65de6b98.plan.md) and [docs/SAIN_AILIMIT_DEMATERIALIZATION.md](docs/SAIN_AILIMIT_DEMATERIALIZATION.md) as the **contract** for those systems.

**Core components (do not regress without an explicit migration doc)**

- **Dematerialize:** `BotDematerializationController` → `BotGameObjectPool.ReturnToPool` (parked / `demat_*` path).
- **Pool:** idempotent return, Destroy-prefix safety, `ResetForPoolRecycle` on `SAINAILimit` / bot reuse expectations.
- **Rematerialize:** `OfflineSquadMaterialization` (proximity + LOS throttled remat), `AutoSquadMaterialization` (cap-gated `auto_*` casualty reconcile on live bots).
- **SMART gate:** `SmartDematerializeGate` and related helpers in `SmartDematSystems.cs` (`SainDematPolicy`, `DematParkReason`) — respect AILimit arbitration and squad parking.
- **Spawn exclusions:** `BotSpawnController.IsWildSpawnStrictlyExcluded` — shared by SMART demat and spawn registration; any Phase **G** change to spawn flow must keep boss/special exclusions identical or centrally owned.

**Phase-by-phase compatibility rules**

- **D (visibility):** Fixes apply to **active** and **post-remat** bots alike. Ensure `EnemyInfo` / `EnemyVisionClass` / part decay do not assume “never pooled.” Validate one **remat** scenario (distant park → near player → visible again) on the same maps as pure visibility tests.
- **E (budget domains):** Scheduler splits must **not** skip or reorder `RecheckActivation` / AILimit-driven activation edges relative to demat without review. Pooled or freshly rematerialized bots may sit in **Occluded** tier briefly — domain budgets should still allow minimum perception + activation handoff ticks.
- **F (interop):** Combat-pressure and layer deferral must **not** leave bots permanently dematerialized, block remat triggers, or fight `DematParkReason` arbitration (LootingBots/QB inactive while parked is OK; **stuck inactive after remat** is not).
- **G (spawn smoothing):** Staggering `Activate` / deferred init must preserve: single authoritative path into/out of the pool, no double-activate, remat completes before relying on `GoalEnemy` / combat pressure. Prefer **queue + budget** over skipping pool pulls. `BotSpawnPoolBridge` today is telemetry-only — future EFT `BotCreator` ↔ pool integration must follow the same invariants.
- **Telemetry:** Changes to frame cadence must keep `SpawnsThisInterval`, `DespawnsThisInterval`, `Pool*ThisInterval`, and `NonSainFrameMs` semantically aligned so before/after demat-aware regressions stay comparable.

**Workstream ordering**

- Finish or **freeze** a SMART demat/remat + pool change set before large **Phase G** edits, or test both together on every wave/remat scenario.
- **Phase G** and SMART spawn/pool work touch the same files (`BotManagerComponent`, `BotSpawnController`, `BotGameObjectPool`, materialization helpers); serialize ownership or merge in one branch.

### Final consistency check (2026-05-06)

- **User scope (1B + balanced):** Captured in **Current direction** and YAML todos **f1**, **e1**.
- **Phase D first:** Correct ordering for player-visible regressions before broad scheduler refactors.
- **Doc link:** Use repo-root path [docs/BUGFIX-MultiMap-GoalHumanFinalVisibility-And-Arbitration.md](docs/BUGFIX-MultiMap-GoalHumanFinalVisibility-And-Arbitration.md) (avoid `../../` — depends on viewer CWD).
- **YAML A–B vs repo:** Superseded — `PerceptionTier`, public `TickInterval`, `CurrentPerceptionTier`, inner `AIFrameBudgetScheduler` already exist under `OptimizedMod/SAIN/SAIN/`; root duplicate merge items are not active work.
- **B3 `SAINPerformanceMonitor`:** Obsolete for execution — not in shipping SAIN tree; telemetry is **SAINPerfLog** (see project docs).
- **Phase C “remove inner SAIN Compile Remove”:** Wrong for current tree — `SAIN.csproj` excludes **root-level** stray globs only; inner `SAIN/SAIN/**` is the compiled ship set. Do not delete a non-existent `Compile Remove="SAIN\**\*.cs"` pattern.
- **Phase H:** Correctly scoped as multi-release RFC, not bundled into Phase D.
- **Demat/remat:** Section **Demat / remat compatibility** added; Phase **G** todo and exit criteria extended accordingly.

---

## Historical appendix — SAIN tree merge checklist (mostly completed in repo)

The remainder of this document describes an earlier **duplicate-file merge into `SAIN\\SAIN\\`**. Many items are already shipped; keep only as archaeology when touching csproj layout.

## Architecture Context

The mod root at `e:\spt-tarkov-ai\OptimizedMod\SAIN\` has two `.csproj` files:

| Project | Location | Current compile scope |
|---------|----------|----------------------|
| Root `SAIN.csproj` | `OptimizedMod\SAIN\SAIN.csproj` | SDK glob minus explicit **`Compile Remove`**: strips duplicate roots (`*.cs`, `Layers\**\*.cs`, `Components\**\*.cs`, …). **`SAIN\SAIN\**\*.cs` remains compiled** — this is the shipping tree for `SAIN.dll`. |
| Nested `SAIN.csproj` | `OptimizedMod\SAIN\SAIN\SAIN.csproj` | Same logical sources under `SAIN\SAIN\` when built standalone (e.g. tooling open nested project only). |

The root `.csproj` comment explicitly documents allowing `SAIN\SAIN\` sources while excluding stray duplicates at the mod folder root — agents should **not** assume `SAIN\**\*.cs` is excluded anymore.

---

## Phase A: Merge duplicates into SAIN\SAIN\ originals

These 4 root-level files contain optimization changes that must be merged into their `SAIN\SAIN\` counterparts, then the root copies deleted.

### A1. Add `PerceptionTier` enum to `SAIN\SAIN\SAINEnum.cs`

Append after the `AILimitSetting` enum (after line 212):

```csharp
public enum PerceptionTier
{
    Visible = 0,
    Audible = 1,
    Occluded = 2,
}
```

Source: root `SAIN\SAINEnum.cs` lines 213-225.

### A2. Change `TickInterval` in `SAIN\SAIN\Classes\Bot\BotBase.cs`

Line 70 — change from:
```csharp
public float TickInterval { get; protected set; }
```
to:
```csharp
public float TickInterval { get; set; } = 1f / 30f;
```

This allows `SAINAILimit.CheckPerceptionTier()` to dynamically set tick rate per bot (Visible=30Hz, Audible=10Hz, Occluded=5Hz).

### A3. Replace `SAIN\SAIN\Classes\Bot\SAINAILimit.cs`

Replace the 73-line original with the 270-line root version. New capabilities:
- `PerceptionTier CurrentPerceptionTier` property + `OnPerceptionTierChanged` event
- `CheckPerceptionTier()` — player-centric perception check
- `DeterminePerceptionTier()` — priority: Visible > Audible > Occluded
- `CheckPlayerCanSeeBot()` — frustum test + single raycast (amortized, 0.5s cache)
- `CheckPlayerCanHearBot()` — zero-cost checks: gunfire (<3s), sprinting (<60m), grenades (<5s)
- `GetTickIntervalForTier()` — returns 1/30, 1/10, or 1/5 seconds
- `ResetForPoolRecycle()` — resets all timers and tier state

### A4. Add `CurrentPerceptionTier` to `SAIN\SAIN\Components\BotComponent.cs`

Add after line 104 (after `CurrentAILimit` property):
```csharp
public PerceptionTier CurrentPerceptionTier
{
    get { return AILimit.CurrentPerceptionTier; }
}
```

### A5. Delete root duplicate files

```
SAIN\SAINEnum.cs                          → DELETE
SAIN\Classes\Bot\SAINAILimit.cs           → DELETE
SAIN\Classes\Bot\BotBase.cs               → DELETE
SAIN\Components\BotComponent.cs           → DELETE
```

---

## Phase B: Move new optimization files into SAIN\SAIN\

These 4 files exist ONLY at root level and must be relocated into the `SAIN\SAIN\` tree so they are compiled by the main project. Code fixes applied during the move.

### B1. SquadCombatCoordinator → `SAIN\SAIN\Layers\Combat\Squad\SquadCombatCoordinator.cs`

Move from root `SAIN\Layers\Combat\Squad\` into `SAIN\SAIN\Layers\Combat\Squad\`.

Code fixes:
- **Line 66**: `.VisibleEnemies.EnemyList` → `.VisibleEnemies` (remove `.EnemyList`)
- **Lines 112, 116, 120, 164**: `.Decision.DecisionManager.SetSquadDecision(...)` → replace with compatible API. If `SetSquadDecision` doesn't exist on the decision manager, use a property setter or add a wrapper method to `SAINDecisionClass`.

### B2. AIFrameBudgetScheduler → `SAIN\SAIN\Components\AIFrameBudgetScheduler.cs`

Move from root `SAIN\Components\` into `SAIN\SAIN\Components\`.

Code fix:
- Add singleton: `public static AIFrameBudgetScheduler Instance { get; private set; }`
- Set `Instance = this;` in constructor or `Awake()`
- Add public properties for perf monitor (see B3):
  ```csharp
  public int VisibleBotsLastFrame { get; private set; }
  public int AudibleBotsLastFrame { get; private set; }
  public int OccludedBotsLastFrame { get; private set; }
  public int OfflineSquadCount => _offlineSquads.Count;
  ```

### B3. SAINPerformanceMonitor → `SAIN\SAIN\Components\SAINPerformanceMonitor.cs`

Move from root `SAIN\Components\` into `SAIN\SAIN\Components\`.

Code fixes:
- **Line 136**: `AIFrameBudgetScheduler.Instance` — works after B2 singleton is added
- **Lines 164-191**: Replace 4 reflection-based methods with direct property access:
  - `GetVisibleBotCount` → `scheduler.VisibleBotsLastFrame`
  - `GetAudibleBotCount` → `scheduler.AudibleBotsLastFrame`
  - `GetOccludedBotCount` → `scheduler.OccludedBotsLastFrame`
  - `GetOfflineSquadCount` → `scheduler.OfflineSquadCount`

### B4. BotPoolPatches → `SAIN\SAIN\Patches\BotPoolPatches.cs`

Move from root `SAIN\Patches\` into `SAIN\SAIN\Patches\`.

No code changes. The global `Object.Destroy` Harmony prefix hook is risky (intercepts every Destroy call in Unity) but compiles as-is and is gated by `IsBotGameObject()` checks.

---

## Phase C: Fix build configuration

### C1. Root `SAIN.csproj` (current state — do not regress)

The active pattern is: **exclude stray compile roots at `OptimizedMod/SAIN/` (not under `SAIN/SAIN/`)** via explicit `Compile Remove` globs (`*.cs`, `Classes\**\*.cs`, …), while **`SAIN\SAIN\**\*.cs` remains the compiled shipping tree**.

**Do not** “remove exclusions to compile SAIN\SAIN” using outdated instructions about `Compile Remove="SAIN\**\*.cs"` — that pattern is **not** what the current csproj uses.

When adding new source files:

- Prefer placing them under `OptimizedMod/SAIN/SAIN/`.
- If you must add files under excluded root folders, add/adjust **targeted** `Compile Remove` entries — never accidentally exclude `SAIN\SAIN\`.

---

## Execution order (active work — post-appendix)

1. **D** — Visibility finalization (`GoalHumanFinal*` / `EnemyVisionClass` pipeline), including **post-remat** visibility smoke.
2. **E** — Budget domains (schedulable units beyond `BotComponent.ManualUpdate`), preserving activation/demat tick ordering.
3. **F** — Interop gates (1B): combat-pressure vs third-party layers, without breaking demat/remat latches.
4. **G** — Spawn / activation smoothing (FPS spikes, `NonSainFrameMs`) **subject to Demat / remat compatibility**.
5. **H** — Unified orchestrator RFC (multi-release).

**Appendix A–C:** historical; verify against repo before repeating any step.
