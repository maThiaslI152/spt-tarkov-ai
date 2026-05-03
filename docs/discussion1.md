# Discussion 1: Architecture & Integration Strategy for the Optimization Mod

> This document synthesizes a design discussion for a new SPT Tarkov AI optimization mod,
> covering architectural decisions, cross-mod integration strategy, AI LOD design, tick
> management, implementation priorities, and risk assessment.

---

## Table of Contents

1. [Architecture Decision: Modular vs Monolithic](#1-architecture-decision-modular-vs-monolithic)
2. [Integration Strategy: How This Mod Controls Other Mods](#2-integration-strategy)
3. [AI LOD Strategy](#3-ai-lod-strategy)
4. [Tick Management Approach](#4-tick-management-approach)
5. [Implementation Priorities](#5-implementation-priorities)
6. [Key Design Decisions](#6-key-design-decisions)
7. [Risk Register](#7-risk-register)

---

## 1. Architecture Decision: Modular vs Monolithic

**Decision:** The optimization mod ships as a single user-facing install (one folder in `BepInEx/plugins/`) but is internally organized into a main DLL with optional internal separation if the codebase grows large enough to warrant it.

### Context

The existing SPT ecosystem — BigBrain, SAIN, LootingBots, Waypoints, AILimit, botplacementsystem, Unda — each ships as its own BepInEx plugin producing its own `.dll`. The community expects and prefers modular mod selection (users pick which features to install).

However, this optimization mod is a single coherent product, not a framework like BigBrain. Making users install 5 separate DLLs for one mod is user-hostile.

### The Decision

- **One user-facing install unit** — one mod folder, one download
- **Internally**: one `.dll` initially; if the codebase genuinely grows large (>50 classes, >30k lines), split into `Core.dll` + feature DLLs that ship in the same folder
- **No external `Core.dll` as a separate mod install** — the Core is purely an internal organizational tool

### Rationale

| Factor | Why This Wins |
|--------|---------------|
| Single install | Users install one mod, not a bundle of micro-DLLs |
| Maintenance | One version to track, one release cadence |
| Cross-subsystem optimization | Shared object pools, caches, tick scheduler (no inter-mod reflection needed) |
| Community expectation | Matches AILimit, Waypoints, Unda — each is a single DLL |
| Future-proofing | If the mod grows, internal DLL split is a compile-time change, not a user-facing one |

---

## 2. Integration Strategy

**Decision:** Use a **cooperative suggestion protocol** — this mod measures the performance budget and influences other mods through their existing public surfaces, not by commanding them.

### The Control Hierarchy

```
Your Optimization Mod (MonoBehaviour on GameWorld)
    │
    ├── Measures frame time every tick
    │
    ├── Layer 1: Unity GameObject Level (brute force, works universally)
    │   ├── Like AILimit: GameObject.SetActive(false) on farthest bots
    │   ├── Effect: SAIN, LootingBots, BigBrain all stop ticking on that bot
    │   └── Zero coupling, zero risk
    │
    ├── Layer 2: BigBrain Public API (reliable, designed for external use)
    │   ├── Requires [BepInDependency("xyz.drakia.bigbrain")]
    │   ├── Can call: BrainManager.RemoveLayer/RestoreLayer/AddCustomLayer
    │   ├── Effect: Dynamically disable non-critical layers when under budget pressure
    │   │   e.g., remove LootingLayer from far bots → they stop scanning for loot
    │   └── Low risk — BigBrain's API is stable by design
    │
    ├── Layer 3: Reflection-Based Interop (zero-dependency, safe)
    │   ├── LootingBots has External.cs with ForceBotToScanLoot, PreventBotFromLooting, etc.
    │   ├── Pattern: Chainloader.PluginInfos.ContainsKey → Type.GetType → AccessTools.Method → Invoke
    │   ├── Effect: Tell LootingBots to reduce scan frequency, prevent looting on distant bots
    │   └── No compile-time dependency — gracefully degrades if mod is absent
    │
    └── Layer 4: Config File Override (crude but reliable)
        ├── Read/write other mods' BepInEx config entries at startup
        ├── Effect: Baseline tuning (e.g., set AILimit's BotLimit to match your LOD tiers)
        └── Done once at startup, not per-frame
```

### The Budget Tracker Pattern (Not a Command Hierarchy)

```
Your mod: "we're at 22ms frame time, budget is 16.7ms"
    │
    ├── Sets a global performance pressure value (e.g., Pressure = 0.8 on 0-1 scale)
    │
    ├── Each system OPTIONALLY reads this and reacts:
    │   ├── Your own LOD system: drop tier, throttle raycasts
    │   ├── LootingBots (via interop): reduce concurrency, prevent distant looting
    │   ├── BigBrain (via API): remove non-critical layers
    │   └── SAIN: no existing interop API → limited to Unity-level SetActive(false) or config changes
    │
    └── No mod is forced to obey. They're hints. If a mod doesn't listen, Unity SetActive(false) is the fallback.
```

### What NOT To Do

- **Do NOT Harmony-patch other mods' internal methods.** Creates fragility — if SAIN renames a private field, your patch silently breaks. The ecosystem deliberately avoids this pattern.
- **Do NOT try to route all mods through one global tick.** Different mods have different tick mechanisms (MonoBehaviour Update, async coroutines, Harmony postfixes on EFT methods). A dispatch layer adds overhead without saving CPU.

### Existing Interop Surfaces

| Mod | Public API Available | Your Mod Can Control |
|-----|---------------------|---------------------|
| **BigBrain** | `BrainManager` (hard dep) | Add/remove layers, query active layers |
| **SAIN** | None (no External.cs) | Only via Unity SetActive(false) or config |
| **LootingBots** | `External.cs` (reflection interop) | Force/prevent loot scans, check inventory |
| **AILimit** | BepInEx config entries | Read/write BotLimit, per-map distances |
| **botplacementsystem** | No interop API | Config or Harmony (risky) |
| **Waypoints** | No interop API | Not needed — NavMesh is transparent |

---

## 3. AI LOD Strategy

### The Problem (from RESEARCH.md)

SAIN already has an AI LOD system (`AILimitSetting` enum: None/Far/VeryFar/Narnia) with distance-based tiers. Each subsystem checks `Bot.CurrentAILimit` to decide how aggressively to throttle.

**However, it's broken by a bug:**

> RESEARCH.md line 489-527:
> `TickClassGroup` correctly calls `ShallTick()`, but `BotBase.TickInterval` defaults to `0f`, so `ShallTick()` always returns `true`.
> Only 2 classes (`LeanClass`, `EnemyListController`) set non-zero intervals.

The infrastructure exists. The tiering logic works. But every subsystem ticks every frame regardless of tier because the `TickInterval` is never set.

### The Fix (Single Harmony Patch)

Your mod patches SAIN's `ShallTick()` to inject proper tick intervals:

```csharp
[HarmonyPatch(typeof(BotBase), nameof(BotBase.ShallTick))]
class FixSAINTickIntervalPatch
{
    static bool Postfix(bool __result, BotBase __instance, float CurrentTime)
    {
        if (!__result) return false; // already skipping, fine

        // Apply dynamic LOD tier override based on your mod's assessment
        float interval = YourLODSystem.GetTickInterval(__instance.BotOwner);
        if (__instance.LastTickTime + interval < CurrentTime)
            return true;
        return false;
    }
}
```

**Risk level:** Low. You're fixing SAIN's own intended behavior that was never properly wired up. If SAIN updates fix this themselves, your patch silently becomes a no-op.

### Your Multi-Level LOD System

```
Tier 0 — "Full" (< 150m, budget healthy)
  ├── SAIN: Let everything run at default rates
  ├── LootingBots: Full concurrency (3 scans)
  ├── BigBrain: All layers active
  └── No throttling

Tier 1 — "Far" (150-250m OR budget > 16.7ms but < 20ms)
  ├── SAIN: Fix TickInterval to proper values (Far tier: vision 15Hz, cover 5Hz, decision 5Hz)
  ├── LootingBots: Reduce concurrency to 2, scan interval to 20s
  ├── BigBrain: All layers still active
  └── Your systems: Throttle raycasts

Tier 2 — "VeryFar" (250-400m OR budget > 20ms but < 25ms)
  ├── SAIN: Fix TickInterval (VeryFar tier: vision minimal, decision 3Hz)
  ├── LootingBots: Prevent looting on bots in this tier
  ├── BigBrain: Remove LootingLayer for these bots
  └── Your systems: Minimal

Tier 3 — "Narnia" (> 400m OR budget > 25ms)
  ├── Unity: GameObject.SetActive(false) on all bots in this tier
  ├── Effect: Zero CPU from these bots (like AILimit)
  └── BigBrain, SAIN, LootingBots: Don't tick at all

Tier 4 — "Emergency" (budget > 30ms)
  ├── Unity: SetActive(false) on all bots beyond 200m
  ├── BigBrain: Remove combat layers from mid-range bots, let looting-only run
  └── LootingBots: Zero concurrency, all bots prevented from looting
```

The LOD system uses **both** distance and frame-time pressure as inputs. A distant bot on a healthy frame gets Tier 1 treatment. A nearby bot during a CPU spike still gets Tier 0 for vision (required for combat) but Tier 2 for non-critical systems.

---

## 4. Tick Management Approach

### Guiding Principle

**Don't try to own the tick. Influence the throttle.**

Each mod keeps its own tick mechanism:

- SAIN ticks via postfix on `GameWorld.DoWorldTick()`
- LootingBots ticks via MonoBehaviour `Update()` + async coroutines
- AILimit ticks via MonoBehaviour `Update()` every 300 frames
- BigBrain ticks via Harmony patches on EFT's brain methods

Your mod does NOT try to route all of these through one central tick. Instead, it:

1. Attaches its own `MonoBehaviour` to `GameWorld` (like AILimit does)
2. Hooks `GameWorld.DoWorldTick()` for timing measurements (like SAIN does)
3. Observes and suggests — doesn't command and reroute

### What Your Tick Does

```
GameWorld.DoWorldTick()
    │
    ├── EFT's native tick runs
    ├── SAIN's WorldTickPatch fires (postfix) → SAIN subsystems tick
    ├── BigBrain layers arbitrate per bot
    ├── LootingBots brainstorm runs per bot
    │
    └── Your mod's budget tracker (postfix on same GameWorld tick):
        ├── Measure elapsed time
        ├── Update LOD tiers based on budget + distance
        ├── Apply fixes (SAIN TickInterval, layer removals, interop calls)
        └── Prepare next frame's budget target
```

### Performance Measurement

```csharp
void OnPostWorldTick()
{
    frameMs = stopwatch.Elapsed.TotalMilliseconds;
    stopwatch.Restart();

    if (frameMs > 16.7)
    {
        pressure = (frameMs - 16.7) / 16.7;  // 0.0 to ~1.0+
        ApplyPressure(pressure);
    }
    else if (frameMs < 14.0 && currentTier > 0)
    {
        ReducePressure();  // gradually restore
    }
}
```

---

## 5. Implementation Priorities

### Phase 1 — Foundation (High Impact, Low Risk)

1. Fix SAIN's `ShallTick()` / `TickInterval` — one Harmony patch, cascading benefit to all SAIN subsystems
2. Attach your `MonoBehaviour` to `GameWorld`, implement frame-time measurement
3. Implement your own internal LOD tier system (distance-based, with frame-time override)
4. Add Unity-level `SetActive(false)` for Narnia-tier bots (duplicates AILimit but ensures coverage)

### Phase 2 — Interop & Layer Control (Medium Impact, Medium Risk)

5. Add reflection-based LootingBots interop (follow the existing `LootingBotsInterop.cs` pattern)
6. Add BigBrain layer control — dynamically remove `LootingLayer` on far bots
7. Implement budget-pressure feedback loop (frame time → LOD tier adjustments)

### Phase 3 — Advanced (High Impact, Higher Risk)

8. Squad-level awareness (shared enemy detection → collapse O(N²) to O(N) for squads)
9. BigBrain layer migration to State Tree pattern (4x per-agent reduction per RESEARCH.md)
10. Global job consolidation (centralized JobManager with budget-aware scheduling)

---

## 6. Key Design Decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | **One user-facing install, not 5 separate mods** | User-friendly; single version to track |
| 2 | **Don't Harmony-patch other mods' internals** | Fragile — breaks on mod updates. Use public APIs + interop + Unity-level control instead |
| 3 | **Cooperative suggestion, not command** | No mod is forced to obey. Falls back to `SetActive(false)` which always works |
| 4 | **Fix SAIN's existing LOD, don't replace it** | SAIN's AI Limit infrastructure is good but has a one-line bug. Fix the bug, get full LOD for free |
| 5 | **Budget tracker is reactive, not predictive** | Measure frame time → react. Predictive scheduling adds complexity for minimal gain |
| 6 | **Distance + frame-time both drive LOD** | Distance alone can't handle CPU spikes from nearby combat. Frame-time alone doesn't know which bots are distant |
| 7 | **Start with SAIN + LootingBots control** | Most CPU impact per RESEARCH.md. SAIN's vision + decision, LootingBots' scans are the top bottlenecks |
| 8 | **AILimit-like SetActive for farthest tier** | Zero CPU cost when Unity-level disabled. Complements SAIN's internal throttling |

---

## 7. Risk Register

| Risk | Likelihood | Severity | Mitigation |
|------|-----------|----------|------------|
| SAIN update breaks TickInterval patch | Medium | Medium | Patch silently becomes no-op; mod degrades gracefully to fallback SetActive |
| SAIN update changes LootingBotsInterop | Low | Low | Reflection interop returns false; no crash |
| BigBrain API changes | Low | High (if it happens) | Add checks, log warnings, degrade gracefully |
| New mod added that this mod doesn't know about | Medium | Low | Unity SetActive(false) works on any mod. Interop additions can be layered on later |
| User has conflicting mods (AILimit + your SetActive) | High | Low | SetActive(false) is idempotent. Both can coexist. Tune distances to not fight each other |
| Performance measurement overhead | Low | Low | Stopwatch is nanoseconds. Keep it simple |

---

## Appendix: References

- `ARCHITECTURE.md` — Full internal architecture of all 7 existing mods
- `RESEARCH.md` — Performance research: TickInterval bug, 12 hotspots, 3-phase optimization plan
- `INTEGRATION.md` — Cross-mod integration: dependencies, layer priorities, interop APIs
- `PERFORMANCE_ARCHITECTURE.md` — Performance optimization architecture and identified hotspots

---

END OF DOCUMENT
