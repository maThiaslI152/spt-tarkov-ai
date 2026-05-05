# SAIN ↔ AILimit: dematerialization, pool, and scheduler fixes

> **Last updated:** 2026-05-05  
> **Audience:** maintainers and agents implementing performance / SMART-adjacent behavior.  
> **Companion docs:** [BUGFIX-AILimitSAIN-Deadlock.md](BUGFIX-AILimitSAIN-Deadlock.md) (symptom, root cause, CSV), [SMART_OFFLINE_COMBAT.md](SMART_OFFLINE_COMBAT.md) (offline slice vs full SMART), [INTEGRATION.md](INTEGRATION.md) (AILimit ↔ ecosystem).

This document is the **full change record** for the phased work: **scheduler self-heal**, **object pool + dematerialization API**, **AILimit integration**, and **proximity-based rematerialization** for parked bots.

---

## 1. Problem summary

**AILimit** historically disabled distant bots with `GameObject.SetActive(false)` and related EFT state. **SAIN** derives `PlayerComponent.IsActive` from hierarchy activity and gates `BotActive` in `SAINActivationClass`. The **AI frame budget scheduler** skipped bots with `!BotActive` *before* running `ManualUpdate`, so `CheckBotActive` never ran again after AILimit re-enabled the GameObject — a **one-way latch** (frozen loot/quest/combat behavior). Perf logs showed `SainBotsTotal` ≫ `SainBotsSampled` and `SkippedBots` could go negative due to accounting.

---

## 2. Phased delivery (status)

| Phase | Goal | Status |
|-------|------|--------|
| **1** | Break the latch; correct `TotalOnlineBots`; introduce pool + dematerialization seams; align offline combat with `demat_*` | **Shipped** |
| **2** | Route AILimit deactivate/reactivate through SAIN dematerialization + pool with legacy fallback | **Shipped** |
| **3** | Hearing + **LOS** rematerialization for `demat_*`; SMART distance/LOS demat; `auto_*` first-engagement casualties reconciled to live bots when the player nears | **Shipped (v1)** — see §5–6; spawn-from-stats / full loot still open |

---

## 3. Phase 1 — SAIN (scheduler, pool, dematerialization seam)

### 3.1 Self-heal before budget skip

- **`OptimizedMod/SAIN/SAIN/Components/AIFrameBudgetScheduler.cs`**  
  At the start of `ProcessFrame`, every non-dead bot in the incoming set calls **`BotComponent.RecheckActivation()`** so `SAINActivationClass` re-evaluates game ending, hierarchy activity / `BotActive`, and stand-by **before** the `!bot.BotActive` early path skips `ManualUpdate`.

- **`OptimizedMod/SAIN/SAIN/Classes/Bot/SAINActivationClass.cs`**  
  **`RecheckExternal()`** (or equivalent) invoked from `BotComponent.RecheckActivation` to refresh activation without requiring a full SAIN tick first.

- **`OptimizedMod/SAIN/SAIN/Components/BotComponent.cs`**  
  **`RecheckActivation()`** forwards to activation / external recheck as wired in that file.

### 3.2 CSV / accounting

- **`AIFrameBudgetScheduler`**  
  **`TotalOnlineBots`** includes bots forced to tick for combat pressure (`forceTickBots`) plus visible + audible + occluded tier counts so **`BotsSkippedThisFrame`** (`TotalOnlineBots - BotsProcessedThisFrame`) does not go negative when force-tick bots are processed outside tier round-robin.

### 3.3 Pool and dematerialization (SAIN-side API)

| Piece | Path | Responsibility |
|-------|------|----------------|
| Pool | `OptimizedMod/SAIN/SAIN/Components/BotGameObjectPool.cs` | `ReturnToPool`, `TryGetFromPool`, telemetry helpers; **`TryRemoveFromPool(GameObject)`** removes a specific instance from queues so the same raid bot is not dequeued twice after rematerialize. |
| Controller | `OptimizedMod/SAIN/SAIN/Components/BotDematerializationController.cs` | **`RequestDematerialize(BotComponent, reason)`**: build single-bot `OfflineSquad` with id prefix `demat_` + `OfflineSquadWorldSync.BuildDematerializeSquadForBot`, register on scheduler, `ReturnToPool`, set **`EBotState.NonActive`**, track profile in `_byProfileId` with **`Pooled`** flag and **`DematParkReason`** holders (AILimit vs SMART merge on repeat request). **`TryReleaseParkReason`**: partial vs full remat when the last holder clears. **`RequestRematerialize`**: full clear + unregister + pool pull + activate. **`IsDematerialized(profileId)`**. |
| Manager wiring | `OptimizedMod/SAIN/SAIN/Components/BotManagerComponent.cs` | **`Pool`** and **`Dematerialization`** are **new instances** each **`Activate`** (new raid / manager). **`Dispose`**: **`Dematerialization.ResetForNewRaid()`** and **`Pool.ClearPool()`**. |
| Offline combat | `OptimizedMod/SAIN/SAIN/Components/AIFrameBudgetScheduler.cs` | **`IsAutoManagedSquad`**: ids starting with `auto_` **or** `demat_` skip **`ApplyOfflineCasualties`** member trimming (live or parked rows must not be gutted by statistical casualties). |

### 3.4 Offline squad id prefix

- **`OptimizedMod/SAIN/SAIN/Components/OfflineSquadWorldSync.cs`**  
  Constant **`DematerializeSquadIdPrefix`** (`demat_`) used when building the offline row for a parked bot.

---

## 4. Phase 2 — AILimit (compile + runtime integration)

| Change | Path |
|--------|------|
| Project reference to SAIN | `OptimizedMod/AILimit/AILimit.csproj` — `<ProjectReference Include="..\SAIN\SAIN.csproj" />` |
| Load order | `OptimizedMod/AILimit/Plugin.cs` — `[BepInDependency("me.sol.sain", BepInDependency.DependencyFlags.SoftDependency)]` |
| Deactivate | `OptimizedMod/AILimit/Component.cs` — After existing stand-by / `GoalEnemy` prep, **`TrySainDematerialize(BotOwner)`** → `BotManagerComponent.Instance.Dematerialization.RequestDematerialize`; on failure, legacy **`SetActive(false)`** + **`EBotState.NonActive`**. |
| Activate | Same file — **`TrySainRematerialize`** first when profile is tracked as dematerialized; else legacy **`SetActive(true)`** + stand-by / **`EBotState.Active`**. |
| Disabled list path | **`IsSainDematerializedForOwner`**: when true, skip extra legacy deactivate handling that would double-park or fight SAIN state. |
| Plugin toggled off | Re-enable path tries **`TrySainRematerialize`** per bot before legacy activation. |

**Helpers (same component):** `TrySainDematerialize`, `TrySainRematerialize`, `IsSainDematerializedForOwner` — all use `BotManagerComponent.Instance`, `BotComponent` on the `BotOwner`’s GameObject, and **`Dematerialization`**.

**Without SAIN:** AILimit behaves as before (no `BotComponent` / no manager → fallback).

---

## 5. Phase 3 — Rematerialization (`demat_*`) + SMART demat + `auto_*` reconcile

| Piece | Path | Behavior |
|-------|------|----------|
| Proximity API | `OptimizedMod/SAIN/SAIN/Components/OfflineSquadMaterialization.cs` | **`TryBeginMaterialize`**: only **`demat_*`** squads; human alive non-AI; distance ≤ **hearing × 1.5**; **`TryReleaseParkReason(..., DematParkReason.SmartLos)`** when SMART held the bot (AILimit may still hold); full remat when no holders → **`RequestRematerialize`**. |
| Proximity drive | **`TryRematerializeDematSquadsNearHumans`** | Snapshot **`OfflineSquads`**, throttle **0.25 s**; nominal hearing from preset **`AILimit.MaxHearingRanges[Far]`** (~100 m). |
| LOS drive | **`TryRematerializeDematSquadsLosFromHumans`** | Throttled pass: any human’s main camera sees squad anchor → remat path above; **`SmartRematLos`** telemetry. |
| SMART demat | `Components/SmartDematSystems.cs` | **`SmartDematerializeGate.TryApply`**: ~180 m, ~8 s no LOS, exclusions + combat pressure; **`SainDematPolicy`** cooldown after full remat. |
| `auto_*` reconcile | `Components/AIFrameBudgetScheduler.ApplyOfflineCasualties`, `Components/AutoSquadMaterialization.cs` | First **auto vs auto** offline pair per raid stores **`PendingWorldCasualties`**; when main player is **near** squad center and under **`MaxBots`**, apply kills to matching **live** SAIN bots (not new spawns). |

**Out of scope (follow-ups):** `BotCreator` / ABPS **new** spawns from stats-only rows, full corpse/loot from resolver, Harmony on vanilla **`OnBotRemoved`** for non-SAIN paths.

---

## 6. Runtime order (raid tick)

1. `BotManagerComponent.ManualUpdate` runs subsystems.  
2. **`OfflineSquadWorldSync.TrySync`** — periodic `auto_*` registration refresh.  
3. **`OfflineSquadMaterialization.TryRematerializeDematSquadsNearHumans`** — throttled **proximity** `demat_*` remat.  
4. **`OfflineSquadMaterialization.TryRematerializeDematSquadsLosFromHumans`** — throttled **LOS** `demat_*` remat.  
5. **`AutoSquadMaterialization.TryTick`** — cap-gated `auto_*` **pending casualty** application to live bots.  
6. **`SmartDematerializeGate.TryApply`** — SMART dematerialize pass.  
7. **`BudgetScheduler.ProcessFrame`** — offline combat (≥2 hostile squads, ≤1 Hz); **per-bot `RecheckActivation`**; tiered processing under ms cap.

**Rule:** remat passes run **before** SMART demat so bots eligible for proximity/LOS return are not dematerialized in the same ordering pass without cooldown.

---

## 7. Build and deploy

```bash
dotnet build OptimizedMod/SAIN/SAIN.csproj -c Release
dotnet build OptimizedMod/AILimit/AILimit.csproj -c Release
```

Deploy outputs per [MOD_BUILD_AND_DEPLOY.md](MOD_BUILD_AND_DEPLOY.md) (typically `SAIN.dll`, `dvize.AILimit.dll` under BepInEx plugins for your layout).

---

## 8. Verification (manual)

1. Raid with **SAIN + AILimit**; enable **SAINPerfLog** diagnostics when investigating.  
2. **`sain_perf_*.csv`:** `SkippedBots` ≥ 0; `TotalOnline` sane vs bots in sim.  
3. **`sain_bigbrain_*.csv`:** `SainBotsSampled` tracks expectations when bots are in range (no permanent sampled ≪ total while bots exist and should tick).  
4. Walk toward a **distant-parked** bot (dematerialized): should **rematerialize** within ~**1.5 × Far hearing** of last recorded center, even if still outside AILimit’s top-`N` by sort order.

---

## 9. Other documentation touched

| Doc | Role |
|-----|------|
| [BUGFIX-AILimitSAIN-Deadlock.md](BUGFIX-AILimitSAIN-Deadlock.md) | Symptom, root cause, CSV example, short per-phase bullets, roadmap table. |
| [SMART_OFFLINE_COMBAT.md](SMART_OFFLINE_COMBAT.md) | Implemented vs not implemented table; P2 materialization / dematerialization rows. |
| [INTEGRATION.md](INTEGRATION.md) | AILimit dependency chain, mechanism table, integration matrix rows for SAIN dematerialize path. |
| [INDEX.md](../INDEX.md) | Repo map rows for AILimit behavior and cross-links. |

---

## 10. Open follow-ups

- **`OfflineSquadMaterialization` for `auto_*`:** spawn or hand off from **`OfflineCombatResult`** when the player enters a zone.  
- **Vanilla despawn / pool:** optional Harmony so removals align with pool policy.  
- **ABPS / cap reconciliation** with dematerialized bot counts if spawn limits are hit.
