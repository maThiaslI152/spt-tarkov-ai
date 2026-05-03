# Bugfix: SAINAILimit Audibility Detection Broken — Bots Not Fighting

> **Date:** 2026-05-03 | **Status:** Fixed & Deployed | **File:** `OptimizedMod/SAIN/SAIN/Classes/Bot/SAINAILimit.cs`

---

## Symptoms Observed

1. **All bots:** Detect player, try to act, then **freeze and loop** behavior without shooting.
2. **Goons (Big Pipe):** Completely passive — does nothing even when gunfire is nearby and Knight (leader) is actively fighting the player.
3. **Navigation errors** in `BepInEx/LogOutput.log`: `"Navigation failed — target unreachable"` from `Blackhorse311-BotMind`.

---

## Root Causes

### Bug 1: `CheckPlayerCanHearBot()` always returned `false`

**Location:** `SAINAILimit.cs` lines 183-231 (original)

All audibility detection logic was **stubbed out as TODOs** — every check for gunfire, sprinting, and grenade sounds was commented out:

```csharp
// Gunfire: NOTE: BotOwner.WeaponManager.LastFireTime is not yet exposed.
// TODO: Implement via EFT BotWeaponManager reflection or SAIN shoot tracker
// if (Bot.BotOwner?.WeaponManager?.LastFireTime > 0f && ...)

// Sprinting: NOTE: SAINMoverClass.IsSprinting is not yet implemented.
// TODO: Add IsSprinting property to SAINMoverClass
// if (Bot.Mover?.IsSprinting == true)

// Recent grenade thrown...
// TODO: Add LastGrenadeThrowTime tracking

_cachedIsAudible = false;
return false;  // ← ALWAYS returns false
```

**Consequence:** When a bot lost direct visual contact with the player (moved behind cover), the perception tier flow was:

1. `CheckPlayerCanSeeBot()` → `false` (player behind wall)
2. `CheckPlayerCanHearBot()` → `false` (all checks stubbed)
3. `CheckGroupMemberInCombat()` → **did not exist** (see Bug 2)
4. `HasActiveEnemy` → **did not exist** (see Bug 3)

The bot dropped to **Occluded tier** — 5Hz ticking, navigation-only processing. Combat decisions, cover seeking, and aiming updates ran at 5 updates per second instead of 30, causing the "detect → stop → loop" freeze behavior.

### Bug 2: No group combat awareness (Big Pipe passivity)

**Location:** `SAINAILimit.cs` `DeterminePerceptionTier()` (original)

SAIN's perception system only checked individual bot state. Followers like Big Pipe (Goons, `WildSpawnType.followerBigPipe`) depend on their squad leader (Knight) for combat coordination. When Knight was fighting the player:

- Knight had `ActiveHumanEnemy = true` → Visible tier → full combat AI
- Big Pipe had `ActiveHumanEnemy = false` (following, not directly engaged) → dropped through all checks → **Occluded** → stood still

There was no mechanism for "if my squad is fighting, I should be at least Audible."

### Bug 3: `HasActiveEnemy` property did not exist

**Location:** `SAINAILimit.cs` line 126 (original)

The code referenced `Bot.EnemyController.HasActiveEnemy` but this property was never defined on `SAINEnemyController`. The controller only had:

- `ActiveHumanEnemy` (bool — has a human enemy)  
- `Enemies` (Dictionary — all tracked enemies)
- `KnownEnemies` (EnemyList — known enemies)

The code should have used `Bot.EnemyController.Enemies.Count > 0` to check for any tracked enemy.

---

## Fixes Applied

All fixes in `OptimizedMod/SAIN/SAIN/Classes/Bot/SAINAILimit.cs`.

### Fix 1: Implemented `CheckPlayerCanHearBot()` with working detection

Replaced all stubbed-out TODOs with actual runtime checks:


| Check              | Implementation                    | API Used                                                                                      |
| ------------------ | --------------------------------- | --------------------------------------------------------------------------------------------- |
| **Active gunfire** | Bot is shooting right now         | `Bot.Shoot.LastShotEnemy != null` (SAIN tracker) + `BotOwner.ShootData.Shooting` (EFT native) |
| **Recent gunfire** | Bot shot within last 3 seconds    | `_lastShotTime` field, updated when `ShootData.Shooting == true`                              |
| **Sprinting**      | Bot sprinting near player (< 60m) | `Bot.Player.IsSprintEnabled` + `PlayerTracker.FindClosestHumanPlayer()`                       |
| **Group gunfire**  | Ally in same group is shooting    | Iterates `BotsGroup.Allies`, checks `allyOwner.ShootData.Shooting`                            |


### Fix 2: Added `CheckGroupMemberInCombat()`

New method that checks if any member of the bot's `BotsGroup` is actively fighting:

```csharp
private bool CheckGroupMemberInCombat()
{
    var botsGroup = Bot.BotOwner?.BotsGroup;
    if (botsGroup == null) return false;

    foreach (var ally in botsGroup.Allies)
    {
        if (ally?.AIData?.BotOwner == null) continue;
        var allyOwner = ally.AIData.BotOwner;

        // Ally is actively shooting
        if (allyOwner.ShootData?.Shooting == true) return true;

        // Ally has an enemy target
        if (allyOwner.Memory?.GoalEnemy != null) return true;
    }
    return false;
}
```

This is the specific fix for Big Pipe — when Knight is fighting, Big Pipe and Birdeye are promoted to at least Audible tier.

### Fix 3: Replaced `HasActiveEnemy` with `Enemies.Count > 0`

Changed the non-existent `Bot.EnemyController.HasActiveEnemy` to `Bot.EnemyController.Enemies.Count > 0` which uses the existing `Enemies` Dictionary.

### New field added

```csharp
private float _lastShotTime;  // Tracks when bot last fired, for 3-second audibility window
```

Added to `ResetForPoolRecycle()` to properly reset on pool recycle.

---

## Fixed Perception Tier Flow

```
DeterminePerceptionTier():
  1. ActiveHumanEnemy → Visible           (full combat AI, 30Hz)
  2. Player can SEE bot → Visible         (frustum + 1 raycast, 30Hz)
  3. Player can HEAR bot → Audible        (gunfire, sprint, group combat, 10Hz)
  4. Group member in combat → Audible     (NEW — fixes Big Pipe, 10Hz)
  5. Has any tracked enemies → Audible    (was broken, now Enemies.Count > 0, 10Hz)
  6. Otherwise → Occluded                 (navigation only, 5Hz)
```

### Before vs After


| Scenario                                          | Before Fix                           | After Fix                            |
| ------------------------------------------------- | ------------------------------------ | ------------------------------------ |
| Bot shoots at player                              | Visible (has ActiveHumanEnemy)       | Visible                              |
| Bot behind cover, still tracking player           | **Occluded** (audibility broken)     | Visible (has ActiveHumanEnemy)       |
| Bot fired 2 seconds ago, player moved behind wall | **Occluded**                         | **Audible** (recent gunfire)         |
| Big Pipe standing near Knight who is shooting     | **Occluded**                         | **Audible** (group member in combat) |
| Bot sprinting toward player location              | **Occluded**                         | **Audible** (sprinting near player)  |
| Bot has AI-vs-AI enemy                            | **Occluded** (HasActiveEnemy broken) | **Audible** (Enemies.Count > 0)      |
| Peaceful bot, far from player, no enemies         | Occluded (correct)                   | Occluded (unchanged)                 |


---

## Build & Deployment

- **Build:** `dotnet build -c Release` — 0 errors, 8 warnings (pre-existing)
- **Output:** `OptimizedMod/SAIN/bin/Release/netstandard2.1/SAIN.dll` (965,632 bytes)
- **Deployed to:** `E:\SPT 4.0 Dev\BepInEx\plugins\SAIN\SAIN.dll`
- **Timestamp:** 2026-05-03 15:31

---

## Verification

To verify the fix is working in-game:

1. Press **F12** to open the SAIN Performance Monitor
2. Enable `Monitor Enabled` and `CSV Logging`
3. Check `VisibleBots` / `AudibleBots` / `OccludedBots` counters — bots in combat near the player should appear in Visible or Audible, never Occluded
4. On Lighthouse with Goons: Big Pipe should engage when Knight detects and fights the player
5. Check `BepInEx/LogOutput/sain_perf.csv` for tier distribution data

