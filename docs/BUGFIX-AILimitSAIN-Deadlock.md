# BUGFIX: AILimit ↔ SAIN scheduler deadlock (frozen bots on quest / loot)

> **Last updated:** 2026-05-06 (root-cause wording: dematerialize vs legacy `SetActive`)  
> **Related:** [SAIN_AILIMIT_DEMATERIALIZATION.md](SAIN_AILIMIT_DEMATERIALIZATION.md) (**full change record**), [SMART_OFFLINE_COMBAT.md](SMART_OFFLINE_COMBAT.md), [SAIN_PERFLOG.md](SAIN_PERFLOG.md), `OptimizedMod/AILimit/Component.cs`, `OptimizedMod/SAIN/SAIN/Components/AIFrameBudgetScheduler.cs`

## Symptom

- Bots ignore gunfire, footsteps, and nearby fights; they stay on **Looting**, **BotMind_Questing**, **PatrolFollower**, or **StandBy** while SAIN combat layers never win.
- Lighthouse **exUsec / Rogue** squads may keep looting and never bootstrap defense (`ShouldBootstrapRogueDefenseCombatLayer`) because `bot.Decision` never ticks.
- Perf CSV shows **`SainBotsTotal` ≫ `SainBotsSampled`** in `sain_bigbrain_*.csv`, and **`TotalOnline`** collapsing in `sain_perf_*.csv` while bots still exist in `SAINBots`.

## Root cause

**Latch (still the core bug story):** When the bot’s player `GameObject` is inactive or otherwise fails `PlayerComponent.IsActive`, **SAIN** marks the bot inactive and the scheduler can skip `ManualUpdate` — so **SAINActivationClass** never gets another chance to clear the latch after the world wakes the bot back up.

1. **AILimit** parks distant bots (`OptimizedMod/AILimit/Component.cs`):
   - **Preferred (SAIN loaded):** `BotDematerializationController.RequestDematerialize` / `RequestRematerialize` (see Phase 2) — avoids hard `SetActive(false)` when the pool path succeeds.
   - **Legacy fallback:** `player.gameObject.SetActive(false)`, `owner.BotState = EBotState.NonActive`, clears `owner.Memory.GoalEnemy` when dematerialize cannot run (no SAIN, missing `BotComponent`, pool full, etc.).
2. **PlayerComponent** `IsActive` follows `gameObject.activeInHierarchy` (`PersonActiveClass.CheckActive`).
3. **SAINActivationClass.CheckBotActive** sets `BotActive` false when `!PlayerComponent.IsActive` (`SAINActivationClass.cs`).
4. **AIFrameBudgetScheduler** used to skip bots with `!bot.BotActive` before any path could re-run activation checks after AILimit turned the `GameObject` back on.

Result (historical): a **one-way latch** — bots wake in EFT but stay frozen in SAIN until **Phase 1** `RecheckActivation()` runs every frame before tiering/skips.

## CSV evidence (example raid)

Lighthouse session `a5cd34b2` (`BepInEx/LogOutput/sain_perf/`):

- `sain_bigbrain_*.csv`: at ~120 s, `SainBotsTotal=16` but `SainBotsSampled=5`; later `SainBotsSampled=0` with `SainBotsTotal=15`.
- `sain_perf_*.csv`: `TotalOnline` drops 16 → 5 in a few seconds; `SkippedBots` can go **negative** when `forceTickBots` are counted in `BotsProcessedThisFrame` but excluded from `TotalOnlineBots` (fixed in Phase 1).

## Phase 1 fix (shipped in this repo)

1. **Self-heal:** At the start of each `AIFrameBudgetScheduler.ProcessFrame`, every non-dead bot calls `BotComponent.RecheckActivation()` → `SAINActivationClass.RecheckExternal()` (`CheckGameEnding`, `CheckBotActive`, `CheckStandBy`) before the `!bot.BotActive` skip.
2. **CSV accounting:** `TotalOnlineBots = forceTickBots.Count + visible + audible + occluded` so `BotsSkippedThisFrame` is never negative.
3. **SMART seams:**
   - `BotManagerComponent.Pool` — live `BotGameObjectPool` instance (telemetry + AILimit Phase 2).
   - `BotManagerComponent.Dematerialization` — `BotDematerializationController` with `RequestDematerialize` / `RequestRematerialize` / `IsDematerialized`; single-bot `OfflineSquad` id prefix `demat_`.
   - Offline combat casualty trim skips `demat_*` squads the same way as `auto_*` (`IsAutoManagedSquad` extended).
4. **Pool consistency:** `BotGameObjectPool.TryRemoveFromPool` removes a GameObject from pool queues on rematerialize so the same raid bot is not offered twice by `TryGetFromPool`. `RequestDematerialize` sets `EBotState.NonActive` after parking; `RequestRematerialize` calls `RegisterActiveBot` for perf telemetry.

## Phase 2 fix (AILimit → SAIN, shipped)

- [`OptimizedMod/AILimit/Component.cs`](OptimizedMod/AILimit/Component.cs): after the same stand-by / `GoalEnemy` prep as before, **activate** path prefers `Dematerialization.RequestRematerialize` when that profile was parked by SAIN; **deactivate** path prefers `RequestDematerialize` and falls back to legacy `SetActive(false)` if SAIN is absent, `BotComponent` is missing, or the pool is full.
- [`OptimizedMod/AILimit/AILimit.csproj`](OptimizedMod/AILimit/AILimit.csproj): `ProjectReference` to `SAIN.csproj` (compile-time types).
- [`OptimizedMod/AILimit/Plugin.cs`](OptimizedMod/AILimit/Plugin.cs): soft `[BepInDependency("me.sol.sain")]` so BepInEx loads SAIN first when both are installed.

## Phase 3 (demat materialization, partial)

- [`OfflineSquadMaterialization.cs`](../OptimizedMod/SAIN/SAIN/Components/OfflineSquadMaterialization.cs): `TryRematerializeDematSquadsNearHumans` from `BotManagerComponent.ManualUpdate` (before `ProcessFrame`) rematerializes parked AILimit bots when any human enters **1.5×** the preset Far `AILimit.MaxHearingRanges` distance of the offline squad center, so bots are not stranded outside AILimit’s top-`N` distance ordering.

## Phased roadmap (user-selected S4 phased)

| Phase | Scope | Status |
|-------|--------|--------|
| **1** | Self-heal + `TotalOnlineBots` fix + pool instance + `BotDematerializationController` API + docs | Shipped |
| **2** | AILimit calls `Dematerialization.RequestDematerialize` / `RequestRematerialize` with legacy fallback; `TryRemoveFromPool` on rematerialize | Shipped |
| **3** | `OfflineSquadMaterialization.TryBeginMaterialize` for `demat_*` + `TryRematerializeDematSquadsNearHumans` (1.5× preset Far hearing, 4 Hz); `auto_*` spawn-from-stats / casualty handoff still open | Partial (demat path shipped) |

## Verification

1. Deploy rebuilt `SAIN.dll`, run Lighthouse with SAINPerfLog enabled.
2. Confirm `sain_perf_*.csv`: `SkippedBots` ≥ 0; `TotalOnline` tracks sampled active bots more closely after AILimit cycles.
3. Confirm `sain_bigbrain_*.csv`: `SainBotsSampled` tracks `SainBotsTotal` when bots are in range (no permanent `sampled=0` while total > 0).
4. Observe exUsec squads: after player proximity + gunfire, SAIN combat layers should appear in the layer histogram again.

## Build

```bash
dotnet build OptimizedMod/SAIN/SAIN.csproj -c Release
```
