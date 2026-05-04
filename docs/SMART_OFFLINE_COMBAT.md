# SMART-style offline combat — status and roadmap

> **Last updated:** 2026-05-05  
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
| **Scheduler wiring** | `Components/BotManagerComponent.cs` | `OfflineSquadWorldSync.ResetForNewRaid()` after scheduler construction; `TrySync` before `BudgetScheduler.ProcessFrame`. |
| **Bot object pool (instance)** | `Components/BotManagerComponent.cs`, `Components/BotGameObjectPool.cs` | `BotManagerComponent.Pool` created each raid; `RaidPerfCsvLogger` can read pooled counts via reflection. Phase 2 routes AILimit through pool + dematerialization. |
| **Dematerialize API** | `Components/BotDematerializationController.cs` | `RequestDematerialize` / `RequestRematerialize` / `IsDematerialized`; single-bot `OfflineSquad` id prefix `demat_`. **Caller:** `OptimizedMod/AILimit/Component.cs` (distance deactivate / reactivate + plugin-off re-enable). See [BUGFIX-AILimitSAIN-Deadlock.md](BUGFIX-AILimitSAIN-Deadlock.md). |
| **Auto squad casualties** | `AIFrameBudgetScheduler.ApplyOfflineCasualties` | Skips trimming `Members` when `SquadId` starts with `auto_` (stats are refreshed on next sync from live bots). |
| **Combat audio** | `Components/CombatAudioSpoofer.cs` | Procedural **`AudioClip`** fallback + `AudioSource.PlayClipAtPoint` with distance attenuation; schedules from `EstimatedCombatDuration` (not gated on `TotalCasualties` alone). |
| **Materialization (demat path)** | `Components/OfflineSquadMaterialization.cs`, `BotManagerComponent.ManualUpdate` | `TryBeginMaterialize` rematerializes `demat_*` when a human is within **1.5×** nominal Far hearing (preset `AILimit.MaxHearingRanges`); `TryRematerializeDematSquadsNearHumans` runs before the budget frame (~4 Hz). `auto_*` / spawn-from-stats not implemented. |

**OptimizationCore** (`OptimizedMod/OptimizationCore/`) remains a **reference** implementation; the **runtime** stack is the SAIN copies above.

---

## What is not implemented (full SMART)

1. **`auto_*` stats-only replacement** — Occluded AI-vs-AI pairs still run as **live** bots alongside statistical Phase 0; sync does not despawn them into stats-only entities. **Contrast:** **`demat_*`** distance parking via AILimit → `BotDematerializationController` + `BotGameObjectPool` **is shipped** ([SAIN_AILIMIT_DEMATERIALIZATION.md](SAIN_AILIMIT_DEMATERIALIZATION.md)).  
2. **`auto_*` offline → online spawn** — No spawn or state restore from `OfflineCombatResult` for distant statistical fights. **`demat_*`** rematerialize (hearing-radius gate → `RequestRematerialize`) **is shipped** in `OfflineSquadMaterialization.cs`.  
3. **EFT-native audio** — No verified **`BetterAudio`** integration (API varies by game build); procedural Unity one-shots are a stand-in, not full EFT gunfire propagation / bot hearing.  
4. **World consistency** — Statistical casualties for `auto_*` squads do not kill real bots or drop loot; full SMART would need an explicit reconciliation model.  
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

---

## Build

```text
dotnet build OptimizedMod/SAIN/SAIN.csproj -c Release
```

Requires MSBuild properties **`SPTCore`**, **`SPTManaged`**, **`SPTSPT`**, **`SPTRoot`** pointing at the target SPT install (see other mod docs).
