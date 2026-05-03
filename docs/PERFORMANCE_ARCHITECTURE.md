# SAIN Performance Optimization — Architecture & Usage Guide

> **Code location:** All source code lives in `OptimizedMod/`. Paths in this document are relative to `OptimizedMod/` unless noted otherwise.

**Agents:** [INDEX.md](../INDEX.md) · [AGENTS.md](AGENTS.md).

## Overview

This document describes the multi-phase performance optimization applied to SAIN (Solarint's AI Modifications) for SPT (Single Player Tarkov). The optimization spans four phases across all forked mods (SAIN, BigBrain, LootingBots, Waypoints, SPT-AILimit, ABPS) with the target of running Lighthouse with 29+ bots at stable 60 FPS.

### Design Principle: Player-Centric, Fake It When Unseen

The core philosophy flips Tarkov's bot-centric AI ("what does this bot need to do?") to player-centric ("what does the player need to experience?"). A bot behind three walls and two floors doesn't need full tactical AI — it just needs to exist convincingly in case the player looks. This cascades through every phase.

**Roadmap and gap analysis (preset budget, visible-vs-off-screen LOD, checklist):** see [AI_BUDGET_LOD_PLAN.md](AI_BUDGET_LOD_PLAN.md).

---

## Architecture: How the Systems Fit Together

```
Each Frame (16.7ms at 60 FPS)
│
├── BotManagerComponent.ManualUpdate()
│   │
│   └── AIFrameBudgetScheduler.ProcessFrame(allBots)
│       │
│       ├── Phase 0: ResolveOfflineSquadCombat()  [≤1 Hz when ≥2 hostile offline squads]
│       │   └── CombatAudioSpoofer: spoofed gunfire audio
│       │
│       ├── Visible tier: ProcessTierRoundRobin (budget slice ≈45% of MaxAIBudgetMs first)
│       ├── Audible tier: ProcessTierRoundRobin (cumulative cap ≈88% before Occluded)
│       └── Occluded tier: ProcessTierRoundRobin (remaining budget; per-tier resume index)
│           └── Hard cap MaxAiBudgetMs/frame (preset **Max AI Frame Budget (ms)**, default 2) → STOP; unfinished tiers resume next frame (fair round-robin)
│
└── Per-frame AI ms cap — tuned via SAIN preset Global → General → Performance (synced to perf CSV / F12)
```

### Performance logging + F12 (2026-05-03, updated)

**`SAINPerfLog`** (`me.sol.sain.perflog`) owns per-raid CSV under `BepInEx/LogOutput/sain_perf/` and the **F12** read-only scheduler lines (**SAINPerfLog (F12)**). Rows are written by `RaidPerfCsvLogger` from the live `AIFrameBudgetScheduler` so short stalls are easier to diagnose:

- CSV includes **`BudgetExhaustedNow`** (per-row instant flag) alongside rolling exhaustion percent.
- CSV includes **`BudgetHeadroomMs`** and **`ProcessedBots` / `SkippedBots`** (and V/A/O/offline counts aligned with the scheduler).
- Raid lifecycle opens/closes writers deterministically (no fixed-path overwrite across raids).
- F12 exposes the same rolling FPS + budget/bot counters the logger samples (plus active CSV paths while in-raid).

This helps distinguish “low average budget use” from brief spikes where skipped updates correlate with visible jitter.

### Key Components

| Component | File | Role |
|---|---|---|
| **AIFrameBudgetScheduler** | `SAIN/SAIN/Components/AIFrameBudgetScheduler.cs` | 2ms hard cap, Visible/Audible/Occluded tiers, **time-sliced round-robin** per tier, offline combat dispatch |
| **SAINAILimit** | `SAIN/SAIN/Classes/Bot/SAINAILimit.cs` | Perception-tier assignment (Visible/Audible/Occluded) |
| **BotComponent** | `SAIN/SAIN/Components/BotComponent.cs` | Per-bot ManualUpdate, ticks only relevant classes per tier |
| **BotBase** | `SAIN/SAIN/Classes/Bot/BotBase.cs` | `ShallTick()` gates individual class ticks by interval |
| **VisionRaycastJob** | `SAIN/SAIN/Classes/BotManager/Jobs/VisionRaycastJob.cs` | Raycast count reduced by LOD tier |
| **SquadCombatCoordinator** | `SAIN/SAIN/Layers/Combat/Squad/SquadCombatCoordinator.cs` | Squad leader hook from `CombatSquadLayer` assigns targets / flanking / suppression |
| **OfflineCombatResolver** | `SAIN/SAIN/Components/OfflineCombatResolver.cs` | Statistical AI-vs-AI combat (budgeted dispatch) |
| **CombatAudioSpoofer** | `SAIN/SAIN/Components/CombatAudioSpoofer.cs` | Fake gunfire for offline combat |
| **BotGameObjectPool** | `SAIN/SAIN/Components/BotGameObjectPool.cs` | Recycle bot GameObjects instead of destroy/create |
| **BotPoolPatches** | `SAIN/SAIN/Patches/BotPoolPatches.cs` | Harmony patches intercepting Destroy/Spawn |

---

## Phase 1: Mechanical Fixes (Lowest Effort, Immediate Impact)

### 1.1 TickInterval Fix

**File:** `SAIN/SAIN/Classes/Bot/BotBase.cs`  
**Change:** Line 70 — `TickInterval` default changed from `0f` to `1f / 30f`

```csharp
// Before
public float TickInterval { get; protected set; }  // defaults to 0f → ticks every frame

// After
public float TickInterval { get; set; } = 1f / 30f;  // defaults to 30Hz (~33ms interval)
```

**Effect:** All ~16 bot classes that use `ShallTick()` automatically throttle from per-frame to 30Hz. The `ShallTick()` method (line 47) returns `true` only when `LastTickTime + TickInterval < CurrentTime`.

**Override example:** The SAINAILimit class overrides `TickInterval` per perception tier:
- Visible: `1f / 30f` (30Hz)
- Audible: `1f / 10f` (10Hz)
- Occluded: `1f / 5f` (5Hz)

### 1.2 Coroutine Job Throttling

**File:** `SAIN/SAIN/Classes/BotManager/Jobs/VisionRaycastJob.cs`  
**Change:** Line 128 — `yield return null` → `yield return wait` in `UpdateEFTVision()`

The coroutine loop created a `WaitForSeconds wait = new(VisionUpdateInterval)` at line 113 but then did `yield return null` at the end of the loop instead of `yield return wait`. This caused the coroutine to resume every frame (via null) instead of respecting the configured interval.

**How to verify:** After this fix, bots in the VeryFar/Narnia AILimit tier skip the EFT look sensor update entirely (line 123 check). The EFT look sensor for remaining bots updates at the configured `LookUpdateFrequency` (Performance mode default: 30Hz).

### 1.3 Performance Mode Default

**File:** `SAIN/SAIN/Preset/GlobalSettings/Categories/General/PerformanceSettings.cs`  
**Change:** Line 12 — `PerformanceMode = true`

This is the master toggle. When `true`, all performance sub-settings activate:
- `VisionRaycastFrequency` (default 30Hz) — vision raycast update rate
- `LookUpdateFrequency` (default 30Hz) — EFT look sensor update rate
- `CoverFindFrequency` (default 10Hz) — cover search rate in combat
- `MaxRaycastsPerEnemy` (default 3) — raycast types per body part (1=LoSo, 2=LoSo+Vision, 3=LoSo+Vision+Shoot)
- `FarBotCpuReduction` (0.5), `VeryFarBotCpuReduction` (0.25), `NarniaBotCpuReduction` (0.0) — per-tier vision cost multipliers

**Configuration location:** These settings are stored in the preset JSON files under `Presets/{presetName}/GlobalSettings.json`. Users can also adjust them via the SAIN in-game editor (F6).

---

## Phase 2: Structural Improvements

### 2.1 Vision Raycast LOD Reduction

**File:** `SAIN/SAIN/Classes/BotManager/Jobs/VisionRaycastJob.cs`  
**Change:** Lines 171-175 — Far tier now uses 1 raycast per body part instead of 2

```csharp
// Before
if (enemy.IsAI && enemy.Bot.CurrentAILimit >= AILimitSetting.Far)
    effectiveChecks = Mathf.Min(raycastChecks, 2);  // 2 raycasts

// After
if (enemy.IsAI && enemy.Bot.CurrentAILimit >= AILimitSetting.Far)
    effectiveChecks = 1;  // Just LineOfSight
```

**Raycast types explained:**
| effectiveChecks | Raycasts per part | What's checked |
|---|---|---|
| 1 | 1 | LineOfSight only (eye → body part) |
| 2 | 2 | LineOfSight + Vision |
| 3 | 3 | LineOfSight + Vision + Shoot |

Far-tier bots get only LineOfSight. VeryFar/Narnia bots are skipped entirely (lines 168-170). Enemies beyond 150m get only 1 body part checked (center mass).

### 2.2 AI Frame Budget Scheduler

**File:** `SAIN/SAIN/Components/AIFrameBudgetScheduler.cs`  
**Wired into:** `SAIN/SAIN/Components/BotManagerComponent` (`BudgetScheduler.ProcessFrame()` owns the bot loop).

This is the **architectural foundation** — everything else depends on it.

**How it works (matches implementation):**

Bots are bucketed by `BotComponent.CurrentPerceptionTier`, combat-sorted inside each list, then **`ProcessTierRoundRobin`** walks each tier from a **resume index** until that tier’s phase budget or the global **2ms** cap is exhausted.

```csharp
public void ProcessFrame(HashSet<BotComponent> allBots, float currentTime, float deltaTime)
{
    _frameTimer.Restart();
    // Phase 0: offline squad combat (≤1 Hz when squads registered)
    // Build visibleBots / audibleBots / occludedBots …
    double visiblePhaseStopMs = MaxAIBudgetMs * 0.45;   // VisibleTierBudgetFraction
    double audiblePhaseStopMs = MaxAIBudgetMs * 0.88;   // AudibleTierCumulativeFraction

    ProcessTierRoundRobin(visibleBots, ref _resumeVisibleIndex, visiblePhaseStopMs, currentTime, deltaTime);
    if (_frameTimer.Elapsed.TotalMilliseconds < MaxAIBudgetMs)
        ProcessTierRoundRobin(audibleBots, ref _resumeAudibleIndex, audiblePhaseStopMs, currentTime, deltaTime);
    if (_frameTimer.Elapsed.TotalMilliseconds < MaxAIBudgetMs)
        ProcessTierRoundRobin(occludedBots, ref _resumeOccludedIndex, MaxAIBudgetMs, currentTime, deltaTime);
}
```

**Key guarantees:**
- AI processing NEVER exceeds **MaxAIBudgetMs** (default **2ms**) per frame.
- Visible tier is processed first with its **own phase ceiling** (~45% of the budget) so Audible bots still get ticks when many Visible bots exist (prevents starvation loops).
- Audible then Occluded tiers run under cumulative caps; unfinished work **round-robins** across frames via `_resume*Index`.
- `BudgetExhaustedLastFrame` tracks backpressure for diagnostics.

**Proven pattern:** STALKER Anomaly's Warfare mode handles 50+ squads and hundreds of NPCs at 60 FPS using identical budget-time architecture.

**How to use:**
- The scheduler is wired when `BotManagerComponent` constructs `BudgetScheduler` during activation.
- Default budget is **2ms** (STALKER-proven target); tier fractions are constants in `AIFrameBudgetScheduler`.
- Check `BudgetExhaustedLastFrame` for diagnostics during profiling.

### 2.5 Perception-Based AI LOD

**File:** `SAIN/SAIN/Classes/Bot/SAINAILimit.cs` (rewritten)  
**Enum:** `SAIN/SAIN/SAINEnum.cs` — `PerceptionTier` (Visible, Audible, Occluded)

**The old behavior (bot-centric):**
```csharp
// "How far am I from the player?" → throttles by distance
CurrentAILimit = CheckDistances(closestPlayerDistance);
```

**The new behavior (player-centric):**
```csharp
// "Can the player SEE me? HEAR me? Or am I hidden?"
PerceptionTier DeterminePerceptionTier() {
    if (fighting player)       → Visible    (full AI, 30Hz)
    if (CheckPlayerCanSeeBot()) → Visible   (camera frustum + 1 raycast)
    if (CheckPlayerCanHearBot())→ Audible   (gunfire/sprint/grenade check)
    else                        → Occluded  (navigation only, 5Hz)
}
```

**Visibility detection (amortized to ~1 raycast per bot per second):**
1. Camera frustum check via `GeometryUtility.TestPlanesAABB` (basically free)
2. Single `Physics.Raycast` from camera to bot chest
3. Result cached for 0.5 seconds per bot

**Audibility detection (zero raycasts, zero cost):**
- Gunfire: `BotOwner.WeaponManager.LastFireTime` within last 3 seconds
- Sprinting: `Bot.Mover.IsSprinting` + distance < 60m from player
- Grenade: `Bot.Grenade.LastGrenadeThrowTime` within last 5 seconds
- Result cached for 1 second per bot

**Tick group skipping per tier:**

| Perception Tier | _alwaysTickClasses | _tickWhenActiveClasses | _tickWhenNoSleepClasses | _tickWhenCombatClasses |
|---|---|---|---|---|
| **Visible** | ✓ | ✓ | ✓ | ✓ |
| **Audible** | ✓ | ✓ | ✗ (skip) | ✗ (skip) |
| **Occluded** | ✓ | ✗ (skip) | ✗ (skip) | ✗ (skip) |

Implemented in `BotComponent.ManualUpdate()` lines 189-220.

**Boss exemption removed:** Previously SAIN processed bosses at full AI regardless of distance. Now bosses follow the same perception tiers. On Lighthouse, this means the Goons (3 bosses) + Zryachiy/Cultists (1-3) + Rogues (4-12) no longer consume full CPU when the player is on the opposite side of the map.

**How to use:**
- Perception tier is computed automatically every frame (throttled to 30Hz max)
- `Bot.CurrentPerceptionTier` exposes the current tier for any code to query
- `Bot.TickInterval` is automatically updated to match the perception tier
- The budget scheduler uses `bot.CurrentPerceptionTier` to sort bots into priority groups

---

### 2.5 (continued): Offline Combat Resolution

**Files:**
- `SAIN/SAIN/Components/OfflineCombatResolver.cs` — statistical combat model
- `SAIN/SAIN/Components/CombatAudioSpoofer.cs` — spoofed gunfire audio (procedural clip + `PlayClipAtPoint`; BetterAudio TBD)
- `SAIN/SAIN/Components/OfflineSquadWorldSync.cs` — auto `RegisterOfflineSquad` from occluded AI-vs-AI pairs (`auto_*` ids)
- `SAIN/SAIN/Components/OfflineSquadMaterialization.cs` — reserved hook for offline→online (stub)

**Implementation status vs full SMART:** see **[SMART_OFFLINE_COMBAT.md](SMART_OFFLINE_COMBAT.md)**.

**How offline combat works:**

```
BotZone A ──── BotZone B ──── BotZone C (Player Here)
[PMCs]         [Rogues]       [Scavs + Player]
   │               │                │
   └─── Combat ────┘                │
   Offline resolution:              │ Full SAIN AI
   PMCs win, 2 dead,               (visible + audible)
   1 rogue dead
```

**Statistical model inputs (from BotCombatStats):**
| Stat | Source | Combat Effect |
|---|---|---|
| BotType | Profile.Info.Settings.Role | Base power multiplier |
| Level | Profile.Info.Level | Accuracy/reaction modifier |
| WeaponDamageOutput | Weapon template | Damage output |
| ArmorClass | Equipment inventory | Damage mitigation (1-6 normalized) |
| HealthPercent | HealthController | Time-to-kill factor |

**Resolution formula:**
```csharp
float powerA = Σ(bot.BasePower × weaponFactor × armorFactor × healthFactor)
float powerB = Σ(...)
float rollA = powerA × Random(0.7, 1.3)  // fog of war
float rollB = powerB × Random(0.7, 1.3)
float winRatio = rollA / (rollA + rollB)
int casualtiesA = round((1 - winRatio) × squadA.Count)
int casualtiesB = round(winRatio × squadB.Count)
```

**Audio spoofing:**
- Combat duration: 3-15 seconds (longer for balanced fights)
- Shot density: ~3 shots per second
- Distance attenuation: volume falls off with distance to the listener (see `CombatAudioSpoofer`)
- **Shipped:** procedural `AudioClip` + Unity `PlayClipAtPoint`. **Future:** EFT `BetterAudio` when API is confirmed for the target client.

**How to use:**
```csharp
// Register offline squads with the scheduler
var scheduler = BotManagerComponent.Instance.BudgetScheduler;
scheduler.RegisterOfflineSquad(new OfflineSquad {
    SquadId = "rogue_patrol_3",
    Faction = "Rogue",
    CenterPosition = zonePosition,
    Members = new List<BotCombatStats> { /* bot stats */ }
});

// When player approaches → materialize squad, unregister offline
scheduler.UnregisterOfflineSquad("rogue_patrol_3");
```

---

### AILimit ↔ Perception LOD ↔ Offline Combat Integration

Three systems control bot "liveness" — they resolve in order:

```
Is bot in offline squad?
  └─ YES → Statistical model only (skip both AILimit and Perception)
  └─ NO → Is bot beyond AILimit range?
           └─ YES → SetActive(false) (SPT-AILimit nuclear option)
           └─ NO → Apply PerceptionTier (Visible/Audible/Occluded)
```

---

## Phase 3: Squad Collapse

### 3.1 Shared Squad Awareness

**Files:** `SAIN/SAIN/Classes/BotManager/Squad.cs`, `SAIN/SAIN/Classes/Bot/EnemyControllers/SAINEnemyController.cs`

**The problem:** Each bot in a squad independently runs per-body-part raycasts to detect enemies (O(N²) visibility cost).

**The solution:** When one squad member spots an enemy, the detection propagates to all squad members. This collapses visibility from O(N²) to O(N) for grouped bots.

**How it works:**

```
Bot A sees Enemy X → EnemyAdded → PropagateSquadEnemyDetection
  │
  └─ Squad.OnEnemySightedByMember fires
       │
       └─ ReceiveSquadEnemyDetection iterates all members
            ├─ Bot B: CheckAddEnemy(X) — no raycast needed
            ├─ Bot C: CheckAddEnemy(X) — no raycast needed
            └─ Bot D: already has X → skipped
```

**Guard against infinite loops:**
- `_isPropagatingSquad` flag on SAINEnemyController
- `_isPropagatingEnemy` flag on Squad
- `IsPlayerAnEnemy()` check before adding

**How to use:** Automatic. When any squad member gains a visible enemy, all squad members receive the detection. No configuration needed.

### 3.2 Squad Combat Coordinator

**File:** `SAIN/SAIN/Layers/Combat/Squad/SquadCombatCoordinator.cs`  
**Wired into:** `CombatSquadLayer.IsActive()` at line 66

**The problem:** Each squad member independently evaluates full personality-driven combat decisions (target selection, flanking, suppression).

**The solution:** Squad leader computes decisions once per squad, distributes simplified orders to members.

**Coordination runs every 500ms on the leader:**

```
SquadCombatCoordinator.CoordinateSquad(leaderBot, decisions)
  │
  ├── CollectAllVisibleEnemies()
  │   └── Iterates all members' VisibleEnemies, deduplicates
  │
  ├── DistributeTargets()
  │   └── Each member gets closest enemy as primary target
  │       ├── < 40m, behind cover → Suppress
  │       ├── < 20m → PushSuppressedEnemy
  │       └── > 80m → Search (spread out)
  │
  └── AssignFlankingPositions()
      └── Members sorted by distance to enemy centroid
          └── Closest engages directly, others search/flank
```

**How to use:** Automatic for any bot in a squad with ≥2 members. The squad leader `Bot.Squad.IAmLeader` triggers coordination in `CombatSquadLayer.IsActive()`. Members in **`DogFight`** are skipped when assigning squad decisions so solo combat is not overwritten. **`SquadCombatCoordinator.ResetCoordinationThrottle()`** runs from **`GameWorldComponent.DestroyComponent()`** so throttle keys do not leak across raids.

### 3.3 BigBrain State Tree Migration

**File:** `SAIN/SAIN/Layers/SAINLayer.cs` — optional `CheckIsActiveWithCache(Func<bool> computeUncached)` helper (BigBrain still queries layers each tick unless upstream behavior changes)

**The problem:** BigBrain's `ShallUseNow()` evaluates ALL custom layers every tick (Behavior Tree pattern). Based on StraySpark profiling: Behavior Tree = 0.042ms/tick/agent, State Tree = 0.011ms/tick/agent (4x reduction).

**The solution:** Throttle **inactive** layer checks: pass a lambda that performs the real `IsActive` predicate **without** calling `IsActive()` (avoids infinite recursion). **ExtractLayer** and **CombatSoloLayer** use this (`CombatSoloLayer` sets `IsActiveCheckInterval = 1/30` for tighter pickup); others still evaluate every tick.

```csharp
protected bool CheckIsActiveWithCache(Func<bool> computeUncached)
{
    if (_cachedIsActive)
        return _cachedIsActive = computeUncached();
    if (Time.time - _lastIsActiveCheckTime >= IsActiveCheckInterval) {
        _lastIsActiveCheckTime = Time.time;
        _cachedIsActive = computeUncached();
    }
    return _cachedIsActive;
}
```

**How to use:** Call `ResetIsActiveEvaluationCache()` when short-circuiting (e.g. bot inactive). Override `IsActiveCheckInterval` to adjust throttle (default 0.2s ≈ 5Hz).

---

## Phase 4: Bot GameObject Recycling

### 4.1 Bot Pool System

**Files:**
- `SAIN/SAIN/Components/BotGameObjectPool.cs` — pool manager
- `SAIN/SAIN/Patches/BotPoolPatches.cs` — Harmony interceptors

**The problem:** Every bot death triggers `GameObject.Destroy()` (GC spike). Every spawn triggers `GameObject.Instantiate()` (allocation spike). On Lighthouse with 29 bots and multiple waves, this creates constant stutter.

**The solution:** Intercept destruction/spawning and recycle GameObjects (Call of Duty pattern — 17 bots at 120+ FPS in CQB).

**Pool flow:**

```
Bot dies
  │
  └─ GameObject.Destroy intercepted by BotPoolPatches
       │
       ├─ IsBotGameObject? → YES → ReturnToPool(botType, go)
       │   ├─ gameObject.SetActive(false)
       │   ├─ Enqueue to pool[botType]
       │   └─ Return false → skip real Destroy
       │
       └─ Not a bot → proceed with normal Destroy

Bot spawns
  │
  └─ ABPS spawn pipeline
       │
       ├─ TryGetFromPool(botType, spawnPos)
       │   ├─ Pool has bot? → Dequeue, teleport, SetActive(true)
       │   │   └─ BotComponent.ResetForPoolRecycle()
       │   └─ Pool empty? → null → normal creation
```

**Pool configuration:**
- `MaxPoolSizePerType = 10` — maximum pooled bots per type (PMC, Scav, Boss, etc.)
- Excess bots destroyed normally
- Pool cleared on raid end via `GameWorldComponent.DestroyComponent()`

**Pool keys (bot types):**
```csharp
string botType = player.Profile.Info.Settings.Role.ToString();
// Examples: "pmcBot", "assault", "bossKilla", "followerBully", etc.
```

### 4.2 Pool State Reset

**All reset methods triggered on pool recycle:**

| Component | Method | What's Reset |
|---|---|---|
| **BotComponent** | `ResetForPoolRecycle()` | Vision state, enemy lists, decisions, activation, AI limit, tick timers |
| **SAINVisionClass** | `ResetVisionState()` | Vision distance update timer |
| **SAINEnemyController** | `ClearAllEnemies()` | Goal enemy, known/visible/LoS lists, hash sets, all controllers |
| **EnemyListController** | `ClearAll()` | Enemies dictionary, array, comparison timer |
| **SAINActivationClass** | `ResetActivationState()` | Check timer, forces reactivation |
| **SAINAILimit** | `ResetForPoolRecycle()` | Distance/perception/visibility/audibility timers, current limits |
| **SAINDecisionClass** | `ResetDecisions(true)` | Decision manager full reset |
| **LootingBrain** | `ResetForPoolRecycle()` | Active loot, ignored IDs, looting state, performance timer |

**How to verify:** After a bot is recycled, `BotGameObjectPool.TotalPooledCount` reflects current pool size. Active bots are tracked in `_activeBotInstanceIds` hash set.

---

## Budget Scheduler Integration Points

The budget scheduler replaces the old per-bot iteration in `BotManagerComponent.ManualUpdate()`:

```csharp
// OLD: Process ALL bots, no budget
foreach (BotComponent bot in BotsArray)
    bot.ManualUpdate(currentTime, deltaTime);

// NEW: Budget-gated processing, perception-tiered
BudgetScheduler.ProcessFrame(BotSpawnController.SAINBots, currentTime, deltaTime);
```

Each bot's `ManualUpdate` now skips tick groups based on perception tier:
- **Occluded**: Only `_alwaysTickClasses` (navigation essentials)
- **Audible**: + `_tickWhenActiveClasses` (movement, basic state)
- **Visible**: All tick groups including combat classes

---

## Configuration Reference

### PerformanceSettings (SAIN F6 Editor → Global Settings → General → Performance)

| Setting | Default | Range | Effect |
|---|---|---|---|
| `PerformanceMode` | `true` | bool | Master toggle |
| `VisionRaycastFrequency` | 30 | 10-30 Hz | Vision raycast update rate |
| `LookUpdateFrequency` | 30 | 10-30 Hz | EFT look sensor rate |
| `CoverFindFrequency` | 10 | 2-10 Hz | Cover search in combat |
| `MaxRaycastsPerEnemy` | 3 | 1-3 | Raycast types per body part |
| `FarBotCpuReduction` | 0.5 | 0.1-1.0 | Vision cost multiplier (Far tier) |
| `VeryFarBotCpuReduction` | 0.25 | 0.1-1.0 | Vision cost multiplier (VeryFar tier) |
| `NarniaBotCpuReduction` | 0.0 | 0.0-1.0 | Vision cost multiplier (Narnia tier) |

### AILimitSettings (SAIN F6 Editor → Global Settings → General → AI Limit)

| Setting | Default | Effect |
|---|---|---|
| `LimitAIvsAIGlobal` | `true` | Disable AI functions vs AI at distance |
| `AILimitUpdateFrequency` | 3s | How often to check distances |
| `AILimitRanges[Far]` | 150m | Distance threshold for Far tier |
| `AILimitRanges[VeryFar]` | 250m | Distance threshold for VeryFar tier |
| `AILimitRanges[Narnia]` | 400m | Distance threshold for Narnia tier |

### Budget Scheduler (hard-coded, not configurable via editor)

| Parameter | Value | Rationale |
|---|---|---|
| `MaxAIBudgetMs` | 2.0 | STALKER-proven, leaves 14.7ms for render/physics |
| Visible priority | Always completes | Player is watching |
| Audible priority | Budget permitting | Player hears, needs movement |
| Occluded priority | Leftover budget | Round-robin across frames |

---

## Profiling & Verification

### Pre-optimization baseline
1. Enable SAIN logging via F6 editor → Debug → Logs → `GlobalProfilingToggle = true`
2. Install BepInEx.FPSCounter or use the game's `fps 1` console command
3. Load Lighthouse with 10+ bots
4. Record: ms per frame, ms per bot, GC allocations

### Post-optimization verification
1. Same settings, same map, same bot count
2. Compare frame time, GC allocs, `BudgetExhaustedLastFrame` rate
3. **Target:** Lighthouse with 29 bots at 60+ FPS

### Key diagnostic properties
```csharp
// Budget scheduler diagnostics
BotManagerComponent.Instance.BudgetScheduler.BudgetExhaustedLastFrame
BotManagerComponent.Instance.BudgetScheduler.BotsProcessedThisFrame
BotManagerComponent.Instance.BudgetScheduler.TotalOnlineBots

// Pool diagnostics
BotGameObjectPool.Instance.TotalPooledCount
BotGameObjectPool.Instance.IsActiveBot(gameObject.GetInstanceID())

// Perception diagnostics (per bot)
bot.CurrentPerceptionTier   // Visible=0, Audible=1, Occluded=2
bot.CurrentAILimit           // None=0, Far=1, VeryFar=2, Narnia=3
bot.TickInterval             // 1/30f, 1/10f, or 1/5f depending on tier
```

---

## Files Changed (Complete Inventory)

### New Files (9)

| File | Phase |
|---|---|
| `SAIN/SAIN/Components/AIFrameBudgetScheduler.cs` | 2.2 |
| `SAIN/SAIN/Components/OfflineCombatResolver.cs` | 2.5 |
| `SAIN/SAIN/Components/CombatAudioSpoofer.cs` | 2.5 |
| `SAIN/SAIN/Components/BotGameObjectPool.cs` | 4.1 |
| `SAIN/SAIN/Patches/BotPoolPatches.cs` | 4.1 |
| `SAIN/SAIN/Layers/Combat/Squad/SquadCombatCoordinator.cs` | 3.2 |

### Modified SAIN Files (16)

| File | Changes |
|---|---|
| `Classes/Bot/BotBase.cs` | TickInterval=1f/30f, public setter |
| `Classes/Bot/SAINAILimit.cs` | PerceptionTier, visibility/audibility checks, ResetForPoolRecycle |
| `Classes/Bot/SAINActivationClass.cs` | ResetActivationState |
| `Classes/Bot/Sense/SAINVisionClass.cs` | ResetVisionState |
| `Classes/Bot/EnemyControllers/SAINEnemyController.cs` | Squad propagation, ClearAllEnemies |
| `Classes/Bot/EnemyControllers/EnemyListController.cs` | ClearAll |
| `Classes/BotManager/Squad.cs` | OnEnemySightedByMember event, ReceiveSquadEnemyDetection |
| `Classes/BotManager/Jobs/VisionRaycastJob.cs` | Far-tier 1 raycast, yield fix |
| `Components/BotComponent.cs` | PerceptionTier property, tier-based tick skipping, ResetForPoolRecycle |
| `Components/BotManagerComponent.cs` | Budget scheduler wiring |
| `Components/GameWorldComponent.cs` | Pool cleanup on raid end |
| `SAIN/SAIN/Layers/SAINLayer.cs` | CheckIsActiveWithCache(Func) + Extract/Solo layers |
| `Layers/Combat/Squad/CombatSquadLayer.cs` | SquadCombatCoordinator integration |
| `Preset/.../PerformanceSettings.cs` | PerformanceMode=true |
| `SAINEnum.cs` | PerceptionTier enum |
| `SAINPlugin.cs` | Pool initialization |

### Modified Cross-Mod Files (2)

| File | Changes |
|---|---|
| `SPT-LootingBots/.../LootingBrain.cs` | ResetForPoolRecycle |
| `SPT-AILimit/Component.cs` | LINQ/Min allocation fix (already applied) |

---

## What Was NOT Built (Deferred)

- **Full Questing Bots integration**: Objective system studied but not rebuilt. BotZone tracking and objective types extracted for lightweight navigation layer.
- **MoreBotsAPI spawn control**: Only needed for Phase 4 pool integration. Phase 1-3 don't touch spawn counts.
- **Threat ring (CoD pattern)**: Deferred to Phase 4.2 — requires pool stability first.
- **spt-unda**: Dropped per plan — conflicts with ABPS population caps. Zone opening logic will be extracted into ABPS directly.
