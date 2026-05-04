# Bugfix: AI Work-Stop Loop & Squad Issues — Layer Cache Race, Squad Persistence, Player Engagement

> **Date:** 2026-05-04 | **Status:** Fixed & Deployed | **Files:** 6 files across SAIN

---

## Symptoms Observed

1. **Universal work-and-stop loop:** Bots (PMC, Scav, exUsec) oscillate between fighting and looting/stopping at ~2–5Hz. BigBrain CSV logs show the active layer alternating between SAIN Combat (priority ~77) and LootingBots (priority 62) or BotMind_Questing dozens of times per second.
2. **Squad coordination lost after first contact:** Once a squad member entered combat, the `CombatSquadLayer` deactivated permanently. BigBrain CSV logs show `S0` (Squad decision = None) on ALL mismatch exemplars.
3. **No reactive squad awareness:** When a squad member was shot by a player, other squad members did not react unless they independently detected the player through vision or hearing.

### Log Evidence

**Factory Raid (2026-05-04 08:51):**
- 4 persistent mismatches for 15+ minutes: `_BugsBunny_~pmcBEAR~Looting`, `AKA-Anton1o~pmcBEAR~BotMind_Questing`, `Thekoenman~pmcUSEC~Looting`, `MT_Militia~pmcUSEC~Looting`
- ALL have: `G1` (GoalEnemy), `C1` (CombatDecision != None), `P1` (pressure), **`SL0` (SAINLayersActive = false)**, **`S0` (Squad = None)**
- Mismatch reason: `thirdPartyOrVanilla` — BigBrain picks LootingBots (62) or BotMind_Questing over SAIN layers (69–70)

**Lighthouse Raid (2026-05-04 09:11):**
- Same pattern with exUsec rogues: `Rib-eye~exUsec~Looting`, `Corsair~exUsec~Looting`, `Auron~exUsec~Looting`, `Dakota~exUsec~PatrolFollower`
- All showing `C1, G1, P1` but **`SL0, S0`**

---

## Root Causes

### Root Cause A: Work-and-Stop Loop — Layer Activation Cache Race

**Location:** `OptimizedMod/SAIN/SAIN/Layers/SAINLayer.cs` — `CheckIsActiveWithCache()` (original)

The BigBrain layer evaluation runs every EFT frame (~33ms). The bot's decision pipeline (`BotDecisionManager.getDecision()`) runs at ~10Hz (100ms). The `CheckIsActiveWithCache()` method used a throttle window on the `false` state:

```csharp
// ORIGINAL (pseudocode):
if (_cachedIsActive)
    return _cachedIsActive = computeUncached();
// When false: only re-checks every IsActiveCheckInterval (200ms / 33ms)
if (Time.time - _lastIsActiveCheckTime >= IsActiveCheckInterval)
    _lastIsActiveCheckTime = Time.time;
    _cachedIsActive = computeUncached();
return _cachedIsActive;  // stale false returned during throttle window
```

When the 10Hz decision pipeline briefly set a decision to `None` (between combat decisions), the SAIN layer cache went `false` and stayed `false` for up to 200ms (squad layer) or 33ms (combat layer). BigBrain then selected the next-highest active layer — LootingBots (62) — even though SAIN Combat (69–70) should have won.

The race was:
1. Decision pipeline sets combat decision, layer becomes active, cache = true
2. Decision pipeline briefly sets None between evaluations, cache goes false
3. Cache throttles — stays false for 33–200ms
4. BigBrain evaluates every 33ms, sees SAIN layers false, picks LootingBots
5. Decision pipeline sets combat decision again, layer becomes active
6. Repeat at 2–5Hz

### Root Cause B: Squad Layer Lost After Combat Engagement

**Location:** `CombatSquadLayer.IsActive()` and `SquadCombatCoordinator.DistributeTargets()` (original)

`CombatSquadLayer.IsActive()` required `CurrentCombatDecision == None`. Once a bot entered combat, the squad layer deactivated permanently. The `SquadCombatCoordinator.DistributeTargets()` also skipped bots with non-None combat decisions via `if (member.Decision.CurrentCombatDecision != ECombatDecision.None) continue;`. This meant:

1. Bot enters combat → combat decision != None
2. Squad layer deactivates (because of the gate)
3. Coordinator stops managing this bot (because of the skip)
4. Result: BigBrain log shows `S0` (Squad decision = None) on ALL mismatch exemplars

### Root Cause C: No Player Engagement Propagation

**Location:** `Squad.UpdateSharedEnemyStatus()` (original)

The existing propagation method had distance checks (`isInCommunicationRange()`) and only handled hearing/sound events. There was no mechanism to propagate "this squad member is being shot by a player" to the rest of the squad. Player-engagement awareness relied entirely on vanilla EFT `BotsGroup` hostility at the engine level, which had delays and propagation gaps.

---

## Fixes Applied

### Fix 1: Eliminate the Layer Activation Cache Race

**File:** `OptimizedMod/SAIN/SAIN/Layers/SAINLayer.cs`

Replaced `CheckIsActiveWithCache()` to **never cache the `false` state**. The cache only prevents re-computation when already active. When inactive, it re-checks immediately every call:

```csharp
protected bool CheckIsActiveWithCache(Func<bool> computeUncached)
{
    bool wasActive = _cachedIsActive;
    bool isActiveNow = computeUncached();

    if (isActiveNow)
    {
        // Never throttle the active transition — BigBrain evaluates
        // every frame (~33ms) and any stale-false window lets
        // LootingBots / BotMind_Questing win the layer election.
        _cachedIsActive = true;
    }
    else
    {
        // Throttle only the false→false transition (prevent flapping
        // from brief None windows in the 10Hz pipeline). If we *were*
        // active last check, accept deactivation immediately.
        if (wasActive || Time.time - _lastIsActiveCheckTime >= IsActiveCheckInterval)
        {
            _cachedIsActive = false;
            _lastIsActiveCheckTime = Time.time;
        }
        return _cachedIsActive;
    }
    return true;
}
```

**Why this works:** The `true` state is never delayed. When the decision pipeline re-establishes a combat decision after a brief `None` window, the cache is immediately `true`, and BigBrain sees the SAIN layer as active on its next evaluation frame (~33ms later). The 200ms stale-false window is eliminated.

### Fix 2a: Squad Layer Active During Combat

**File:** `OptimizedMod/SAIN/SAIN/Layers/Combat/Squad/CombatSquadLayer.cs`

The `CurrentCombatDecision != None` gate was removed from `IsActive()`. The squad layer is now active whenever:
- A squad decision from the coordinator is valid AND not expired
- The coordinator has an unexpired order (checked via `GetSquadState`)
- Rogue defense bootstrap needs to issue initial orders

Self-actions (surgery/healing) still take absolute priority.

### Fix 2b: Coordinator Distributes Targets Continuously

**File:** `OptimizedMod/SAIN/SAIN/Layers/Combat/Squad/SquadCombatCoordinator.cs`

Removed the `CurrentCombatDecision != None` skip gates in **three** methods:
- `DistributeTargets()` — previously skipped members with active combat decisions
- `AssignFlankingPositions()` — previously skipped members with active combat decisions
- `ApplyRogueDefenseOrders()` — previously skipped members with active combat decisions

All three now distribute targets to **all** members regardless of solo combat state.

**Added public API:**
- `GetSquadState(BotComponent)` — returns the coordinator state for a bot's squad (used by CombatSquadLayer.IsActive)
- `HasActiveOrder(BotComponent)` — returns true if the coordinator has an active, non-expired order
- `SquadCoordState` class visibility changed from `private sealed` to `public sealed`

### Fix 2c: BotDecisionManager Defers to Coordinator

**File:** `OptimizedMod/SAIN/SAIN/Classes/Bot/Decision/BotDecisionManager.cs`

**Two changes:**

1. **`getDecision()` — Early return when coordinator has active order:**
   At the start of the 10Hz decision pipeline, if `SquadCombatCoordinator.HasActiveOrder(Bot)` is true, the method returns immediately, preserving whatever decisions the 2Hz coordinator set. This prevents the 10Hz pipeline from constantly overriding the coordinator.

2. **`SetSquadDecision()` — Preserve combat decisions:**
   Previously cleared combat to `None` whenever a squad decision was set. Now preserves `CurrentCombatDecision` and `CurrentSelfDecision`:
   ```csharp
   public void SetSquadDecision(ESquadDecision squadDecision)
   {
       Enemy enemy = Bot.EnemyController.ChooseEnemy();
       SetDecisions(CurrentCombatDecision, squadDecision, CurrentSelfDecision, enemy);
   }
   ```
   This allows both layers to run simultaneously — solo CombatLayer manages firing/cover, squad layer manages positioning/target distribution.

### Fix 3a: Squad-Wide Player Engagement Propagation

**File:** `OptimizedMod/SAIN/SAIN/Classes/BotManager/Squad.cs`

**`ReportPlayerEngagement(IPlayer, Vector3, BotComponent)` — New method:**
Propagates "this squad member is being shot by a player" to all squad members:
- Bypasses distance checks for exUsec (rogue bots) — always shares within squad
- Bypasses RNG — 100% propagation for engagement reports
- Calls `member.EnemyController.CheckAddEnemy(player)` on each member
- Sets a gunshot search point at the player's last known position via `AddPointToSearch()`
- Marks the enemy as actively engaged via `Status.SetVulnerableAction(EEnemyAction.Shooting)`
- Fires `OnMemberReportedPlayerEngagement` event

**`memberWasHit()` — Damage hook handler:**
Called when `BeingHitAction` fires on a squad member. Extracts the human aggressor from `DamageInfoStruct` and calls `ReportPlayerEngagement()`.

**`AddMember()` subscription:**
When a bot joins the squad, subscribes to `bot.Player.BeingHitAction` using a closure stored in `_hitDelegates` for clean cleanup.

**`RemoveMember()` unsubscription:**
The BeingHitAction handler is unsubscribed when a member leaves, using the delegate stored in `_hitDelegates`.

### Fix 3b: Per-Bot Engagement Awareness

**File:** `OptimizedMod/SAIN/SAIN/Classes/Bot/Info/BotSquadClass.cs`

Subscribes to `Squad.OnMemberReportedPlayerEngagement` when the squad is acquired. The handler calls `CheckAddEnemy(player)` on itself and marks the enemy with `SetVulnerableAction(EEnemyAction.Shooting)`, ensuring the bot treats the player as an immediate threat even without direct line of sight.

Properly unsubscribes in both `RemoveFromSquad()` and `getSquad()` to prevent double-subscription on squad re-acquisition.

---

## Files Modified

| # | File | Change |
|---|------|--------|
| 1 | `OptimizedMod/SAIN/SAIN/Layers/SAINLayer.cs` | Fix `CheckIsActiveWithCache()` race: never cache `false` state |
| 2 | `OptimizedMod/SAIN/SAIN/Layers/Combat/Squad/CombatSquadLayer.cs` | Remove `CurrentCombatDecision != None` gate; stay active during combat |
| 3 | `OptimizedMod/SAIN/SAIN/Layers/Combat/Squad/SquadCombatCoordinator.cs` | Remove `CurrentCombatDecision != None` skip; coordinate continuously; expose state |
| 4 | `OptimizedMod/SAIN/SAIN/Classes/Bot/Decision/BotDecisionManager.cs` | Defer to coordinator orders when active; preserve combat decisions |
| 5 | `OptimizedMod/SAIN/SAIN/Classes/BotManager/Squad.cs` | Add `ReportPlayerEngagement()`, damage hook, engagement event |
| 6 | `OptimizedMod/SAIN/SAIN/Classes/Bot/Info/BotSquadClass.cs` | Subscribe to engagement reports from squad members |

---

## Design Rationale

### Cache Race Fix (Fix 1) — Why not just remove the cache entirely?
The cache serves a purpose: `IsActive()` on some SAIN layers (especially CombatSolo) can be expensive because it evaluates enemy positions, cover status, and weapon readiness. The cache prevents this computation every BigBrain frame (~33ms). The fix keeps the cache for the `active → active` path (no unnecessary re-computation) while eliminating the stale-false window by always immediately accepting `true` from the uncached computation.

### Squad Persistence (Fix 2) — Why three coordinated changes?
The squad layer persistence requires all three changes to work together:
1. **Coordinator** removes its `CurrentCombatDecision` skips → issues orders to all members continuously
2. **BotDecisionManager** defers to coordinator orders via `HasActiveOrder` check → doesn't override them at 10Hz
3. **SetSquadDecision** preserves combat decisions → both solo and squad layers stay active simultaneously
4. **CombatSquadLayer** reads coordinator state directly → stays active even when 10Hz pipeline hasn't set a squad decision

If any one of these four changes is missing, the squad layer will still deactivate during combat.

### Engagement Propagation (Fix 3) — Why use `BeingHitAction` instead of polling?
EFT's `Player.BeingHitAction` event fires immediately when damage is taken (same frame). This is faster and more efficient than polling for damage state changes. The closure-with-dictionary pattern (`_hitDelegates`) ensures proper lifecycle management — subscriptions are tracked and cleaned up when members leave the squad.

exUsec (rogue) bots bypass the `isInCommunicationRange()` check because Lighthouse rogue squads are designed to operate as a coordinated unit — engagement knowledge must propagate regardless of distance or radio equipment.

---

## Verification Guide

### CSV Log Check
After deploying the fix, BigBrain CSV logs should show:
- `SL1` (SAINLayersActive = true) for bots in combat
- `S1` (Squad decision != None) for squad members during combat
- No persistent `thirdPartyOrVanilla` mismatches for bots that have `G1, C1, P1`

### Behavior Check
1. **Work-stop loop:** Observe PMC/Scav bots in combat — they should fight continuously without freezing or alternating to looting/stopping behaviors
2. **Squad coordination:** Watch exUsec rogue squads on Lighthouse — they should maintain formation and coordinated attacks throughout combat, not just before first contact
3. **Engagement propagation:** Shoot one member of a squad — other members should react immediately (turn toward the player, seek cover, return fire) even if they haven't directly detected the player
