---
name: SAIN-optimization-fix-plan
overview: Merge root-level duplicate/optimized files into SAIN\SAIN\ source tree, move new optimization files into SAIN\SAIN\, add PerceptionTier-based performance scaling, fix the build configuration so root SAIN.csproj compiles the SAIN\SAIN\ source tree, and delete stale root duplicates.
todos:
  - id: a1-perception-tier-enum
    content: Add PerceptionTier enum to SAIN\SAIN\SAINEnum.cs
    status: pending
  - id: a2-tick-interval-setter
    content: Change TickInterval setter in SAIN\SAIN\Classes\Bot\BotBase.cs (protected set → set; default 1f/30f)
    status: pending
  - id: a3-sain-ai-limit-replace
    content: Replace SAIN\SAIN\Classes\Bot\SAINAILimit.cs with root version (PerceptionTier, visibility, audibility checks)
    status: pending
  - id: a4-bot-component-tier
    content: Add CurrentPerceptionTier property to SAIN\SAIN\Components\BotComponent.cs
    status: pending
  - id: a5-delete-duplicates
    content: Delete 4 root-level duplicate files (SAINEnum.cs, Classes\Bot\SAINAILimit.cs, Classes\Bot\BotBase.cs, Components\BotComponent.cs)
    status: pending
  - id: b1-squad-coordinator-fix
    content: "Move SquadCombatCoordinator.cs into SAIN\SAIN\Layers\Combat\Squad\ and fix EnemyList/SetSquadDecision calls"
    status: pending
  - id: b2-scheduler-singleton
    content: "Move AIFrameBudgetScheduler.cs into SAIN\SAIN\Components\ and add public static Instance singleton"
    status: pending
  - id: b3-perf-monitor-fix
    content: "Move SAINPerformanceMonitor.cs into SAIN\SAIN\Components\ and replace reflection with public properties on AIFrameBudgetScheduler"
    status: pending
  - id: b4-bot-pool-move
    content: "Move BotPoolPatches.cs into SAIN\SAIN\Patches\ (no code changes, document global Object.Destroy hook risk)"
    status: pending
  - id: c1-csproj-fix
    content: "Fix root SAIN.csproj: remove Compile Remove exclusion so SAIN\SAIN\ source is compiled"
    status: pending
isProject: false
---

# SAIN Optimization Fix Plan

## Architecture Context

The mod root at `e:\spt-tarkov-ai\OptimizedMod\SAIN\` has two `.csproj` files:

| Project | Location | Current compile scope |
|---------|----------|----------------------|
| Root `SAIN.csproj` | `SAIN\SAIN.csproj` | Compiles root-level `.cs` files; **excludes** `SAIN\**\*.cs` and `SAINServerMod\**\*.cs` |
| Original `SAIN.csproj` | `SAIN\SAIN\SAIN.csproj` | Compiles the actual source tree at `SAIN\SAIN\` |

The root project was created as a development harness — it compiles root-level copies of source files (with modifications) while excluding the originals. The end goal is to **retire this split** and have the root `SAIN.csproj` compile the `SAIN\SAIN\` source tree directly.

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

### C1. Root `SAIN.csproj`

Remove the two `Compile Remove` lines that exclude the SAIN\SAIN\ source:
```xml
<!-- REMOVE these lines: -->
<Compile Remove="SAIN\**\*.cs" />
<Compile Remove="SAINServerMod\**\*.cs" />
```

After A5 deletes the 4 root duplicates and B1-B4 move the new files into `SAIN\SAIN\`, any remaining root-level `.cs` files (e.g., `SAINPlugin.cs`, `Types/Jobs/SainJobTemplate.cs`) may still conflict with their `SAIN\SAIN\` counterparts. If conflicts arise, either:
- Delete the root duplicates (preferred), or
- Add specific `Compile Remove` entries for conflicting root files

---

## Execution Order

1. **A1** + **A2** + **A3** + **A4** — Apply changes to SAIN\SAIN\ originals (independent, can be parallel)
2. **A5** — Delete 4 root duplicates (after confirming A1-A4)
3. **B1** — Move + fix SquadCombatCoordinator
4. **B2** — Move + fix AIFrameBudgetScheduler (add singleton + public properties)
5. **B3** — Move + fix SAINPerformanceMonitor (replace reflection)
6. **B4** — Move BotPoolPatches (no code changes)
7. **C1** — Fix root SAIN.csproj (remove Compile Remove exclusions, verify build)
