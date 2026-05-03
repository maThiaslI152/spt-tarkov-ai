---

name: sain-performance-optimization
overview: Multi-phase optimization across the entire forked mod stack. Architectural foundation: AI frame budget scheduler (STALKER-proven, 2ms hard cap). Core philosophy: player-centric design — don't simulate what the player can't see. Fake it when unseen, compute only what the player experiences. Progresses from mechanical fixes (Phase 1) through structural improvements (Phase 2), visibility-based AI LOD + offline combat resolution (Phase 2.5), squad-level collapse (Phase 3), to Call of Duty-style bot GameObject recycling (Phase 4). All mods forked — full source control. Target: Lighthouse 29+ bots at 60 FPS.
todos:

- id: phase1-tickinterval
content: "Phase 1.1: Fix TickInterval default in BotBase.cs from 0f to 1f/30f (1-line change, cascading effect across ~16 classes)"
status: completed
- id: phase1-coroutines
content: "Phase 1.2: Replace 16 yield-return-null with WaitForSeconds in 6 coroutine job files"
status: completed
- id: phase1-config
content: "Phase 1.3: Enable AILimit + SAIN Performance Mode preset (configuration-only, no code changes)"
status: completed
- id: phase2-vision-lod
content: "Phase 2.1: Add LOD-tier-based raycast reduction in VisionRaycastJob.cs (3 checks -> 1 for Far+ tiers)"
status: completed
- id: phase2-jobmanager
content: "Phase 2.2: Implement AI frame budget scheduler — guaranteed hard cap on AI processing time per frame (2ms target), priority-based bot processing within budget, STALKER-proven architecture"
status: completed
- id: phase2.5-visibility-lod
content: "Phase 2.5: Implement visibility-based AI LOD + offline combat resolution (SMART terrain pattern) — replace pure-distance AILimit with player-can-see/can-hear occlusion-aware throttling, resolve AI-vs-AI combat statistically using bot equipment/level stats"
status: completed
- id: phase3-squad-awareness
content: "Phase 3.1: Implement shared squad awareness via TalkClass bypass (propagate enemy detection without redundant per-bot raycasts)"
status: completed
- id: phase3-squad-collapse
content: "Phase 3.2: SquadCombatCoordinator + leader hook from CombatSquadLayer (`SAIN/SAIN/Layers/Combat/Squad/`)"
status: completed
- id: phase3-bigbrain-st
content: "Phase 3.3: Optional CheckIsActiveWithCache on SAINLayer (`SAIN/SAIN/Layers/SAINLayer.cs`) — BigBrain/EFT layer arbitration unchanged"
status: completed
- id: phase4-objectpool
content: "Phase 4.1: Implement bot GameObject pool/recycle system — intercept EFT destroy/spawn via Harmony, pool BotOwner GameObjects instead of destroying/recreating"
status: completed
- id: phase4-pool-sain
content: "Phase 4.2: Wire pool recycle events through SAIN BotComponent + LootingBots LootingBrain reset paths"
status: completed
- id: profile-baseline
content: "Capture baseline profiling data before any changes using SAIN logging + BepInEx.FPSCounter"
status: pending
- id: f12-perf-monitor
content: "SAINPerfLog — F12-accessible scheduler stats + per-raid CSV under LogOutput/sain_perf/; diagnostic toggle; SAIN gates spam via SainPerfLogInterop (legacy in-SAIN SAINPerformanceMonitor path retired)"
status: completed
- id: audio-spoofer-wiring
content: "Phase 2.5: Wire CombatAudioSpoofer to EFT BetterAudio (placeholder ready, needs SPT runtime to locate AudioClip assets by weapon template)"
status: pending
isProject: false

---

> **Doc note (2026-05-03):** Raid perf CSV, BigBrain snapshot CSV, and **F12** scheduler/diagnostic UI now live in **`OptimizedMod/SAINPerfLog/`** (`me.sol.sain.perflog`). The YAML todo **`f12-perf-monitor`** below described the original in-SAIN `SAINPerformanceMonitor` path; runtime behavior is **standalone per-raid files** + **SAINPerfLog (F12)**. SAIN’s **SAIN Performance** config section was removed.

# SAIN Performance Optimization Plan

## Design Principle: Player-Centric, Fake It When Unseen

Tarkov and community mods (SAIN, LootingBots, Questing Bots) try to **fully emulate AI** — they make bots truly "think" like players: vision checks, cover finding, tactical decisions, inventory management, loot scanning. This means every bot independently computes its own complete simulation of reality, regardless of whether any human player can see or hear them.

**The core problem:** Most of this computation is invisible to the player. A bot behind 3 walls and 2 floors on the other side of Dorms is running the same AI as the bot in the player's crosshairs.

**The solution:** Flip the paradigm from **bot-centric** (what does this bot need to do?) to **player-centric** (what does the player need to experience?).

```
Bot-Centric (Current):                    Player-Centric (Target):
"What should this bot do?"                "Can the player see this bot?"
↓                                         ↓
Bot checks vision → cover → tactics       Camera raycast: yes/no
↓                                         ↓
Independent of player perception          If NO → fake it, skip expensive work
                                          If YES → full AI, be convincing
```

**The Theater Model:** Bots are actors on a stage. The player is the audience. When the audience isn't looking, actors can go off-script. When the audience looks, they must perform. The audience hears footsteps through walls — they don't need to see perfectly choreographed tactical maneuvers, they just need the *result* to feel right.

This principle cascades through every phase:


| Phase   | Bot-Centric (What We Stop Doing)                            | Player-Centric (What We Do Instead)                                  |
| ------- | ----------------------------------------------------------- | -------------------------------------------------------------------- |
| **1**   | Tick every class every frame (bots "need" constant updates) | Default to 30Hz, only tick faster when player is engaged             |
| **2**   | LOD based on distance from bot                              | LOD based on distance from player's camera                           |
| **2.5** | Full AI for all bots regardless of walls                    | Visibility + audibility gating: occluded bots get minimal processing |
| **3**   | Each squad member computes independently                    | One squad coordinator decides, members execute simplified orders     |
| **4**   | Destroy/create GameObjects on death/spawn                   | Recycle and reset — the same "actor" plays a new role                |


## Architectural Foundation: AI Frame Budget (What STALKER Proves at Scale)

All the faking, tiering, and offline resolution in this plan depends on one mechanism that SAIN completely lacks: a **guaranteed AI time budget per frame.**

**How STALKER Anomaly's budget time works:**

```
Each frame (16.7ms at 60 FPS):
├── Rendering: ~8ms (GPU work)
├── Physics: ~2ms (Unity physics step)
├── Player input: ~1ms
├── AI/A-Life budget: ~2ms (HARD CAP — never exceeds this)
│   ├── Process closest squad (highest priority)
│   ├── If time remains → process next squad
│   ├── If time remains → process next squad
│   └── Budget exhausted → STOP, remaining squads wait until next frame
└── Other systems: ~3.7ms
```

The budget is **non-negotiable**. When 2ms of AI time is consumed, the AI system stops processing and resumes next frame. This guarantees stable 60 FPS regardless of how many bots exist on the map — the AI simply processes fewer bots per frame when the map is crowded, but the framerate never drops.

**SAIN's current behavior (no budget):**

```
Each frame:
├── Rendering: ~8ms
├── Physics: ~2ms
├── Player input: ~1ms
├── SAIN AI: ~???ms (UNBOUNDED — tries to process ALL bots)
│   ├── Bot 1: full AI
│   ├── Bot 2: full AI
│   ├── ...all bots...
│   └── Bot 30: full AI → total: 15ms
└── Frame total: 26ms → 38 FPS (budget blown)
```

When SAIN processes all bots, the frame budget is destroyed. FPS drops. No recovery mechanism exists.

**The budget scheduler architecture:**

```csharp
public class AIFrameBudgetScheduler
{
    public float MaxAIBudgetMs = 2.0f; // 2ms hard cap per frame
    private Queue<AITask> _taskQueue = new();
    private Stopwatch _frameTimer = new();
    
    public void ProcessFrame()
    {
        _frameTimer.Restart();
        
        // Phase 1: Offline tasks (sub-0.1ms each, process all every frame)
        foreach (var squad in OfflineCombatSquads)
            ResolveOfflineCombatTick(squad); // Statistical model, negligible cost
        
        // Phase 2: Online task tiers (process within remaining budget)
        ProcessTier(AITier.Visible,    _frameTimer);  // High priority: must complete
        ProcessTier(AITier.Audible,    _frameTimer);  // Medium priority: process what fits
        ProcessTier(AITier.Occluded,   _frameTimer);  // Low priority: leftover budget only
    }
    
    void ProcessTier(AITier tier, Stopwatch timer)
    {
        var bots = GetBotsByTier(tier);
        foreach (var bot in bots)
        {
            if (timer.ElapsedMilliseconds >= MaxAIBudgetMs)
                return; // Hard stop — remaining bots wait until next frame
            
            ProcessBot(bot, tier);
        }
    }
}
```

**Shipped SAIN fork note:** `AIFrameBudgetScheduler` uses **`ProcessTierRoundRobin`** with per-tier **resume indices** and **phase ceilings** (~45% Visible slice, cumulative ~88% before Occluded) rather than a naive single `foreach` per tier. Conceptually the same budget guarantee; pseudocode above is simplified.

**Why budget time is the foundation, not just an optimization:**

Every other technique in this plan produces variable savings. Perception tiering reduces work for occluded bots, but it doesn't guarantee the AI won't spike when 10 bots suddenly become visible. Budget time provides the guarantee:

- 5 visible bots this frame? Process all 5 in 2ms.
- 15 visible bots this frame? Process the first 8 in 2ms, queue the rest.
- Player won't notice: bots processed at 15ms intervals still behave naturally (60Hz AI is overkill for human-like behavior anyway).

**STALKER's proof:** Warfare mode processes 50+ squads, hundreds of NPCs, and complex faction logic — all on potato hardware at 60 FPS. The AI system NEVER uses more than its allocated budget. SPT can and should do exactly the same thing.

The 2ms budget is a design target based on the 16.7ms frame budget at 60 FPS:

- 2ms for AI (our target)
- Remaining 14.7ms for rendering, physics, networking, and everything else
- Matches STALKER's proven allocation

**Implementation note:** Unity's `Time.realtimeSinceStartup` and `Stopwatch` provide microsecond precision. The scheduler tracks elapsed time and enforces the hard cap. There is no "just finish this one last bot" exception — that's how SAIN currently destroys framerates.

## The Quantity Problem: Live Tarkov Needs ~30 Bots, SAIN Can Handle ~5

There's a fundamental tension in SPT that no current mod setup resolves:


|                       | Live Tarkov (BSG Servers)     | SPT + SAIN (Local CPU)                | SPT Vanilla AI                 |
| --------------------- | ----------------------------- | ------------------------------------- | ------------------------------ |
| **Bot count per map** | 20-48 (varies by map)         | Practical: 5-8 at decent FPS          | Can handle more, but...        |
| **AI quality**        | High (server-side, optimized) | Very high (SAIN: tactical, realistic) | Low (simple patrol + shoot)    |
| **Bottleneck**        | None (dedicated servers)      | Single player CPU                     | Not bottlenecked by AI quality |


**The problem:** SAIN chose **quality over quantity**. It makes bots behave like real players — full vision raycasts, cover finding, tactical decisions, squad coordination, personality-driven behavior. Beautiful AI. But it costs so much CPU that you can only run 5-8 bots on a Ryzen 5 5600 at acceptable FPS.

**The alternative:** Vanilla Tarkov AI can handle more bots, but it's too simple — bots patrol, see enemy, shoot. No tactics, no cover usage, no squad behavior. Immersion-breaking.

**The gap we need to bridge:**

```
Target: SAIN-quality AI × Live-Tarkov population = ~30 bots at 60 FPS
Current: ~6 bots at 15-30 FPS (on Lighthouse/Customs)
Gap: 5x more bots at 4x higher FPS = 20x improvement needed
```

**How "fake it when unseen" solves this:**

The key insight: the player only experiences a fraction of the AI on the map at any moment. Lighthouse with 30+ bots might have:

- 3-4 bots in the player's direct vicinity (visible or fighting) → need SAIN-quality
- 5-8 bots the player can hear (footsteps, distant gunfire) → need movement + basic reactions
- 15-20 bots the player is completely unaware of → just need to exist, move along objectives
- 5-10 "offline" bots (statistics only, SMART terrain) → resolved via statistical model, zero CPU

By tiering AI quality to player perception (and moving some bots entirely offline), we deliver the full-map population experience at a fraction of the CPU cost:

```
30 online bots × SAIN-quality (0.5ms each) = 15ms per frame → ~30 FPS (unplayable)
30 online + 15 offline × perception-tiered = 2.68ms → ~60+ FPS
```

**Lighthouse as the benchmark target:**

Lighthouse is the hardest map because of its diverse boss ecosystem:


| AI Type                            | Count     | Behavior                                      | Current SAIN Cost                    |
| ---------------------------------- | --------- | --------------------------------------------- | ------------------------------------ |
| Rogues (Water Treatment)           | 4-12      | Coordinated defense, mounted weapons, patrols | Full AI per bot                      |
| Goons (Knight, Big Pipe, Birdeye)  | 3         | Coordinated 3-boss squad, flanking tactics    | Full AI per bot                      |
| Zryachiy + Cultist guards (Island) | 1-3       | Island defense, cultist behavior              | Full AI per bot                      |
| Partisan (loitering)               | 1         | Roaming, ambush behavior                      | Full AI                              |
| PMCs                               | 8-12      | Full SAIN tactical AI                         | Full AI per bot                      |
| Scavs                              | 5-10      | Patrol, loot, fight                           | Full AI per bot                      |
| **Total**                          | **22-41** |                                               | **All at full quality → impossible** |


**Two specific SAIN behaviors that waste CPU:**

1. **Bosses always active:** SAIN processes bosses at full AI even when they're across the map, behind mountains, completely irrelevant to the player. The Goons could be patrolling the Chalet area while the player is at the Water Treatment Plant — SAIN still computes their full tactical decisions.
2. **AI-vs-AI combat simulation:** When PMCs fight Scavs (or Rogues fight PMCs) far from the player, SAIN fully simulates the combat — vision, cover, tactics, shooting — for immersion's sake. Great intent, but the player only hears distant gunfire. They don't need to see the flanking maneuver.

**Both can be faked:**

- Bosses far from player: reduce to movement + basic pathfinding only
- AI-vs-AI combat far from player: skip full combat simulation, just produce gunfire audio + move bots around the engagement zone, occasionally "kill" one probabilistically
- When the player approaches: switch those bots to full SAIN quality seamlessly

## Problem

SPT runs all bot AI locally on the player's CPU. Even 5-6 fighting bots drops to ~15 FPS (20ms frame time, exceeding the 16.7ms budget). SAIN is the primary bottleneck: ~16 of 18 bot classes tick every frame needlessly, and 16 `yield return null` in coroutine jobs resume per-frame regardless of configured intervals.

## Phased Approach

### Phase 1: Mechanical Fixes (highest impact-to-effort)

**1. Fix TickInterval defaults in BotBase.cs**

- Root cause: `TickInterval` defaults to `0f`, so `ShallTick()` always returns true
- Change: Set `TickInterval = 1f / 30f` (30Hz default) at [SAIN/SAIN/Classes/Bot/BotBase.cs line 71](SAIN/SAIN/Classes/Bot/BotBase.cs)
- Impact: ~16 classes stop ticking every frame

**2. Throttle inner coroutine yields (6 job files)**

- 16 `yield return null` across 6 files: VisionRaycastJob.cs (4), EnemyPlaceRaycastJob.cs (6), DirectionDataJob.cs (1), FlashlightRaycastJob.cs (2), RandomVisiblePointGeneratorJob.cs (1), EnemyPathVisibilityRaycastJob.cs (2)
- Change: Replace `yield return null` with cached `WaitForSeconds` matching outer loop
- Impact: Eliminates per-frame coroutine resumes

**3. Enable AILimit + SAIN Performance Mode**

- Configuration-only: Set SAIN's AI Limit tier system + SPT-AILimit bot deactivation
- Impact: 50%+ CPU reduction on distant bots without code changes

### Phase 2: Structural Improvements

**4. LOD-tier raycast reduction in VisionRaycastJob.cs**

- SAIN's `AILimitSetting` (None/Far/VeryFar/Narnia) already exists but doesn't reduce raycast count
- Change: Drop from 3 raycasts per body part to 1 for Far+ tiers
- Impact: Proportional savings at distance

**5. Implement AI frame budget scheduler (Foundation)**

- Currently SAIN has no time budget — processes all bots, FPS crashes
- Change: Create `AIFrameBudgetScheduler.cs` with 2ms hard cap, tiered priority processing (Visible → Audible → Occluded)
- This is the **first structural change to build** — everything else depends on it
- Replaces existing coroutine scheduling entirely (no more yield return problems)
- Impact: Guarantees AI never steals frames from rendering, regardless of bot count

### Phase 2.5: Visibility-Based AI LOD — "If You Can't See It, Fake It"

**The philosophy:** Tarkov's AI mods simulate bots as if they're real players — every bot runs full vision, cover-finding, tactical decisions, and combat logic. But the player only experiences a fraction of this. A bot the player can't see doesn't need to truly "think" — it just needs to produce a convincing result if the player happens to look.

This phase replaces pure-distance AILimit with **perception-gated computation**: only compute what the player can experience.

**The current wrong behavior in `SAINAILimit.CheckDistances()`:**

```54:69:SAIN/SAIN/SAIN/Classes/Bot/SAINAILimit.cs
    private AILimitSetting CheckDistances(float closestPlayerDist)
    {
        var aiLimit = GlobalSettingsClass.Instance.General.AILimit;
        if (closestPlayerDist < aiLimit.AILimitRanges[AILimitSetting.Far])
            return AILimitSetting.None;  // BOT: "I'm close, I need full AI"
        ...
    }
```

The bot asks "how far am I?" — not "does the player know I exist?"


| Scenario                             | Distance | Bot-Centric Result | Player-Centric Reality                                               |
| ------------------------------------ | -------- | ------------------ | -------------------------------------------------------------------- |
| Bot behind 3 walls, 2 floors (Dorms) | 30m      | FULL AI            | Player can't see OR hear them — fake everything                      |
| Bot in open field with scope line    | 200m     | THROTTLED          | Player can see them — need convincing behavior                       |
| Bot footsteps in adjacent room       | 15m      | FULL AI            | Player hears but doesn't see — need position + movement, not tactics |
| Bot sprinting on floor above         | 10m      | FULL AI            | Player hears — same as above                                         |


**The fix: perception-gated computation.**

Instead of "how far is the bot?", ask two player-centric questions:

1. **Can the player SEE this bot right now?** → Camera frustum + single raycast
2. **Can the player HEAR this bot right now?** → Gunfire, footsteps, door breach, grenade

```csharp
// New: PerceptionTier replaces AILimitSetting
enum PerceptionTier
{
    Visible,   // Player sees bot → FULL AI (must act convincing)
    Audible,   // Player hears but can't see → POSITION + MOVEMENT only (fake the rest)
    Occluded   // Player unaware of bot → NAVIGATION only (just walk patrol path)
}
```

**What gets faked at each tier:**


| Tier         | What's Real                                             | What's Faked                                                                                        |
| ------------ | ------------------------------------------------------- | --------------------------------------------------------------------------------------------------- |
| **Visible**  | Everything — vision, cover, tactics, shooting, movement | Nothing — player is watching, bot must perform                                                      |
| **Audible**  | Position, patrol path, fire-if-shot-at                  | Vision checks (no player to check against), cover finding, tactical decisions, inventory management |
| **Occluded** | Patrol path navigation only                             | Everything else — vision, cover, tactics, combat, squad coordination                                |


**Tick frequency per tier:**


| Perception Tier | Vision | Decision | Cover | Movement | Total CPU per bot |
| --------------- | ------ | -------- | ----- | -------- | ----------------- |
| **Visible**     | 30Hz   | 30Hz     | 10Hz  | 30Hz     | Full (~0.5ms)     |
| **Audible**     | 5Hz    | 5Hz      | skip  | 10Hz     | Reduced (~0.1ms)  |
| **Occluded**    | 2Hz    | 2Hz      | skip  | 5Hz      | Minimal (~0.02ms) |


**Audibility detection (no raycasts, zero-cost):**

- Gunfire: `BotOwner.WeaponManager.LastFireTime` within 3 seconds
- Sprint footsteps: `BotOwner.Mover.IsSprinting` + distance < 60m (audible through walls)
- Door breach / grenade: event-based flags
- Cache result for 1-2 seconds

**Visibility detection (one raycast, amortized):**

- Camera frustum check first: `GeometryUtility.TestPlanesAABB` (basically free)
- If in frustum: single `Physics.Raycast` from camera to bot chest position
- Cache result for 0.5-1.0 seconds per bot
- Amortized cost: ~1 raycast per bot per second

**Key files:**


| File                                                                                   | Change                                                                                       |
| -------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| [SAIN/SAIN/SAIN/Classes/Bot/SAINAILimit.cs](SAIN/SAIN/SAIN/Classes/Bot/SAINAILimit.cs) | Replace `CheckDistances()` with `CheckPerception()` — player-centric visibility + audibility |
| [SAIN/SAIN/SAIN/Classes/Bot/BotBase.cs](SAIN/SAIN/SAIN/Classes/Bot/BotBase.cs)         | Wire `TickInterval` to perception tier (Visible=30Hz, Audible=10Hz, Occluded=5Hz)            |
| [SAIN/SAIN/SAIN/Components/BotComponent.cs](SAIN/SAIN/SAIN/Components/BotComponent.cs) | Skip entire tick groups when perception tier doesn't warrant them                            |


**Why this works for Tarkov's map design:**

```
Customs Dorms (3 floors, many rooms, thin walls)
┌─────────────────────────────────────────┐
│ Floor 3: [Bot A] looting room           │ ← Occluded (2 floors + walls)
│          → Navigation only, 0.02ms      │
│                                         │
│ Floor 2: [Player] clearing rooms        │
│                                         │
│ Floor 1: [Bot B] sprinting, footsteps   │ ← Audible (footsteps travel)
│          → Movement + patrol, 0.1ms     │
│                                         │
│ Outside: [Bot C] visible through window │ ← Visible (player sees)
│          200m away with scope           │ → Full AI, 0.5ms
└─────────────────────────────────────────┘

Bot-centric: A=0.5ms, B=0.5ms, C=0.1ms → 1.1ms total
Player-centric: A=0.02ms, B=0.1ms, C=0.5ms → 0.62ms total (43% less)

But more importantly: on Streets with 20 bots where 15 are occluded,
the savings are ~7.5ms of frame time — the difference between 45 FPS and 60 FPS.
```

**Interaction with SPT-AILimit:**
SPT-AILimit handles extreme range (>200m) via `GameObject.SetActive(false)` — the nuclear option. Perception LOD handles the medium range (20-200m) where bots are alive but hidden. Together they cover the full spectrum: dead bots, hidden bots, heard bots, seen bots.

**Boss always-active override removal:**

SAIN currently processes bosses at full AI regardless of distance. This is intentional — bosses are special, they drive map narrative. But on Lighthouse, this means the Goons (3 bosses) + Zryachiy/Cultists (1-3) + Partisan (1) + Rogues (4-12) = 9-19 bots running full AI even when the player is on the opposite side of the map.

Fix: Remove the boss exemption from `SAINAILimit.CheckDistances()`. Bosses follow the same perception tiers as regular bots:

- Player can't see/hear the Goons → minimal processing
- Player approaches Chalet area → Goons switch to full AI
- Bosses remain "special" only in their *behavior complexity when visible*, not in their *CPU budget allocation*

**AI-vs-AI combat: fake the distant firefight:**

When PMCs fight Scavs or Rogues fight PMCs far from the player, SAIN fully simulates the combat. The player only hears distant gunfire. Instead:

1. **Detect AI-vs-AI engagement** (bots shooting at bots, not player)
2. **If player can't see the combat zone:**
  - Skip vision/cover/tactical computation for both sides
  - Produce gunfire audio (spoof shots, no real bullet simulation)
  - Move bots around the engagement zone randomly
  - Probabilistically "kill" one side after N seconds (weighted by bot type/power)
  - Notify LootingBots of new corpse if applicable
3. **If player approaches:** Transition to full SAIN combat simulation seamlessly

This turns an expensive combat simulation into a cheap theater production while preserving the player's experience of a living, fighting world.

**Offline combat resolution (STALKER Anomaly Warfare SMART Terrain pattern):**

The theater approach above still keeps bots as live GameObjects. But STALKER Anomaly's Warfare mode goes further: its SMART (Simulated Military Action in Real Time) terrain system resolves inter-faction combat **entirely offline** using only squad statistics. No GameObjects, no per-frame updates — just math.

**How SMART terrain works in STALKER:**

- The Zone is divided into sectors/territories
- Each faction controls territories and dispatches squads with statistics (rank, equipment, numbers)
- When enemy squads enter the same territory, combat is resolved via statistical model
- **Offline Power Multiplier** adjusts faction strength for simulated warfare
- Results: territories change hands, squads destroyed/damaged, loot generated
- When the player enters the area, the world state reflects simulation results — corpses, surviving squads, territory ownership

**How this applies to SPT — bots already have rich statistics:**

Every SPT bot spawns with complete combat statistics that can feed an offline resolution model:


| Statistic    | Source                                 | Combat Relevance                            |
| ------------ | -------------------------------------- | ------------------------------------------- |
| Bot type     | `BotOwner.Profile.Info.Settings.Role`  | Base power multiplier (PMC > Raider > Scav) |
| Bot level    | `BotOwner.Profile.Info.Level`          | Accuracy, reaction time modifier            |
| Weapon class | `BotOwner.WeaponManager`               | Damage output, effective range, fire rate   |
| Ammo type    | Weapon magazine content                | Penetration value vs armor                  |
| Armor class  | `BotOwner.Profile.Inventory.Equipment` | Damage reduction                            |
| Health pool  | Head/Thorax/Stomach/Arm/Leg values     | Time-to-kill                                |
| Squad size   | `BotOwner.BotsGroup.MembersCount`      | Force multiplier                            |
| BotZone      | Questing Bots zone tracking            | Territory assignment                        |


**Offline combat resolution formula (example):**

```csharp
float ResolveOfflineCombat(Squad squadA, Squad squadB)
{
    // Combat power = Σ (bot_power × weapon_multiplier × armor_multiplier)
    float powerA = squadA.Members.Sum(b => 
        b.BasePower * b.WeaponDamageOutput * b.ArmorMitigation * b.HealthFactor);
    float powerB = squadB.Members.Sum(b => 
        b.BasePower * b.WeaponDamageOutput * b.ArmorMitigation * b.HealthFactor);
    
    // Add randomness (fog of war)
    float rollA = powerA * Random.Range(0.7f, 1.3f);
    float rollB = powerB * Random.Range(0.7f, 1.3f);
    
    // Determine casualties proportionally
    float winRatio = rollA / (rollA + rollB);
    int casualtiesA = Mathf.RoundToInt((1 - winRatio) * squadA.Members.Count);
    int casualtiesB = Mathf.RoundToInt(winRatio * squadB.Members.Count);
    
    return new OfflineCombatResult(casualtiesA, casualtiesB, winRatio > 0.5f ? squadA : squadB);
}
```

**Integration with the player-centric model:**

```
BotZone A ──── BotZone B ──── BotZone C (Player Here)
[PMCs]         [Rogues]       [Scavs + Player]
   │               │                │
   └─── Combat ────┘                │
   Offline resolution:              │ Full SAIN AI
   PMCs win, 2 dead,               (visible + audible)
   1 rogue dead                   
                                   
When player enters BotZone B: corpses materialize, surviving PMCs transition to online (full SAIN)
```

**This enables populations BEYOND maxBotCap:**

Current SPT maxBotCap = 29 (Lighthouse). But with offline resolution:

- 10-15 "online" bots (GameObjects in player's perception sphere) — full SAIN
- 20-40 "offline" bots (statistics only, resolved offline) — zero CPU cost
- Total perceived population: 30-55 bots

The world feels alive and populated because offline bots still fight, die, and leave traces. The player finds corpses, hears distant gunfire (spoofed), and encounters battle aftermath.

**Transition trigger:** When the player's perception sphere (visible + audible range) overlaps with an offline squad's territory, materialize the squad into real GameObjects with state derived from the offline simulation results.

**Spoofed audio for offline combat — hearing the fake war:**

The visual aftermath (corpses, loot) is only half the experience. In live Tarkov, players navigate toward distant gunfire — it's the primary cue that tells you "something is happening over there." Offline combat must produce convincing audio or the world feels dead.

**Audio generation from combat statistics:**

When offline combat resolves, the statistical model tells us:

- Which squads fought (PMCs vs Rogues, Scavs vs PMCs)
- How many bots participated on each side
- What weapons they carry (AK-74M, M4A1, MP-153 shotgun, etc.)
- How long the engagement lasted (derived from combat power ratio)
- Who won and how many casualties

From these, we generate an audio schedule:

```
Offline Combat: 3 PMCs (2x AK-74M, 1x MP5) vs 4 Rogues (3x M4A1, 1x DVL-10 sniper)
Combat power ratio: 40/60 → Rogues favored → medium-length engagement (~15 seconds)

Generated Audio Schedule:
t=0.0s: M4A1 burst (Rogue opens fire)          at combat zone center + random offset
t=0.3s: AK-74M return fire (PMC responds)      at combat zone center + different offset
t=1.2s: M4A1 sustained fire (Rogue suppressing) at combat zone center
t=1.8s: AK-74M single shots (PMC repositioning) at slightly moved position
t=3.0s: DVL-10 sniper shot (Rogue sniper)      at elevated position
t=3.5s: MP5 burst (PMC close range)            at moved position
t=5.0s: M4A1 kill shot burst                   at combat zone center
t=5.5s: AK-74M dying shots                     at same position
...continues for ~15 seconds total
```

**Implementation using EFT's own gunshot audio:**

EFT already has weapon-specific audio clips for every gun. We can either:

1. Reference EFT's `AudioClip` assets by weapon template ID and play through temporary `AudioSource` components
2. Use SAIN's existing gunfire event system (which already triggers audio for online bots)
3. Create lightweight `CombatAudioSpoofer` MonoBehaviour that schedules and plays gunshot sequences

```csharp
public class CombatAudioSpoofer : MonoBehaviour
{
    public void ScheduleOfflineCombatAudio(OfflineCombatResult result, Vector3 combatZone)
    {
        float duration = result.EstimatedCombatDuration; // e.g., 15 seconds
        float distance = Vector3.Distance(combatZone, Camera.main.transform.position);
        float volumeMultiplier = Mathf.Clamp01(1f - (distance / 500f)); // attenuate beyond 500m
        
        StartCoroutine(PlayCombatSequence(result, combatZone, volumeMultiplier));
    }
    
    IEnumerator PlayCombatSequence(OfflineCombatResult result, Vector3 zone, float volume)
    {
        var shots = GenerateShotSchedule(result); // List of (time, weaponType, position)
        float elapsed = 0f;
        int shotIndex = 0;
        
        while (elapsed < result.EstimatedCombatDuration && shotIndex < shots.Count)
        {
            if (elapsed >= shots[shotIndex].Time)
            {
                var shot = shots[shotIndex];
                AudioClip clip = GetWeaponAudio(shot.WeaponType);
                AudioSource.PlayClipAtPoint(clip, shot.Position, volume);
                shotIndex++;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
    }
}
```

**Distance-based audio fidelity:**


| Distance to Combat | Audio Behavior                                               | Player Experience                                        |
| ------------------ | ------------------------------------------------------------ | -------------------------------------------------------- |
| 0-100m             | Full gunfire audio, multiple weapon types distinguishable    | "That's an M4 vs AK fight nearby, I should check it out" |
| 100-300m           | Attenuated gunfire, less weapon distinction, some shots lost | "I hear fighting over at Water Treatment"                |
| 300-500m           | Muffled pops, only louder weapons (sniper, shotgun) audible  | "Distant gunfire, somewhere on the map"                  |
| 500m+              | No audio (too far to hear)                                   | Silence                                                  |


**Integration with the combat model:**

The statistical model should produce richer output to feed audio generation:

- `CombatDuration`: how long the firefight lasted
- `WeaponTypesUsed`: list of weapon templates on each side
- `ShotDensity`: shots per second (high = intense firefight, low = sniper duel)
- `CombatZoneCenter`: where the audio should originate
- `IsAmbush`: if true, short burst then silence (asymmetric combat)

This makes offline combat not just statistically correct but **audibly convincing** — the player hears a world at war and can navigate toward (or away from) the action organically, exactly like live Tarkov.

**Objective-driven bot movement (Questing Bots pattern):**

Questing Bots (forked but currently broken on latest SPT) gives bots objectives: extract, loot specific areas, patrol routes, plant items. This is a critical enabler for the "fake it" approach:

- **Without objectives:** Bots loiter aimlessly when occluded. Boring and unconvincing if the player stumbles upon them.
- **With objectives:** Bots navigate with purpose toward destinations. When the player looks, the bot is moving toward a believable goal — looting a room, heading to extract, patrolling a zone.

The Questing Bots codebase proves:

- BotZone data is accessible at runtime (already used to assign objective locations)
- BigBrain CustomLayer integration exists (registers at priority 20-30, between LootingBots and SAIN combat)
- Pathfinding for objectives uses Waypoints' enhanced NavMesh
- `BotPathingUpdateInterval` is configurable (default 100ms — already throttled)

We can extract the objective system from Questing Bots and integrate it as a lightweight "navigation layer" that runs even at the Occluded tier. This gives occluded bots convincing movement without the full SAIN computation stack.

**SPT maxBotCap reference (target populations):**


| Map             | SPT maxBotCap (Online) | Offline Squads                                         | Total Perceived | Player-Centric Online Budget                  |
| --------------- | ---------------------- | ------------------------------------------------------ | --------------- | --------------------------------------------- |
| **Customs**     | 19                     | 2-3 squads (Reshala patrol, Scav groups)               | 25-30           | 3 visible + 5 audible + 11 occluded = ~2.5ms  |
| **Lighthouse**  | 29                     | 3-5 squads (Rogue patrols, Goon route, Cultist island) | 35-45           | 4 visible + 8 audible + 17 occluded = ~3.2ms  |
| **Streets**     | 48                     | 4-6 squads (Kaban defense, Scav gangs, PMC groups)     | 50-60           | 5 visible + 12 audible + 31 occluded = ~4.3ms |
| **Interchange** | 30                     | 2-3 squads (Killa patrol, Scav groups)                 | 35-38           | 3 visible + 7 audible + 20 occluded = ~2.6ms  |
| **Reserve**     | 28                     | 3-4 squads (Glukhar squad, Raider patrols, Scavs)      | 33-38           | 3 visible + 8 audible + 17 occluded = ~2.6ms  |


All maps land under 5ms of AI frame budget for online bots. Offline squads cost zero CPU — resolved via statistical model.

---

### Phase 3: Squad-Level Computation Collapse (fork-enabled)

**6. Shared squad awareness via TalkClass bypass**

- Currently: each bot in a squad independently runs per-body-part raycasts to detect enemies (O(N²) visibility)
- Change: When one squad member spots an enemy, propagate detection to all squad members without redundant raycasts
- Impact: Collapses visibility from O(N²) to O(N) for grouped bots (bosses+guards, PMC groups)

**7. Squad hierarchy collapse in CombatSquadLayer**

- Currently: each bot independently evaluates full personality-driven decisions even within squads
- Change: Squad coordinator computes target distribution, flanking direction, and suppression assignments once → individual bots execute simplified orders
- Pattern from Bannerlord's 1000-agent battles and Call of Duty's 17-bot local multiplayer
- Impact: Dramatic reduction for Tarkov's boss/guard groups (Reshala + 4 guards = 5x redundant computation eliminated)

**Fork status:** `SquadCombatCoordinator` ships under `SAIN/SAIN/Layers/Combat/Squad/` and runs when the squad leader’s `CombatSquadLayer` is active (`CoordinateSquad` on leader tick).

**8. BigBrain layer evaluation migration (State Tree pattern)**

- Originally scoped: change BigBrain so **not every layer’s `IsActive()` runs every tick** — "active layer + transitions only"
- **Shipped in this fork:** optional **`CheckIsActiveWithCache()`** on `SAIN/SAIN/Layers/SAINLayer.cs` for mods that choose to throttle inactive checks; BigBrain / EFT wrappers still drive layer polling unless separately patched.
- StraySpark-style gains require edits to **`CustomLayerWrapper` / `BrainManager`** (not done here); treat Phase 3.3 doc narrative as **design target + partial helper**, not completed framework migration.

### Phase 4: Bot GameObject Recycling (Call of Duty Spawn Pattern)

**9. Intercept EFT bot destroy → pool instead**

- Patch `BotOwner` destruction path (or `GameObject.Destroy` for bot objects) to redirect to a pool
- Maintain a `Dictionary<string, Queue<GameObject>>` pool keyed by bot type (PMC, Scav, Boss, etc.)
- On bot death: reset position/health/inventory, deactivate via `SetActive(false)`, enqueue to pool
- Eliminates `GameObject.Instantiate()` and `Destroy()` GC spikes on every death/wave

**10. Intercept EFT bot spawn → pull from pool**

- Patch ABPS `BotOwnerCreationPatch` (and EFT's internal spawn path) to check pool first
- If pooled bot available: teleport to spawn point, reset AI state (`BotStandBy`, BigBrain layers, SAIN components), activate
- Fall back to normal creation if pool is empty (first wave after raid start)

**11. Wire state reset through the mod stack**


| Component                  | Reset Required                                                          |
| -------------------------- | ----------------------------------------------------------------------- |
| EFT `BotOwner`             | Position, health, inventory, equipment, brain state                     |
| BigBrain layers            | Layer wrappers re-validate brain assignment, reset active layer         |
| SAIN `BotComponent`        | Reset `TickClassGroup` timers, vision state, enemy list, decision state |
| LootingBots `LootingBrain` | Reset `ScanScheduler` tokens, `ActiveLootCache`, `LootFinder` state     |
| AILimit                    | Re-register recycled bot in distance tracking, spawn eligibility timer  |


**12. Threat ring integration (CoD pattern)**

- Once pooling is stable, add CoD's "threat ring" model: bots track a single aggregated threat direction/intensity instead of per-enemy visibility
- Collapses O(N²) enemy perception into O(N)
- Works synergistically with squad awareness (Phase 3.1) and object pooling

**Design considerations from CoD's architecture:**

```
CoD Pattern                        SPT Implementation
─────────────────────────────────────────────────────────
Bot dies → pool, don't destroy     Harmony patch on Destroy/despawn
Pool → teleport + reset            Reset state, position, Activate
Fixed-timestep AI decoupling       JobManager (Phase 2.2)
Threat ring over per-enemy checks  Aggregated threat score (Phase 3.1 + 4)
Shared squad awareness             TalkClass bypass (Phase 3.1)
```

**Risk assessment:**


| Risk                                     | Mitigation                                                                                                                    |
| ---------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| EFT initialization assumes fresh objects | Audit BotOwner creation path to identify all fields needing reset; write exhaustive ResetState() method                       |
| Pool size memory overhead                | Cap pool per bot type (e.g., max 5 PMCs, 10 Scavs, 3 Bosses), destroy excess                                                  |
| BigBrain layer leaks on recycled bots    | Force `Stop()` + `Start()` lifecycle on all layers during recycle                                                             |
| AILimit interacts with SetActive         | Pool deactivates bots; AILimit also deactivates — ensure they don't conflict (pool owns inactive state outside AILimit range) |
| Compatibility with ABPS spawn patches    | ABPS already patches spawn pipeline; pool interceptor must run before ABPS hooks                                              |


**Effort:** HIGH (touches EFT core, multiple mods). **Impact:** Eliminates GC spikes from bot creation/destruction entirely. CoD proves this handles 17 bots in dense CQB at 120+ FPS — SPT can match that.

---

## Fork Control Summary


| Mod                           | Forked?                                                                                                                                                                       | Can Edit?    | Primary Changes                                                                                                                        |
| ----------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------ | -------------------------------------------------------------------------------------------------------------------------------------- |
| **SAIN**                      | Yes (954 files)                                                                                                                                                               | Full control | TickInterval, coroutines, vision LOD, perception LOD, boss override, squads, BigBrain layer eval                                       |
| **BigBrain**                  | Yes                                                                                                                                                                           | Full control | Layer arbitration — **State Tree migration optional / not shipped** in SAIN fork (helper lives on `SAINLayer` only)                                                                                   |
| **LootingBots**               | Yes                                                                                                                                                                           | Full control | State reset wiring for pool                                                                                                            |
| **Waypoints**                 | Yes                                                                                                                                                                           | Full control | Path cache for recycled bots, objective navigation                                                                                     |
| **SPT-AILimit**               | Yes                                                                                                                                                                           | Full control | Pool compatibility, distance tracking                                                                                                  |
| **botplacementsystem (ABPS)** | Yes                                                                                                                                                                           | Full control | Pool interceptor in spawn pipeline                                                                                                     |
| **spt-unda**                  | **DROPPED** — conflicts with ABPS population caps. PMC wave generation bypasses limits we need for budget scheduler. Zone opening logic will be extracted into ABPS directly. |              |                                                                                                                                        |
| **Questing Bots**             | Yes (broken on current SPT)                                                                                                                                                   | Full control | Objective system extraction — BotZone tracking, objective types, pathfinding integration (study-only until SPT compatibility is fixed) |
| **MoreBotsAPI**               | Yes                                                                                                                                                                           | Full control | Spawn control API, per-map bot count configuration (integration with pool system)                                                      |


## Verification Strategy

- Profile before/after each phase using SAIN's built-in logging + BepInEx.FPSCounter
- Measure frame time per bot, coroutine CPU time, GC allocations
- **Benchmark target:** Lighthouse with 29 bots (full maxBotCap) at 60 FPS minimum
- Test each phase on Customs (19 bots), Lighthouse (29 bots), and Streets (48 bots)

## Implementation Order & Cross-Mod Integration

### Critical Dependency Chain

The phases are ordered by dependency — later phases build on earlier ones. But within phases, cross-mod ordering matters:

```
Phase 1 (SAIN only, no deps)
    ↓
Phase 2.2 Budget Scheduler (SAIN only, FOUNDATION — everything depends on this)
    ↓
Phase 2.1 Vision LOD (SAIN only, uses budget scheduler)
    ↓
Phase 2.5 Perception LOD + Offline Combat
    ├── SAIN: PerceptionTier, SAINAILimit rewrite, boss override removal
    ├── Waypoints: Objective navigation paths (references Questing Bots design)
    ├── Questing Bots: STUDY ONLY — extract BotZone tracking, objective types
    └── SPT-AILimit: Compatibility patch (see below)
    ↓
Phase 3 Squad Collapse
    ├── SAIN: TalkClass bypass, squad coordinator + CombatSquadLayer leader hook
    └── BigBrain: State Tree migration — **only optional SAINLayer cache helper in fork** (wrapper migration TBD)
    ↓
Phase 4 Object Pooling (touches ALL mods)
    ├── EFT Core: Harmony patches on spawn/destroy
    ├── ABPS: Pool interceptor in spawn pipeline
    ├── SAIN: BotComponent state reset
    ├── BigBrain: Layer lifecycle (Stop/Start on recycle)
    ├── LootingBots: LootingBrain state reset
    ├── SPT-AILimit: Pool vs SetActive coordination
    ├── Waypoints: Path cache invalidation
    └── MoreBotsAPI: Spawn control pool integration
```

### Mod-by-Mod Change Summary

**SAIN (primary target, most changes):**


| Phase | Files                                          | What Changes                             |
| ----- | ---------------------------------------------- | ---------------------------------------- |
| 1.1   | `Classes/Bot/BotBase.cs`                       | TickInterval default 0f → 1f/30f         |
| 1.2   | 6 job files                                    | yield return null → WaitForSeconds       |
| 1.3   | Preset config                                  | Enable PerformanceMode + AILimit         |
| 2.1   | `Classes/BotManager/Jobs/VisionRaycastJob.cs`  | LOD-tier raycast count reduction         |
| 2.2   | **NEW** `Components/AIFrameBudgetScheduler.cs` | 2ms hard cap, tiered priority processing |
| 2.2   | `Components/BotComponent.cs`                   | Wire ManualUpdate() through scheduler    |
| 2.5   | `Classes/Bot/SAINAILimit.cs`                   | CheckDistances() → CheckPerception()     |
| 2.5   | `Classes/Bot/BotBase.cs`                       | TickInterval tied to PerceptionTier      |
| 2.5   | `Components/BotComponent.cs`                   | Skip tick groups per tier                |
| 2.5   | **NEW** `Components/CombatAudioSpoofer.cs`     | Audio faking for offline combat          |
| 3.1   | `Classes/Bot/Talk/SAINBotTalkClass.cs`         | Squad awareness bypass                   |
| 3.2   | `SAIN/SAIN/Layers/Combat/Squad/SquadCombatCoordinator.cs` | Coordinator logic                          |
| 3.2   | `SAIN/SAIN/Layers/Combat/Squad/CombatSquadLayer.cs`       | Leader calls `CoordinateSquad`             |
| 4     | `Components/BotComponent.cs`                   | BotComponent state reset for pool        |


**BigBrain:**


| Phase | Files                            | What Changes                              |
| ----- | -------------------------------- | ----------------------------------------- |
| 3.3   | `SAIN/SAIN/Layers/SAINLayer.cs`  | Optional `CheckIsActiveWithCache()` (helper only) |
| 3.3   | *(not in fork)* `Internal/CustomLayerWrapper.cs`, `Brains/BrainManager.cs` | Planned BigBrain arbitration changes |
| 4     | `Internal/CustomLayerWrapper.cs` | Stop()/Start() lifecycle for pool recycle |


**Waypoints:**


| Phase | Files               | What Changes                                            |
| ----- | ------------------- | ------------------------------------------------------- |
| 2.5   | Pathfinding patches | Cache path results per BotZone for objective navigation |
| 4     | NavMesh patch       | Invalidate path cache on pool recycle                   |


**SPT-AILimit:**


| Phase | Files          | What Changes                                                         |
| ----- | -------------- | -------------------------------------------------------------------- |
| 2.5   | `Component.cs` | Skip deactivation for offline-tracked bots (they're not GameObjects) |
| 2.5   | `Component.cs` | Fix LINQ/Min allocation hotspot                                      |
| 4     | `Component.cs` | Re-register recycled bots in distance tracking                       |


**LootingBots:**


| Phase | Files                         | What Changes                                                |
| ----- | ----------------------------- | ----------------------------------------------------------- |
| 4     | `Controllers/LootingBrain.cs` | Reset ScanScheduler, ActiveLootCache, LootFinder on recycle |
| 4     | `Controllers/LootFinder.cs`   | Clear search state on recycle                               |


**ABPS (botplacementsystem):**


| Phase | Files                      | What Changes                                  |
| ----- | -------------------------- | --------------------------------------------- |
| 4     | `BotOwnerCreationPatch.cs` | Pool check before creation                    |
| 4     | Spawn patches              | Route recycled bots through normal spawn init |


### Known Conflict Points & Resolutions

**1. SPT-AILimit vs Perception LOD vs Offline Combat**

Three systems now control bot "liveness":

- SPT-AILimit: `GameObject.SetActive(false)` at extreme range
- Perception LOD: Full → Audible → Occluded throttling
- Offline Combat: Statistical model, no GameObject

Resolution order:

```
Is bot in offline squad? → YES → Statistical model only (skip both AILimit and Perception)
Is bot beyond AILimit range? → YES → SetActive(false) (nuclear option)
Else → Apply Perception tier (Visible/Audible/Occluded)
```

**2. Budget Scheduler vs Coroutine Jobs**

Current SAIN has 5+ coroutine jobs running independently. The budget scheduler must either:

- (A) Replace coroutines entirely — scheduler ticks jobs within budget
- (B) Wrap coroutines — scheduler enables/disables coroutine execution based on budget

Recommendation: (A) for Phase 2.2. Replace coroutine scheduling with direct tick from `AIFrameBudgetScheduler.ProcessFrame()`. This eliminates the `yield return null` problem (Phase 1.2) entirely — no coroutines, no yields.

**3. BigBrain State Tree Migration vs Existing Layer Registration**

BigBrain's `ShallUseNow()` evaluates ALL layers every tick. Phase 3.3 originally targeted "active layer + transitions only" inside BigBrain without breaking `BrainManager.AddCustomLayer()` registration. **This fork has not changed BigBrain arbitration** — only an optional `SAINLayer` cache helper exists (see Phase 3 section above).

**4. Object Pool vs EFT Spawn Lifecycle**

EFT's spawn pipeline has multiple stages: wave generation → profile loading → BotOwner creation → component attachment → BigBrain layer injection → SAIN init → LootingBots init. The pool must intercept at the right point (after profile assignment, before component init) and trigger all downstream systems as if it were a fresh spawn.

**5. Offline → Online Transition Timing**

When a player approaches an offline squad's zone, materialization must happen early enough that bots are "ready" when the player can see them. Recommended: materialize at 1.5× perception radius (if player can hear at 60m, materialize offline squad at 90m). This gives bots ~0.5-1.0 seconds to initialize before the player can perceive them.

### Recommended Build Order (Practical Sequence)

```
STEP 0: Baseline profiling (SAIN logging + BepInEx.FPSCounter)
  Measure: ms per frame, ms per bot, GC allocs on Lighthouse with 10 bots

STEP 1: Phase 1.1 — TickInterval fix (1 line, immediate test)
  Verify: bot classes drop from per-frame to 30Hz

STEP 2: Phase 1.3 — Enable PerformanceMode (config only)
  Verify: config takes effect, note FPS improvement

STEP 3: Phase 2.2 — Budget Scheduler (FOUNDATION, build first)
  Create AIFrameBudgetScheduler.cs
  Wire BotComponent.ManualUpdate() through scheduler
  Hard-code 2ms cap
  Verify: scheduler enforces budget, FPS stabilizes

STEP 4: Phase 2.5 — Perception LOD (core visibility system)
  Rewrite SAINAILimit.CheckDistances() → CheckPerception()
  Add PerceptionTier enum
  Wire TickInterval to perception tier
  Test: verify bots change tier when going behind walls

STEP 5: Phase 2.5 — Boss override removal + AI-vs-AI faking
  Remove boss exemption from perception check
  Add AI-vs-AI detection and simplified resolution
  Test: Lighthouse Rogue/Goon CPU drops when player is across map

STEP 6: Phase 2.5 — Offline combat resolution
  Study Questing Bots BotZone tracking
  Implement statistical combat model
  Add CombatAudioSpoofer for fake gunfire
  Test: offline squads produce audio, corpses appear on approach

STEP 7: Phase 2.1 — Vision LOD (now safe with scheduler)
  Reduce raycasts per tier in VisionRaycastJob
  Test: FPS improvement at distance without visual quality loss

STEP 8: Phase 1.2 — Coroutine throttling (may be OBSOLETE if Step 3 replaced coroutines)
  If budget scheduler replaced coroutines: SKIP this step
  If coroutines still exist: replace yield return null

STEP 9: Phase 3 — Squad collapse
  Shared squad awareness (TalkClass bypass)
  CombatSquadLayer coordinator logic
  BigBrain State Tree migration
  Test: squad CPU drops, behavior unchanged for player

STEP 10: Phase 4 — Object pooling
  Harmony patches on spawn/destroy
  Wire through all mods (SAIN, BigBrain, LootingBots, AILimit, ABPS, Waypoints)
  Test: GC.Alloc drops, no spawn stutter

STEP 11: Full integration test
  Lighthouse: 29 online + 15 offline bots
  Verify: stable 60 FPS, convincing audio, believable world state
```

### What We Don't Build (Yet)

- **Full Questing Bots integration**: Study the objective system, extract the BotZone tracking and objective type definitions, but don't rebuild the full mod. Focus on the lightweight navigation layer for occluded bots.
- **MoreBotsAPI spawn control**: Only needed for Phase 4 pool integration. Phase 1-3 don't touch spawn counts.
- **Threat ring (CoD pattern)**: Deferred to Phase 4.2 — requires pool stability first.

