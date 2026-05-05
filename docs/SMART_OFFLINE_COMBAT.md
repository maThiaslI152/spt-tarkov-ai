# SMART-style offline combat — status and roadmap

> **Last updated:** 2026-05-05 (Phase 1+2 SMART demat / remat / auto reconcile)  
> **Related:** [SAIN_AILIMIT_DEMATERIALIZATION.md](SAIN_AILIMIT_DEMATERIALIZATION.md) (AILimit + pool + `demat_*` materialization), [PERFORMANCE_ARCHITECTURE.md](PERFORMANCE_ARCHITECTURE.md) §2.5, [PERFORMANCE_PLAN.md](PERFORMANCE_PLAN.md) Phase 2.5, [PROGRESS.md](PROGRESS.md)

## Terminology

| Term | Meaning in this repo |
|------|---------------------|
| **SMART (design target)** | Distant AI-vs-AI resolved **only** by statistics (no full per-frame simulation), **spoofed** world-consistent audio, then **materialized** real bots/corpses when the player can perceive the zone (see STALKER Analogy / PERFORMANCE_PLAN). |
| **Current slice** | Live bots still run full AI; **additional** `OfflineSquad` registrations drive statistical rolls + cheap audio for qualifying distant engagements. |

---

## What is implemented (shipping under `OptimizedMod/SAIN/SAIN/`)

| Piece | File(s) | Behavior |
|-------|---------|------------|
| **Budget Phase 0** | `Components/AIFrameBudgetScheduler.cs` | ≤1 Hz offline resolution when ≥2 hostile `OfflineSquad`s exist; pairs within **400 m**; optional `[SAIN DIAG] OfflineCombat:` when **SAINPerfLog** diagnostic logging is on. |
| **Statistical resolver** | `Components/OfflineCombatResolver.cs` | Power roll, casualties, duration, weapon id lists for audio. |
| **Auto squad registration** | `Components/OfflineSquadWorldSync.cs` | Every **5 s**: clears `auto_*` squads, finds closest **Occluded** pair with active **AI** `GoalEnemy`, **≥70 m** from humans, **≤400 m** apart, hostile via `BotsGroup.IsPlayerEnemy`; builds squads from each bot’s **`BotsGroup`** (occluded members). Calls `RegisterOfflineSquad`. |
| **Scheduler wiring** | `Components/BotManagerComponent.cs` | `OfflineSquadWorldSync.ResetForNewRaid()` after scheduler construction; **`ManualUpdate` order:** `TrySync` → proximity `demat_*` remat → LOS `demat_*` remat → `AutoSquadMaterialization.TryTick` → `SmartDematerializeGate.TryApply` → `ProcessFrame`. |
| **Bot object pool (instance)** | `Components/BotManagerComponent.cs`, `Components/BotGameObjectPool.cs` | `BotManagerComponent.Pool` created each raid; `RaidPerfCsvLogger` can read pooled counts via reflection. Phase 2 routes AILimit through pool + dematerialization. |
| **Dematerialize API** | `Components/BotDematerializationController.cs` | `RequestDematerialize` / `RequestRematerialize` / `IsDematerialized`; single-bot `OfflineSquad` id prefix `demat_`. **Caller:** `OptimizedMod/AILimit/Component.cs` (distance deactivate / reactivate + plugin-off re-enable). See [BUGFIX-AILimitSAIN-Deadlock.md](BUGFIX-AILimitSAIN-Deadlock.md). |
| **Auto squad casualties** | `AIFrameBudgetScheduler.ApplyOfflineCasualties` | Skips trimming `Members` when `SquadId` starts with `auto_` (stats are refreshed on next sync from live bots). |
| **Combat audio** | `Components/CombatAudioSpoofer.cs` | Procedural **`AudioClip`** fallback + `AudioSource.PlayClipAtPoint` with distance attenuation; schedules from `EstimatedCombatDuration` (not gated on `TotalCasualties` alone). |
| **Materialization (demat path)** | `Components/OfflineSquadMaterialization.cs`, `BotManagerComponent.ManualUpdate` | `TryBeginMaterialize` / `TryRematerializeDematSquadsNearHumans` rematerialize `demat_*` on **hearing** (1.5× nominal Far preset); `TryRematerializeDematSquadsLosFromHumans` adds **main-camera LOS** regain (throttled). Partial release uses `BotDematerializationController.TryReleaseParkReason` so AILimit and SMART can co-hold parking. |
| **SMART dematerialize gate** | `Components/SmartDematSystems.cs` (`SmartDematerializeGate`, `SainDematPolicy`, telemetry) | ~**180 m** from nearest human, ~**8 s** sustained no main-camera LOS, guards (strict spawn exclusions, `SAINExternal` combat pressure, already dematerialized, remat→demat cooldown). Calls `RequestDematerialize(..., "smart-los-distance")`. |
| **Policy + pool hardening** | `Components/BotDematerializationController.cs`, `Components/BotGameObjectPool.cs`, `Patches/BotPoolPatches.cs` | Multi-holder `DematParkReason` (AILimit vs SMART); `TryReleaseParkReason` / `RequestRematerialize`; idempotent `ReturnToPool` + Destroy prefix skips pooled / dematerialized instances. |
| **`auto_*` world reconcile (Phase 2 v1)** | `Components/AIFrameBudgetScheduler.cs`, `Components/AutoSquadMaterialization.cs` | First **auto vs auto** offline resolution per unordered pair records `PendingWorldCasualties` on each `OfflineSquad`. When the **main player** is within **120 m** of the squad center and `MaxBots` allows, matching **live** bots (by profile id) are killed via `ActiveHealthController.IsAlive = false` until pending is satisfied; excluded wild types skipped. **Not** a new BotCreator spawn. |
| **Pool telemetry probe** | `Components/BotSpawnPoolBridge.cs`, `BotManagerComponent.Activate` | One `TryGetFromPool` probe per raid so pool hit/miss counters can move without wiring full spawn. |

**OptimizationCore** (`OptimizedMod/OptimizationCore/`) remains a **reference** implementation; the **runtime** stack is the SAIN copies above.

---

## What is not implemented (full SMART)

1. **`auto_*` stats-only replacement** — Occluded AI-vs-AI pairs still run as **live** bots alongside statistical Phase 0; sync does not despawn them into stats-only entities. **Contrast:** **`demat_*`** distance parking via AILimit + SMART → `BotDematerializationController` + `BotGameObjectPool` **is shipped** ([SAIN_AILIMIT_DEMATERIALIZATION.md](SAIN_AILIMIT_DEMATERIALIZATION.md)).  
2. **`auto_*` new spawns from stats** — Phase 2 v1 **reconciles** the first auto-vs-auto roll onto **existing** live bots when the player nears; there is still **no** `BotCreator` / ABPS / MoreBotsAPI spawn of shells from `OfflineCombatResult`. Partial HP-only damage (without kill) is not implemented.  
3. **EFT-native audio** — No verified **`BetterAudio`** integration (API varies by game build); procedural Unity one-shots are a stand-in, not full EFT gunfire propagation / bot hearing.  
4. **World consistency** — No corpse/loot pipeline from offline rolls; kills use minimal health latch — validate on target SPT build.  
5. **Broader registration sources** — No map metadata / BotZone-only pairing yet; sync is heuristic on current raid bots.

---

## Future development (priority sketch)

| Priority | Work item | Notes |
|----------|-----------|--------|
| P0 | **In-raid validation** | Confirm registrations fire on real maps; tune distances, hostility checks, and sync interval. |
| P1 | **BetterAudio + real clips** | Confirm `BetterAudio` overloads against target `Assembly-CSharp`; or ship a small WAV/bundle and load from disk. |
| P1 | **Preset toggle** | e.g. `PerformanceSettings`: enable/disable auto offline squads, min human distance, max pairs. |
| P2 | **Materialization** | **Demat:** `TryBeginMaterialize` + proximity pass (shipped). **Remaining:** early spawn / `UnregisterOfflineSquad` / state handoff from `OfflineCombatResult` for `auto_*`. |
| P2 | **Dematerialization strategy** | **Shipped (AILimit):** calls `BotDematerializationController` + `BotGameObjectPool` with legacy fallback. **Open:** reconcile with ABPS caps and spawn/despawn Harmony for vanilla waves. |
| P3 | **Corpse / loot / narrative** | Optional: apply resolver output to world state or discard purely as immersion. |

---

## How to observe behavior

1. Install **SAINPerfLog**, enable **Diagnostic Logging** (F12), run a raid with distant AI combat.  
2. Watch **LogOutput** for `[SAIN DIAG] OfflineCombat:` when Phase 0 resolves.  
3. Listen for short **distant pops** (procedural fallback) near the combat zone center when the player is within attenuation range.  
4. Perf CSV: `SmartDemat*Delta`, `AutoSpawnAttemptsDelta` / `AutoSpawnFailuresDelta`, **`AutoMatAppliedDelta`** (successful auto casualty batches). Optional spawn-event CSV: `AutoMat`, `SmartRematLos`, etc.

---

## Build

```text
dotnet build OptimizedMod/SAIN/SAIN.csproj -c Release
```

Requires MSBuild properties **`SPTCore`**, **`SPTManaged`**, **`SPTSPT`**, **`SPTRoot`** pointing at the target SPT install (see other mod docs).
