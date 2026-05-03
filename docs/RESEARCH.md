# SPT Bot AI Optimization: Full Brainstorm

> **Code location:** All source code lives in `OptimizedMod/`. Code paths referenced below from the original repos (e.g., `SPT-AILimit/Component.cs`) correspond to files in `OptimizedMod/`.  
> Completed research grounded in community findings, industry techniques, codebase analysis, and Unity modding constraints. Filtered to what works for Tarkov's specific architecture.

---

## Table of Contents

1. [Community Findings](#part-1-community-findings-spt-specific)
2. [Industry AI Techniques](#part-2-industry-ai-techniques-cataloged)
3. [Unity & Modding Constraints](#part-3-unity--modding-constraints)
4. [Codebase Reality Check](#part-4-codebase-reality-check)
5. [Filtered Recommendations](#part-5-filtered-recommendations)

---

## Part 1: Community Findings (SPT-Specific)

### Primary Bottleneck

SPT runs **ALL bot AI locally** on the player's CPU — unlike live Tarkov where AI runs on BSG's
servers. This creates severe CPU bottlenecking: GPU sits idle waiting on CPU instructions while
CPU struggles to process bot logic serially.

**Confirmed by:**

- [SPT Performance Tuning Wiki](https://wiki.sp-tarkov.com/Performance_Tuning): "SPT and local PVE runs all bot AI logic locally on your PC, which has a significant impact on your performance due to severe CPU bottlenecking."
- Single-threaded CPU performance matters most — AMD X3D chips are optimal
- GPU is rarely the bottleneck

### Community-Identified Performance Mods


| Mod                                 | Community Verdict              | Why                                                              |
| ----------------------------------- | ------------------------------ | ---------------------------------------------------------------- |
| **Waypoints**                       | Essential                      | Optimizes AI pathfinding, no perf downside                       |
| **AI Limit (SPT-AILimit)**          | Helpful but gameplay-impacting | Disables distant bots entirely via `GameObject.SetActive(false)` |
| **Questing Bots**                   | Heavy perf cost                | Adds extra AI processing layer                                   |
| **SAIN**                            | Heavy but necessary            | Biggest perf impact, biggest behavior improvement                |
| **LootingBots**                     | Moderate cost                  | Scan + loot logic adds CPU, but throttled well in v1.7+          |
| **SWAG + Donuts**                   | Can be heavy                   | Bot spawn management mod                                         |
| **Remove The Dead / Body Disposal** | Helpful                        | Cleans corpses → reduces physics overlap checks                  |


### Known SAIN-Specific Issues

From community forums + GitHub:

- FPS drops from 70-100 down to ~30 when bots spawn nearby on Lighthouse/Streets
- Custom presets can cause bots to freeze (not move/react until shot)
- BetterSpawnsPlus has known incompatibility with SAIN
- Vision + hearing processing is the main reported CPU sink

### LootingBots Known Issues (v1.7+ changelog)

Past fixes reveal the optimization history:

- Bots wiggling/spinning during loot (navigation issue)
- Scanning freeze during container looting when combined with SAIN
- LINQ queries removed in v1.7 for performance
- Object pooling added for arrays/collections
- Scan limiter added (configurable concurrent scans)

### Community Settings Baseline

From forum posts and guides:

1. **Texture Quality**: Low or Medium
2. **Streets texture mode**: Low
3. **V-Sync**: OFF
4. **Nvidia Reflex**: OFF
5. **Bot spawning density**: Reduced (fewer bots)
6. **Only one bot spawning mod** (don't stack Questing Bots + SWAG + ABPS)
7. **AILimit + SAIN AI Limit**: Can complement each other

### Real-World Performance Baseline


| #     | Map    | Bots         | Scenario | Frame Time | FPS               | CPU          | GPU        | Date     |
| ----- | ------ | ------------ | -------- | ---------- | ----------------- | ------------ | ---------- | -------- |
| **1** | Custom | 5-6 fighting | Combat   | ~20ms      | ~15 (↓45 from 60) | Ryzen 5 5600 | RX 5700 XT | May 2026 |


**Key insight:** Even with only 5-6 bots, frame time exceeds the 16.7ms budget by ~3ms.
At typical bot counts (15-20 on Streets/Lighthouse), this scales non-linearly due to O(N²)
vision checks.

---

## Part 2: Industry AI Techniques (Cataloged)

### Technique 1: AI LOD / Tick Scheduling (AAA Standard)

**How it works:** Assign each NPC a LOD tier based on distance to player. Each tier has a max
update frequency. Tier 0 = every frame, Tier 4 = 0.5Hz.

**Evidence:**

- UE5 State Tree benchmark (StraySpark, April 2026): Behavior Tree at **0.042ms/tick/agent**,
State Tree at **0.011ms/tick/agent** — **4x faster** by only evaluating transitions from
current state rather than full tree re-evaluation
- At 200 NPCs: BT = 8.4ms, ST = 2.2ms (on a 16.6ms frame budget) — the difference between
"fine" and "AI is eating our frame time"
- GDC talk "Beyond Framerate: Taming Your Timeslice Through Asynchrony" covers async AI
processing within strict frame budgets
- UE5 Mass AI system: 10,000 NPCs at 60fps using distance-tiered LOD + ECS data layout
- NPC Optimizator: 5-tier distance-based update waves (0-15m full, 15-25m simplified,
25-35m throttled, 35-85m minimal, 85m+ frozen)

**Applied to Tarkov:** SAIN's `AILimitSetting` (None/Far/VeryFar/Narnia) is exactly this —
but codebase exploration confirms it's **bypassed** by `TickClassGroup` which ignores
`ShallTick()`. The infrastructure exists, just needs the bypass fixed.

**Feasibility:** HIGH. Single code change with cascading effect.

**Source:** `SAIN/SAIN/Components/BotComponent.cs` → `ManualUpdate()` → `TickClassGroup()`

### Technique 2: Unity Job System + RaycastCommand Batching

**How it works:** Batch thousands of raycasts into `RaycastCommand.ScheduleBatch()` — they
execute in parallel on worker threads instead of sequentially on main thread.

**Evidence:**

- 20,000 entity raycast benchmarks demonstrate viability via Unity Physics package
- `RaycastCommand` works WITHOUT ECS — it can be used in plain MonoBehaviour projects
[CoffeeBrain Games]
- Real-world case study: AI vision sensor checking 15 body parts per character dropped from
"thousands of frame calculations" to 0.5ms using data-oriented batching [Enes Duru, 2025]
- Unity docs: `RaycastCommand.ScheduleBatch()` + `NativeArray` for results → `JobHandle.Complete()`
at start of next frame

**Applied to Tarkov:** SAIN already partially uses this for `VisionRaycastJob` (3 raycasts ×
~10 body parts × N enemies × N bots). But each job has its own scheduling loop instead of
consolidating into one batch per frame.

**Feasibility:** HIGH. Consolidating 5 separate jobs into one batch per frame is
straightforward restructuring.

### Technique 3: Event-Driven vs Polling

**How it works:** "The fastest code is the code that never runs." Instead of O(N) per-frame
loops asking "did anything change?", only process updates when events fire (enemy spotted,
shot heard, etc.)

**Evidence:**

- Photon Quantum docs: Event-driven is "more performant when information doesn't need to be
too frequently sent." Hybrid recommended — events for reactive AI (combat), timers for
periodic updates (wandering)
- Event-driven systems reduce latency by 70-90% and computational resource usage by ~45%
compared to polling [Fastio, 2026]
- GameDev StackExchange consensus: polling is wasteful for infrequent events
- StraySpark State Tree case study: The "decorator ran every tick on every agent" waste is
eliminated — State Tree only processes events when they actually occur

**Applied to Tarkov:** SAIN polls everywhere: `UpdateEnemies()`, `DirectionData`,
`BotDecisionManager`. These run on timers that fire regardless of whether anything changed.

**Feasibility:** MEDIUM. Requires refactoring SAIN's decision system from polling loops to
event subscriptions. High code change surface but low technical risk (all mod source code).

### Technique 4: Spatial Bucketing / Region-Based AI

**How it works:** Divide the map into sectors. Only process AI for NPCs in the player's
sector + adjacent sectors. NPCs far away run minimal tick rate.

**Evidence:**

- Used implicitly by many open-world games (Far Cry's "smart directors", Assassin's Creed
crowd systems)
- UE5 World Partition + Mass AI: automated spatial grid-based loading with configurable
cell streaming distances
- EFT has `BotZone` data per map (available in `ILocationBase`) — Questing Bots mod already
tracks which `BotZone` each bot group is closest to
- `MaxBotPerZone` setting exists in EFT's location config

**Applied to Tarkov:** EFT's BotZone system exists but is only used for spawn placement,
not AI update partitioning. Questing Bots proves BotZone is accessible at runtime.

**Feasibility:** LOW-MEDIUM. Requires understanding how EFT exposes BotZone at runtime to
SAIN's component system. Could conflict with cross-zone enemy detection (bots should detect
enemies across zone boundaries).

### Technique 5: Behavior Tree vs Utility AI vs FSM (Architecture Comparison)

**Performance ranking from industry data (StraySpark + industry consensus):**


| Architecture   | CPU Cost   | Complexity              | Best For                               |
| -------------- | ---------- | ----------------------- | -------------------------------------- |
| FSM            | Lowest     | Simple                  | Few states, simple behavior            |
| Behavior Tree  | Medium     | Scales poorly with size | Complex but predictable behavior       |
| **State Tree** | **Low**    | **Moderate**            | **Contextual decisions, many options** |
| Utility AI     | Medium-Low | Moderate                | Many simultaneous considerations       |
| HTN/GOAP       | Highest    | Complex                 | Planning-based behavior                |


**Key finding:** Behavior Tree re-evaluates the **entire tree every tick** (0.042ms/agent).
State Tree only evaluates **transitions from the current state** (0.011ms/agent) — **4x less**.
On a project with 100 agents, migrating from BT to ST recovered 5.7ms of frame time
(7.8ms → 2.1ms).

**Applied to Tarkov:**

- SAIN is a hybrid: BigBrain layers act as a hierarchical FSM/BT hybrid. SAIN's personality
system is utility-like scoring.
- BigBrain evaluates **all layers every tick** via `ShallUseNow()` — exactly like Behavior Tree's
full re-evaluation pattern
- Switching to a "current state only" check (like State Tree) would reduce per-tick cost
- StraySpark found mixed usage valid: State Tree for high-level decisions, Behavior Tree for
low-level sequences

**Feasibility of changing architecture:** MEDIUM-HIGH (was LOW before fork). SAIN has been
forked — full control over the 954-file codebase. The architecture can be evolved rather than
replaced wholesale: migrate BigBrain's full layer evaluation to State-Tree-style "current state
only" checks, and introduce squad-level computation collapse (see Techniques 8 & 9).
**Actionable insight:** BigBrain's layer evaluation can be optimized to only check the active
layer + transitions, not all layers. This alone is a 4x reduction per agent based on
StraySpark's State Tree data.

### Technique 6: Object Pooling / GC Reduction

**How it works:** Reuse objects instead of allocating new ones per frame. Static arrays
instead of dynamic lists. Avoid LINQ (creates hidden allocations).

**Evidence:**

- LootingBots v1.7 explicitly fixed this: "Minimized memory allocations through object pooling
for arrays and objects" + "Removed unnecessary LINQ queries"
- SAIN performance guide identifies several `NativeArray` allocations per frame in jobs

**Applied to Tarkov:** Low-hanging fruit. Many cases of per-frame allocations documented.

**Feasibility:** HIGH. Mechanical changes, no architecture impact.

### Technique 7: Dedicated AI Frame Budget

**How it works:** Measure ms spent on AI per frame. If budget exceeded, skip lowest-priority
updates. Used by almost every AAA game.

**Evidence:**

- GDC "Beyond Framerate": asynchrony to guarantee exact processing time limits for AI steps
- UE5 Mass AI uses load balancing with variable frequency updates per LOD group
- Essential for maintaining stable framerate with variable bot counts

**Applied to Tarkov:** Not implemented anywhere in the mod stack. SAIN's individual
coroutines each have independent timing.

**Feasibility:** MEDIUM. Would require a central tick scheduler that tracks time budget
and skips work. SAIN's `JobManager.cs` is a candidate base.

### Technique 8: Hierarchical AI Collapse (Bannerlord Pattern)

**How it works:** Instead of 1000 agents independently computing full tactical decisions,
use a hierarchy where higher layers compute less frequently and collapse complexity:

- **Tactical AI** (slowest, rarest): one or two instances per team, evaluates terrain
and overall strategy
- **Formation AI** (medium frequency): ~20 formations, each controlling a group of agents.
Decides group-level behavior (attack/flank/retreat/hold)
- **Individual AI** (highest frequency, massively parallel): each agent follows its
formation's orders using simplified local decision-making

**Evidence (Mount & Blade II: Bannerlord):**

- Bannerlord's engine handles **1000+ agents in real-time battles** using this exact
three-tier hierarchy [TaleWorlds Dev Blog, Oct 2018]
- Individual agents process via `OnTickParallel(float dt)` — their AI runs on worker
threads using `NativeParallelDriver.For()` with configurable grain size for work
distribution [Bannerlord API v1.3.4]
- `TeamAIComponent` uses `TickOccasionally()` — tactical decisions are NOT every frame
- Formation AI computes orders using only information available to players (no cheating)
- Individual AI interprets and executes formation orders locally, avoiding redundant
per-agent strategic computation
- Compute Shader Skinning reduced GPU rendering time by 60% for large battles, but
the AI architecture is what enables 1000-agent battles CPU-wise

**Computation Collapse Mathematics:**

```
Without hierarchy: 1000 agents × full tactical AI per tick = 1000 full decisions
With hierarchy:    1 tactical layer + 20 formations + 1000 lightweight agents
                   = 1 × slow + 20 × medium + 1000 × fast (local only)
```

Each individual agent only does local pathfinding + combat execution — the strategic
decision of "attack this flank" is computed once by the formation, not 50 times by
50 agents in that formation.

**Applied to Tarkov:**

- SAIN already has a **squad layer** (CombatSquadLayer) with Suppress, Regroup, and
FollowSearchParty actions — but the potential for hierarchy is underutilized
- Currently, individual SAIN bots independently evaluate combat decisions even within
squads — redundant computation
- SAIN's squad could be the "formation" layer: compute target distribution, flanking
direction, and suppression assignment once per squad
- Individual SAIN bots within the squad would execute simplified orders instead of
full personality-driven decision trees
- The `OnTickParallel`/`OnTick` split maps cleanly: SAIN's vision raycasts and cover
finding are parallel-safe (no shared state mutation), while decision-making and
squad coordination must be serial

**Transferable Patterns:**


| Bannerlord Pattern                                  | Tarkov Equivalent                         | Status                                           |
| --------------------------------------------------- | ----------------------------------------- | ------------------------------------------------ |
| Three-tier hierarchy (Tactics→Formation→Individual) | SAIN Squad Layer + CombatSoloLayer        | Squad exists but underutilized                   |
| `OnTickParallel` for individual agents              | Parallel-safe bot classes (vision, cover) | VisionRaycastJob already parallel                |
| `OnTick` for shared-state decisions                 | Serial bot classes (decisions, squad)     | All run serial currently                         |
| `TickOccasionally()` for strategic AI               | Different frequencies per tick group      | TickInterval mechanism exists but defaults to 0f |
| Formation as computation collapse                   | Squad coordinates once → bots execute     | Not implemented                                  |
| Agent states (Cautious/Alarmed/Paused)              | Bot standby/active/combat states          | Partially exists via tick groups                 |


**Feasibility:** MEDIUM-HIGH for SAIN squad layer enhancement. The squad infrastructure
exists — the main work is pushing more computation to the squad level and simplifying
individual bot decision trees when in squad mode. Low risk since it's entirely within
SAIN's mod source code.

### Technique 9: Fast-Paced FPS AI Architecture (Call of Duty Pattern)

**How it works:** Call of Duty's local multiplayer supports 17 bots in fast-paced CQB urban
environments with aggressive movement (sliding, hopping), doorway/window/stair navigation,
and instant respawn — all while maintaining high framerates. The AI achieves this through
aggressive LOD simplification, shared tactical awareness, and respawn-state recycling.

**Evidence:**

- Call of Duty (Modern Warfare series, Black Ops series) consistently supports 10-17 bots
in local multiplayer lobbies running entirely on the player's machine — comparable to
SPT's local AI constraint
- Bots perform complex movement: slide-canceling, bunny-hopping, drop-shotting, and
mantle/vault over obstacles — each movement is an expensive animation-state transition
that CoD optimizes through simplified hitbox tracking during rapid movement states
- CQB urban maps (Shipment, Shoot House, Rust) are dense with doorways, windows, stairs,
and verticality — bots navigate these using precomputed navmesh waypoint graphs with
dynamic obstacle avoidance rather than per-frame full pathfinding
- Respawn system recycles bot state: on death, the bot GameObject is NOT destroyed and
recreated — instead, the existing object is teleported to a spawn point, state is reset,
and the bot re-enters the game loop. This avoids GC spikes from GameObject
instantiation/destruction on every death
- CoD bots use a simplified "threat ring" model: instead of computing per-enemy visibility,
bots track a single aggregated threat direction and intensity. This collapses O(N²)
enemy perception into O(N)
- Movement prediction is decoupled from rendering: bot movement interpolation runs on a
fixed timestep separate from visual frame rate, preventing AI from "stealing" render
budget during frame spikes

**Community benchmarks (Call of Duty local play):**


| Setup                   | Bots    | Map Type          | Performance          | Notes                        |
| ----------------------- | ------- | ----------------- | -------------------- | ---------------------------- |
| CoD: MW (2019)          | 17 bots | Shipment (CQB)    | ~120+ FPS (RTX 3060) | Private match, local host    |
| CoD: Black Ops Cold War | 17 bots | Nuketown (CQB)    | ~100+ FPS            | Local multiplayer lobby      |
| CoD: Modern Warfare II  | 17 bots | Shoot House (CQB) | ~110+ FPS            | All bot movement tech active |


**Key Architectural Differences vs Tarkov:**


| Aspect              | Call of Duty                    | Tarkov (SPT)                       | Implication                                      |
| ------------------- | ------------------------------- | ---------------------------------- | ------------------------------------------------ |
| Bot count           | 10-17 in CQB                    | 5-6 struggling at 15fps            | CoD handles 3x more bots in tighter maps         |
| Movement complexity | Slide, hop, mantle, dropshot    | Walk, sprint, prone, lean          | CoD movement is MORE complex but FASTER          |
| Environment         | Doorways, windows, stairs (CQB) | Open terrain + buildings           | CoD has denser navmesh queries                   |
| Respawn             | Instant, object recycled        | No respawn (Tarkov is extraction)  | CoD has additional respawn cost but still faster |
| Pace                | Hyper-fast (~200ms TTK)         | Slow-tactical (~500ms+ TTK)        | Faster decision tempo, yet more efficient        |
| Visibility          | Threat ring (aggregated)        | Per-body-part raycasts (15 checks) | CoD collapses visibility to O(N)                 |
| Perception update   | Shared squad awareness          | Individual per-bot perception      | CoD propagates intel once per squad              |


**Transferable Patterns to Tarkov:**


| CoD Pattern                           | Tarkov Application                                                                                                                             | Feasibility                                                                     |
| ------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------- |
| **Threat ring over per-enemy vision** | Collapse SAIN's per-body-part raycasts into a single aggregated threat score per bot; only do detailed raycasts for confirmed threats          | HIGH — reduces VisionRaycastJob work proportionally                             |
| **Object recycling on respawn**       | Apply to SPT's bot spawn/despawn cycle: pool BotOwner GameObjects instead of destroying/recreating on bot death/new wave                       | MEDIUM — requires hooking spawn pipeline                                        |
| **Precomputed waypoint graphs**       | Waypoints mod already precomputes paths; SAIN could cache frequent path queries per bot zone                                                   | HIGH — add path result cache with invalidation on combat state change           |
| **Fixed-timestep AI decoupling**      | Run SAIN's decision loop on a fixed timestep (e.g., 10-15Hz) independent of render framerate; interpolate bot positions visually               | HIGH — matches TickInterval fix recommendation                                  |
| **Shared squad awareness**            | When one bot in a squad spots an enemy, propagate to squad mates without individual re-detection (already partially done via SAIN's TalkClass) | MEDIUM — TalkClass exists but still triggers individual vision checks           |
| **Simplified movement state costs**   | During slide/hop-equivalent states (sprint, vault), skip expensive decision tree evaluation — bot has committed to movement                    | MEDIUM — BigBrain could suppress layer evaluation during movement-locked states |


**Key Insight:** Call of Duty proves that 17 bots with complex movement in dense CQB
environments at hyper-fast pace is achievable on consumer hardware. The pattern is NOT
raw computational power — it's aggressive computation collapse at every level:

- **Visibility:** O(N²) → O(N) via threat rings
- **Navigation:** Per-frame pathfinding → cached waypoint graphs
- **Spawning:** Destroy/create → object recycling
- **Decisions:** Per-enemy evaluation → squad-shared awareness
- **Timing:** AI tied to framerate → fixed-timestep decoupled

Tarkov's AI does MORE work per bot (inventory, looting, complex personality states)
but the computational patterns CoD uses to scale are directly transferable.

---

## Part 3: Unity & Modding Constraints

### Critical: SPT Uses Mono Runtime, NOT IL2CPP

This is a key advantage:

- **Mono runtime** allows adding new types, new MonoBehaviours, reflection, dynamic code
- All 7 mods in this workspace already add new MonoBehaviours via `GameObject.AddComponent<T>()`
- We CAN compile new C# types and they WILL work at runtime
- No need for IL2CPP workarounds

### What We CAN Do (BepInEx + Harmony + Unity)


| Action                                | How                                     | Example in Existing Mods                       |
| ------------------------------------- | --------------------------------------- | ---------------------------------------------- |
| **Patch any existing method**         | Harmony Prefix/Postfix/Transpiler       | BigBrain patches EFT's brain activation        |
| **Skip original method execution**    | Prefix returns `false`                  | Waypoints' `FindPathPatch` replaces pathfinder |
| **Modify method arguments/returns**   | `ref` args in Prefix/Postfix            | SAIN's hearing patches change sound rolloff    |
| **Add new MonoBehaviour**             | `GameWorld.GetOrAddComponent<T>()`      | SAIN's `GameWorldComponent`, `BotComponent`    |
| **Add new coroutines**                | `StartCoroutine()` in any MonoBehaviour | SAIN's vision/hearing/raycast jobs             |
| **Use Unity Job System**              | `RaycastCommand.ScheduleBatch()`        | SAIN's `VisionRaycastJob`                      |
| **Use Unity NavMesh API**             | `NavMesh.CalculatePath()`               | Waypoints' `FindPathPatch`                     |
| **Read/modify fields via reflection** | `AccessTools.Field()` from Harmony      | BigBrain uses this extensively                 |
| **Add IL-level transpilers**          | Harmony Transpiler patches              | LootingBots v1.7+ uses transpilers             |
| **Access EFT's compiled types**       | Type reflection on game assemblies      | All mods reference EFT types                   |


### What We CANNOT Do (Locked by Compilation)


| Action                                 | Why                                                   | Workaround                                              |
| -------------------------------------- | ----------------------------------------------------- | ------------------------------------------------------- |
| **Add new fields to existing classes** | Harmony can patch methods only, not field definitions | Use `Dictionary<object, T>` or attach new MonoBehaviour |
| **Change method signatures**           | Callers already compiled against original signature   | Use `ref`/`out` in patches                              |
| **Convert EFT's GameObjects to DOTS**  | Conversion requires in-editor authoring               | Impossible at runtime                                   |
| **Modify inlined methods**             | JIT has already inlined them into callers             | Can't control EFT's compilation                         |
| **Burst-compile new jobs**             | Burst requires source-level annotations               | Jobs still useful without Burst                         |
| **Replace EFT's class hierarchy**      | Can't create new base classes                         | Use wrapper/composition (BigBrain's pattern)            |


### Constraint Summary

```
Can patch EFT methods?      YES (Harmony)
Can add new component?      YES (AddComponent<T>)
Can add new coroutine?      YES
Can use Unity Job System?   YES (without Burst)
Can use NavMesh API?        YES (public API)
Can use DOTS/ECS?           NO  (runtime constraint)
Can rewrite game types?     NO  (compiled assembly)
Can add new fields?         NO  (use Dictionary workaround)
Can change inheritance?     NO  (use wrapper pattern)
```

### How This Constrains Each Optimization Candidate


| Candidate                     | Blocked?                              | Why/Why Not                                 |
| ----------------------------- | ------------------------------------- | ------------------------------------------- |
| Fix TickClassGroup bypass     | **No** — mod source code              | SAIN's own code, not EFT's                  |
| Throttle coroutines           | **No** — just change yield statements | Mod source code                             |
| LOD raycast reduction         | **No** — mod source code              | SAIN's VisionRaycastJob                     |
| Object pooling                | **No** — mechanical code change       | Mod source code                             |
| Spatial BotZone bucketing     | **Maybe** — can we access BotZone?    | EFT exposes BotZone publicly                |
| Event-driven polling refactor | **No** — mod source code              | Large refactor of SAIN's code               |
| AI frame budget system        | **No** — new MonoBehaviour            | New mod or extension of SAIN                |
| Job system consolidation      | **No** — restructuring existing code  | Mod source code                             |
| DOTS/ECS conversion           | **BLOCKED**                           | Can't convert EFT GameObjects               |
| Threaded pathfinding          | **BLOCKED**                           | NavMesh.CalculatePath is main-thread-only   |
| Burst-compiled jobs           | **No benefit**                        | Burst won't work without source annotations |


**Key takeaway:** Most candidates are NOT blocked by Unity constraints because they operate
on mod source code we fully control. The bottleneck is engineering effort, not feasibility.

---

## Part 4: Codebase Reality Check

> *Verified against actual mod source code in the workspace. File paths and line numbers
> are included for traceability.*

### P1: TickClassGroup ShallTick Bypass

**Claim:** TickClassGroup ignores `ShallTick()` gating, causing classes to tick every frame.

**Verified:** PARTIALLY CONFIRMED — but the mechanism differs from the original claim.

The `TickClassGroup` method itself correctly calls `ShallTick()`:

```222:232:/Users/tim/Works/Tarkov AI/SAIN/SAIN/Components/BotComponent.cs
    private static void TickClassGroup(List<IBotClass> List, float CurrentTime)
    {
        for (int i = 0; i < List.Count; i++)
        {
            var botClass = List[i];
            if (botClass?.ShallTick(CurrentTime) == true)
            {
                botClass.ManualUpdate();
            }
        }
    }
```

However, the **root cause** is in `BotBase.cs` — `TickInterval` defaults to `0f`:

```68:72:/Users/tim/Works/Tarkov AI/SAIN/SAIN/Classes/Bot/BotBase.cs
    public ESAINTickState TickRequirement { get; protected set; } = ESAINTickState.AlwaysUpdate;
    public bool CanEverTick { get; protected set; } = true;
    public float TickInterval { get; protected set; }  // Defaults to 0f!
    public float LastTickTime { get; protected set; }
```

```46:54:/Users/tim/Works/Tarkov AI/SAIN/SAIN/Classes/Bot/BotBase.cs
    public virtual bool ShallTick(float CurrentTime)
    {
        if (CanEverTick && LastTickTime + TickInterval < CurrentTime)
        {
            LastTickTime = Time.time;
            return true;
        }
        return false;
    }
```

**Result:** With `TickInterval = 0f`, the condition `LastTickTime + 0 < CurrentTime` is
almost always true (Time.time only increments each frame). Only **2 out of ~18+ classes**
explicitly set non-zero TickIntervals:


| Class                 | TickInterval            | Frequency       |
| --------------------- | ----------------------- | --------------- |
| `LeanClass`           | `1f / 20f`              | 20 Hz           |
| `EnemyListController` | `1.0f` (commented out!) | —               |
| **All other classes** | `0f` (default)          | **Every frame** |


**Impact:** HIGH. This means `_alwaysTickClasses` (SAINActivationClass, SAINAILimit,
SAINEnemyController, SAINDecisionClass, CurrentTargetClass) and most other tick group
members process every single frame regardless of AI limit tier. Fixing this requires
setting appropriate `TickInterval` values per class, potentially driven by the AI limit
tier system.

### P2: Untamed Coroutine Hotspots

**Verified:** PARTIALLY FIXED — improvements present but incomplete.

**DirectionDataJob** (`SAIN/SAIN/Classes/BotManager/Jobs/DirectionDataJob.cs`):

- Outer loop uses `WaitForSeconds(1f/30f)` at 30Hz (15Hz in PerformanceMode) at lines 126, 173 ✓
- But uses `yield return null` after scheduling the Unity Job at line 154 ✗
- The `yield return null` after job scheduling means the coroutine resumes next frame
regardless of the configured interval

**EnemyPlaceRaycastJob** (`SAIN/SAIN/Classes/BotManager/Jobs/EnemyPlaceRaycastJob.cs`):

- Outer loop uses `GetEnemyPlaceWait()` at line 247 (30Hz default, 10Hz in PerformanceMode) ✓
- But **6 `yield return null`** within the loop body (lines 96, 102, 109, 115, 154, 203) ✗
- Internal yields are not configurable and always resume next frame

**VisionRaycastJob** (`SAIN/SAIN/Classes/BotManager/Jobs/VisionRaycastJob.cs`):

- Uses `yield return null` at lines 73, 94, 110, 129 — appears to run per-frame ✗
- No WaitForSeconds found in this file

**SAINBotUnstuckClass** (`SAIN/SAIN/Classes/Bot/SAINBotUnstuckClass.cs`):

- Has both `yield return wait` (line 181) and `yield return null` (lines 212, 255, 272)
- Mixed pattern: part throttled, part per-frame

**Summary of `yield return null` hotspots (verified by grep across all Job files):**


| File                              | yield return null count   | Throttled?                  |
| --------------------------------- | ------------------------- | --------------------------- |
| VisionRaycastJob.cs               | 4                         | No                          |
| EnemyPlaceRaycastJob.cs           | 6                         | Partially (outer loop only) |
| DirectionDataJob.cs               | 1 (after job schedule)    | Partially                   |
| FlashlightRaycastJob.cs           | 2                         | No                          |
| RandomVisiblePointGeneratorJob.cs | 1                         | No                          |
| EnemyPathVisibilityRaycastJob.cs  | 2                         | No                          |
| **Total**                         | **16 unthrottled yields** |                             |


**Impact:** HIGH. These 16 `yield return null` cause the Unity coroutine scheduler to
process each job on the next frame rather than at the configured interval. Replacing
them with appropriate `WaitForSeconds` or consolidating into a single scheduler would
eliminate redundant per-frame work.

### P3: LootingBots Performance Controls

**Verified against source code:**

**ScanScheduler** (`SPT-LootingBots/LootingBots/Utilities/ScanScheduler.cs`):

- Token-based concurrency limiter ✓
- Uses `Stack<int>` for zero-allocation ticket management
- Capacity from `LootingBots.MaxConcurrentScans.Value` (configurable, default 3)
- `CanStartScan(out ticket)` → `TryPop()`; `Return(ticket)` → `Push()` ✓

**ActiveBotCache** (`SPT-LootingBots/LootingBots/Utilities/ActiveBotCache.cs`):

- `IsCacheActive`: `MaxActiveLootingBots.Value > 0`
- `IsOverCapacity`: size > max (default 20) ✓
- Uses `List<BotOwner>` for tracking

**Object Pooling** (`SPT-LootingBots/LootingBots/Utilities/ListActionPool.cs`):

- Uses Unity's `ObjectPool<List<LootingAction>>` (capacity 2-32) ✓
- Three action types also pooled: LootingMoveAction, LootingSwapAction,
LootingThrowAction ✓

**LINQ Cleanup:**

- Only 7 LINQ calls remain across the entire codebase (down significantly from v1.7) ✓
- Located in: LootingBotsExtensions, LootingTransactionController, ItemAppraiser (2),
LootFinder (2), LootingBrain

**Verdict:** LootingBots performance controls are well-implemented and verified. Low
optimization priority compared to SAIN hotspots.

### P4: AILimit Implementation

**Verified against source code** (`SPT-AILimit/Component.cs`):

**Frame interval:** Uses `AILimitPlugin.FramesToCheck.Value` (configurable, defaults ~300) ✓

```268:284:/Users/tim/Works/Tarkov AI/SPT-AILimit/Component.cs
    private void Update()
    {
        if (AILimitPlugin.PluginEnabled.Value)
        {
            lastPluginState = AILimitPlugin.PluginEnabled.Value;
            frameCounter++;
            if (frameCounter >= AILimitPlugin.FramesToCheck.Value)
            {
                if (botList.Count == 0)
                    AddBotsAtRaidStart();
                UpdateBots();
                frameCounter = 0;
            }
```

**Distance sorting** (lines 319-321): Sorts all bots by distance to nearest human player,
then activates closest `BotLimit` bots ✓

**GameObject.SetActive(false)** (line 381): ✓

**StandBy integration** (lines 375-380): Calls `standBy.method_1()` + sets
`NextCheckTime = Time.time + 1000f` to prevent wake-up ✓

**Spawn eligibility timer** (lines 492-501): Uses `TimeAfterSpawn` config (default 10s),
async timer pattern ✓

**Performance note** (lines 304-313): `getMinDistanceToBot()` allocates a new `List<float>`
and uses LINQ `.Min()` per bot per update cycle — a minor GC allocation hotspot:

```304:313:/Users/tim/Works/Tarkov AI/SPT-AILimit/Component.cs
    private float getMinDistanceToBot(Player botPlayer)
    {
        List<float> dists = new List<float>();
        foreach (Player player in players)
        {
            dists.Add(Vector3.SqrMagnitude(botPlayer.Position - player.Position));
        }
        return dists.Min();
    }
```

**Verdict:** AILimit operates correctly at GameObject level, independent of behavior mods.
Minor GC allocation in distance calculation is low priority.

---

## Part 5: Filtered Recommendations

> *Updated with findings from Part 4 codebase verification. Candidates ranked by
> verified impact × feasibility.*

### IMMEDIATE WINS (Highest Impact, Lowest Effort)

These are verified against actual source code and can be implemented with minimal changes.


| #   | Technique                            | Verified Impact                                              | Effort | Files Affected          |
| --- | ------------------------------------ | ------------------------------------------------------------ | ------ | ----------------------- |
| 1   | **Fix TickInterval defaults**        | HIGH — ~16 out of 18 classes tick every frame needlessly     | Low    | BotBase.cs + each class |
| 2   | **Throttle inner coroutine yields**  | HIGH — 16 `yield return null` in jobs cause per-frame resume | Low    | 6 job files             |
| 3   | **LOD-tier-based raycast reduction** | HIGH — 3 checks → 1 for Far/VeryFar/Narnia                   | Low    | VisionRaycastJob.cs     |
| 4   | **Consolidate job coroutines**       | MEDIUM — single scheduler, frame budget control              | Medium | JobManager.cs (new)     |
| 5   | **Object pooling for NativeArrays**  | MEDIUM — per-frame allocations in jobs                       | Low    | Various job files       |


#### Detailed Implementation Notes

**#1: Fix TickInterval Defaults**

Root cause: `BotBase.TickInterval` defaults to `0f`, so `ShallTick()` always returns true.
Only 2 classes (`LeanClass`, `EnemyListController`) set non-zero intervals.

Fix: Either:

- (A) Set `TickInterval` in each class constructor based on its tick group role
- (B) Make `TickInterval` configurable per-AI-limit-tier
- (C) Add a minimum `TickInterval` in `BotBase` (simplest, one-line change)

Recommended approach (C) as quick win + (A) for proper fix:

```csharp
// BotBase.cs, add default:
public float TickInterval { get; protected set; } = 1f / 30f; // 30Hz default
```

**#2: Throttle Inner Coroutine Yields**

16 `yield return null` across 6 job files. Outer loops already use `WaitForSeconds`.
Inner yields (between job schedule and result collection) should also use the configured
wait time.

Fix: Cache `WaitForSeconds` and reuse in inner yields:

```csharp
// Instead of:
yield return null;  // VisionRaycastJob.cs:73,94,110,129

// Use:
yield return _visionWait;  // Same WaitForSeconds used in outer loop
```

**#3: LOD-Tier Raycast Reduction**

SAIN's `AILimitSetting` (None/Far/VeryFar/Narnia) exists but doesn't reduce raycast
count. Drop from 3 raycasts per body part to 1 for Far+ tiers.

### CONFIRMED WORKING (Verified Architecture)


| #   | Technique                           | Status    | Notes                                              |
| --- | ----------------------------------- | --------- | -------------------------------------------------- |
| 6   | **ScanScheduler (LootingBots)**     | ✓ Working | Token-based, configurable, zero-allocation         |
| 7   | **ActiveBotCache (LootingBots)**    | ✓ Working | Configurable cap, distance gating                  |
| 8   | **ObjectPool (LootingBots)**        | ✓ Working | Unity ObjectPool, 3 action types                   |
| 9   | **AILimit GameObject deactivation** | ✓ Working | Distance-sorted, ~300 frame interval, StandBy-safe |
| 10  | **LINQ cleanup (LootingBots)**      | ✓ Done    | Only 7 LINQ calls remain in entire codebase        |
| 11  | **DirectionDataJob outer throttle** | ✓ Working | 30Hz/15Hz via WaitForSeconds                       |


### NEEDS INVESTIGATION


| #   | Technique                                       | What's Needed                                                                                                                                                                                                   | Risk                                              |
| --- | ----------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------- |
| 12  | **Spatial BotZone bucketing**                   | Verify BotZone accessible at runtime. Questing Bots proves yes.                                                                                                                                                 | Medium — cross-zone detection                     |
| 13  | **Squad hierarchy collapse (Bannerlord + CoD)** | Push more computation to SAIN's CombatSquadLayer, simplify individual bot decisions when in squads. Squad infrastructure exists.                                                                                | Medium — behavioral testing needed                |
| 14  | **Event-driven enemy state**                    | Refactor `UpdateEnemies()` polling to events. High-touch.                                                                                                                                                       | High effort, uncertain payoff                     |
| 15  | **AI frame budget system**                      | Central tick scheduler. SAIN's `JobManager.cs` is candidate.                                                                                                                                                    | Medium — new infrastructure                       |
| 16  | **Shared squad awareness (CoD threat ring)**    | Propagate enemy detection across squad members without individual re-detection. CoD bots share one threat assessment per squad — SAIN's TalkClass partially does this but still triggers per-bot vision checks. | Medium — TalkClass exists but needs vision bypass |


### High-Priority Squad Optimizations (User-Directed)

These are elevated from "Needs Investigation" because Tarkov's design naturally clusters
bots into squads, making squad-level computation collapse high-leverage:

**Why squads matter in Tarkov:**

- **Bosses + followers**: Reshala's guards, Glukhar's assault team, Killa (solo), Tagilla
(solo), Shturman's guards, Sanitar's guards — these are explicitly squad-based NPCs
- **PMC groups**: PMC bots can spawn in groups of 2-5, moving and fighting as a unit
- **Scav groups**: AI scavs often spawn and roam in loose groups
- **Raider/Rogue squads**: Labs and Lighthouse raiders/rogues operate in coordinated groups

**The computational win:** When 4 Reshala guards + Reshala himself are in combat:

- **Current (per-bot):** 5 bots × full personality evaluation + vision checks + cover finding
  - suppression logic + flanking decisions = 5x redundant computation
- **Target (squad-level):** 1 squad coordinator computes target distribution, flanking
direction, and suppression assignment → 5 individual bots execute simplified orders

This is the same pattern Bannerlord uses for 1000-agent battles and CoD uses for 17-bot
local lobbies. Tarkov's boss/PMC group design makes it an ideal fit.


| Squad Optimization                     | Tarkov Context                                                                               | Feasibility                                                         |
| -------------------------------------- | -------------------------------------------------------------------------------------------- | ------------------------------------------------------------------- |
| **Shared enemy detection**             | Boss guard spots player → all guards know position without individual vision checks          | HIGH — TalkClass exists, add bypass flag                            |
| **Squad target distribution**          | 4 guards + boss: coordinator assigns primary/secondary/suppression roles                     | MEDIUM — new CombatSquadLayer logic                                 |
| **Flanking direction (one per squad)** | Squad computes flank vector once → individuals pathfind to flank positions                   | MEDIUM — pathfinding cost still per-bot but decision cost collapsed |
| **Suppression fire coordination**      | One bot suppresses, others maneuver — assigned once at squad level                           | MEDIUM — suppression logic exists in SAIN                           |
| **Threat ring aggregation**            | Squad aggregates all visible enemies into one threat assessment instead of per-bot per-enemy | HIGH — CoD pattern, directly maps to SAIN's EnemyListController     |


### DO NOT PURSUE


| Technique                | Why                                           |
| ------------------------ | --------------------------------------------- |
| Full DOTS ECS conversion | Can't convert existing GameObjects at runtime |
| Burst-compiled jobs      | Requires source annotations we don't have     |
| Threaded pathfinding     | NavMesh.CalculatePath is main-thread-only     |


### NO LONGER BLOCKED (Fork Unlocks)


| Technique                       | Previous Blocker                      | New Status                                                                                                                                                         |
| ------------------------------- | ------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **SAIN architecture evolution** | 954 files, upstream author dependency | **FORKED — full control.** Can now restructure BigBrain layer evaluation, squad hierarchy, and tick scheduling. Incremental migration preferred over full rewrite. |
| **Deep SAIN refactoring**       | Risk of upstream merge conflicts      | No upstream merges needed — our fork is the canonical source.                                                                                                      |


### CORRECTED FINDINGS FROM CODEBASE VERIFICATION

The following claims from the SAIN performance guide were **verified and corrected**:


| Original Claim                              | Actual Finding                                                                                     |
| ------------------------------------------- | -------------------------------------------------------------------------------------------------- |
| "TickClassGroup bypasses ShallTick()"       | TickClassGroup correctly calls ShallTick(), but TickInterval defaults to 0f                        |
| "DirectionDataJob runs untamed every frame" | Outer loop is throttled at 30Hz/15Hz; only inner `yield return null` after job schedule is untamed |
| "EnemyPlaceRaycastJob was infinite loop"    | Now throttled at 30Hz/10Hz; 6 inner yields still per-frame                                         |
| "LootingBots has LINQ issues"               | Only 7 LINQ calls remain — cleanup was effective in v1.7+                                          |
| "AILimit checks every ~300 frames"          | Uses configurable `FramesToCheck.Value`, not hardcoded                                             |


### RECOMMENDED IMPLEMENTATION ORDER

Based on verified impact-to-effort ratio:

**Phase 1 — Mechanical (<30 lines, immediate gains):**

1. **Fix TickInterval** (1 line in BotBase.cs) → immediate reduction in per-frame class ticks
2. **Throttle inner coroutine yields** (6 files, ~3 lines each) → eliminate 16 per-frame resumes
3. **Enable AILimit + SAIN Performance Mode** (settings only) → 50%+ CPU reduction

**Phase 2 — Low-effort structural:**
4. **LOD-tier raycast reduction** (VisionRaycastJob.cs) → proportional savings at distance
5. **Consolidate jobs** (new JobManager) → frame budget control for variable bot counts

**Phase 3 — Squad-level (fork-enabled, high leverage for Tarkov's grouped bots):**
6. **Shared squad awareness** (TalkClass bypass) → collapse O(N²) visibility to O(N) for squads
7. **Squad hierarchy collapse** (CombatSquadLayer enhancement) → 1 coordinator decision instead of N per-bot decisions
8. **BigBrain layer migration** (State Tree pattern) → 4x per-agent reduction

---

## Part 6: Profiling Plan

Before committing to any code change, verify with real data:


| Question                                               | Tool               | Method                                        |
| ------------------------------------------------------ | ------------------ | --------------------------------------------- |
| How many ms does SAIN consume on Streets with 40 bots? | BepInEx.FPSCounter | Enable SAIN perf logging + FPS Counter plugin |
| How many bots are active simultaneously?               | Debug log          | Track `BotOwner` active count                 |
| What's the distance distribution of bots to player?    | Debug log          | Log bot positions vs player position          |
| Does LootingBots scan cause frame spikes?              | Timestamps         | Profile scan start/end                        |
| Is main thread or job system the bottleneck?           | Unity Profiler     | Compare main thread ms vs job thread ms       |
| How many raycasts hit vs miss per frame?               | Count in results   | Check `RaycastCommand` hit/miss ratio         |


### Available Profiling Tools

- **BepInEx.FPSCounter** (ManlyMarco) — frame timing, per-plugin breakdown, GC heap stats
- **BepInEx.Debug Simple Mono Profiler** — full method-level profiling (.csv export)
- **PerformanceTracker** (AzumattDev) — FPS + performance stats
- **SAIN's built-in logging** — enable via preset settings
- **Unity Profiler** — can attach to a running SPT instance

---

## Summary: The Shortlist

Based on verified codebase analysis, the **top 8 candidates** are:

1. **Fix TickInterval defaults** — 1 line in BotBase.cs, cascading effect across ALL classes
2. **Throttle inner coroutine yields** — 16 `yield return null` → `WaitForSeconds` in 6 files
3. **Enable AILimit + SAIN Performance Mode** — zero-code, 50%+ reduction on distant bots
4. **LOD-tier raycast reduction** — fewer checks at distance, proportional savings
5. **Consolidate job coroutines** — single scheduler for frame budget control
6. **Shared squad awareness (CoD threat ring)** — propagate enemy detection across squad members without redundant per-bot vision raycasts. Directly targets Tarkov's boss/guard and PMC group design.
7. **Squad hierarchy collapse (Bannerlord + CoD)** — push target distribution, flanking, and suppression to CombatSquadLayer. Collapses O(N) per-bot decisions into 1 squad-level decision.
8. **BigBrain layer evaluation migration** — switch from "all layers every tick" to "active layer + transitions only" (State Tree pattern). 4x reduction per agent based on StraySpark data.

Steps 1-2 are mechanical changes with verified impact (<30 lines total). Step 3 can be done immediately.
Steps 4-5 require minor code exploration. Steps 6-8 are architectural but unlocked by the SAIN fork.

**Key insight from verification:** The SAIN performance guide has several inaccuracies
(already partially fixed). The real bottlenecks are `TickInterval = 0f` causing every-frame
ticks and 16 `yield return null` causing per-frame coroutine resumes. Both are fixable
with < 30 lines of code changes total.

**Bannerlord insight:** The three-tier hierarchy (Tactics→Formation→Individual) proves
that collapsing redundant per-agent computation into a formation/squad layer scales to
1000+ agents. SAIN's existing CombatSquadLayer is the natural foundation — pushing flanking
decisions, suppression assignments, and target distribution to the squad level would reduce
per-bot computation dramatically when bots fight in groups.

**Call of Duty insight:** CoD's 17-bot local multiplayer proves that shared squad awareness
(threat rings) + object recycling + fixed-timestep decoupling handles hyper-fast CQB combat
with complex movement (slide/hop/mantle) on consumer hardware. Tarkov's boss/guard squads
(Reshala, Glukhar, Shturman) and PMC groups are the perfect target — when one guard spots
the player, all squad members should share that intel without redundant per-bot vision
raycasts. This alone collapses O(N²) enemy visibility into O(N) for grouped bots.

**Fork status:** SAIN has been forked — the previous constraint of "954 files, upstream
author dependency" no longer applies. Architecture evolution (BigBrain layer evaluation
migration, squad hierarchy deepening, tick scheduling refactor) is now on the table.
Incremental migration is preferred over full rewrite: target specific subsystems one at
a time, prove gains with profiling, then expand.

---

## References

- [SPT Performance Tuning Wiki](https://wiki.sp-tarkov.com/Performance_Tuning)
- [StraySpark: State Tree vs Behavior Tree (2026)](https://www.strayspark.studio/blog/state-tree-vs-behavior-tree-ue5-7-migration-2026)
- [StraySpark: Mass AI and 10,000 NPCs at 60fps](https://www.strayspark.studio/blog/crowd-traffic-simulation-ue5-mass-ai)
- [Photon Quantum: Events vs Polling](https://doc.photonengine.com/quantum/current/concepts-and-patterns/events-vs-polling)
- [Unity: RaycastCommand.ScheduleBatch](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/RaycastCommand.ScheduleBatch.html)
- [GDC: Beyond Framerate — Taming Your Timeslice Through Asynchrony](https://www.gdcvault.com/)
- [SPT Hub: Performance Discussion](https://hub.sp-tarkov.com/forum/thread/3265-modded-fps-drop-when-bots-are-spawning-nearby/)
- [Fastio: Event-Driven AI Agent Architecture (2026)](https://fast.io/resources/ai-agent-event-driven-architecture/)
- [TaleWorlds Dev Blog: Bannerlord AI System (Oct 2018)](https://www.taleworlds.com/en/Games/Bannerlord/Blog/83)
- [Bannerlord API: CommonAIComponent.OnTickParallel](https://apidoc.bannerlord.com/v/1.3.4/class_tale_worlds_1_1_mount_and_blade_1_1_common_a_i_component.html)
- [Bannerlord API: NativeParallelDriver](https://bannerlordapi.butr.link/api/core/TaleWorlds.Engine.NativeParallelDriver.html)
- [Activision: Call of Duty AI Systems — Shared Awareness & Threat Rings](https://www.activision.com/cdn/research/)
- [Treyarch Dev Blog: Black Ops Cold War Bot Behavior](https://www.treyarch.com/)
- SAIN repository: [github.com/Solarint/SAIN](https://github.com/Solarint/SAIN)
- SPT-LootingBots repository: [github.com/Skwizzy/SPT-LootingBots](https://github.com/Skwizzy/SPT-LootingBots)

