# Bugfix: SAIN Combat Layers Blocked by BotMind — Bots Not Fighting (Priority Conflict)

> **Date:** 2026-05-03 | **Status:** Fixed & Deployed | **File:** `OptimizedMod/SAIN/SAIN/Preset/GlobalSettings/Categories/General/LayerSettings.cs`

---

## Symptoms Observed

1. **All bots stuck in patrol/follow/questing** — ignore the player completely, even when shot at point blank
2. **No combat activation** — bots never transition to combat behavior (cover, return fire, etc.)
3. **BotMind MedicBuddy/Questing layers active on every bot** — log shows `MedicBuddyShooterLayer.IsActive` and `MedicBuddyMedicLayer.IsActive` on Bot1 through Bot17+
4. **"Stuck check failed"** warnings in logs — `[Bot18] Stuck check failed at 271m from target`

---

## Root Cause

### BigBrain Layer Priority System

BigBrain selects which `CustomLayer` controls a bot at any moment. The **highest-priority active layer wins**. Priority is an integer: higher number = higher priority.

### What Went Wrong

SAIN's combat layers for regular bots (PMCs, Scavs, Rogues, Raiders) were registered at **priority 20-22**:

```csharp
// LayerSettings.cs (BEFORE FIX)
SAINCombatSquadLayerPriority = 22;   // Squad combat — team tactics, target distribution
SAINCombatSoloLayerPriority = 20;    // Solo combat — cover, aim, fire
SAINExtractLayerPriority = 24;       // Extraction behavior
```

Meanwhile, BotMind's `MedicBuddyMedicLayer` and `MedicBuddyShooterLayer` layers are registered (likely at priorities 25-50). Since BigBrain picks the highest-priority active layer:

```
Priority 99: SAIN DebugLayer
Priority 80: SAIN AvoidThreatLayer (grenade dodge)
Priority 41-50: BotMind MedicBuddy layers (estimated)
Priority 25-40: BotMind QuestingLayer (estimated)
Priority 20-24: SAIN Combat layers ← TOO LOW, never activated
Priority 4-13: LootingBots
```

**Result:** BotMind's patrol/medic/quest layers always had higher priority than SAIN's combat layers. Bots were permanently stuck in patrol mode because the combat layers could never activate.

### Why Bosses Were Unaffected

Bosses and followers use hardcoded priorities (not the config):

```csharp
// BigBrainHandler.cs - AddCustomLayersToBosses()
BrainManager.AddCustomLayer(typeof(CombatSquadLayer), brainList, 70);
BrainManager.AddCustomLayer(typeof(CombatSoloLayer), brainList, 69);
```

This is why the earlier Big Pipe fix (group combat awareness in `SAINAILimit`) was about perception tiering, not layer priority. Bosses already had correct combat priority.

---

## Fix Applied

Raised regular bot combat layer priorities to match the boss/follower defaults:

| Setting | Before | After | File |
|---|---|---|---|
| `SAINCombatSquadLayerPriority` | 22 | **70** | `LayerSettings.cs` + `My Tuned Preset/GlobalSettings.json` |
| `SAINCombatSoloLayerPriority` | 20 | **69** | Same |
| `SAINExtractLayerPriority` | 24 | **65** | Same |

### New Layer Priority Stack

```
99: SAIN DebugLayer
80: SAIN AvoidThreatLayer (grenade dodge)
70: SAIN CombatSquadLayer       ← FIXED (was 22)
69: SAIN CombatSoloLayer        ← FIXED (was 20)
65: SAIN ExtractLayer           ← now below combat
??: BotMind layers (overridden by combat when fighting)
13: LootingBots
```

### Why Extract Changed Too

Extraction was at 24 (above combat at 20-22). If a bot wanted to extract, it could override combat. Now extraction is at 65 (below combat at 69-70), so bots only extract when NOT in combat.

---

## Files Changed

| File | Change |
|---|---|
| `OptimizedMod/SAIN/SAIN/Preset/GlobalSettings/Categories/General/LayerSettings.cs` | Defaults: 22→70, 20→69, 24→65 |
| `E:\SPT 4.0 Dev\BepInEx\plugins\SAIN\Presets\My Tuned Preset\GlobalSettings.json` | Same values in deployed config |

### Why Both Files

The C# code provides the **default** used when no config JSON exists. The JSON **overrides** the default when present. Since "My Tuned Preset" is the active preset, its JSON takes precedence. Both must match for consistency.

---

## Build & Deployment

- **Build:** `dotnet build -c Release` — 0 errors, 8 warnings
- **Output:** `OptimizedMod/SAIN/bin/Release/netstandard2.1/SAIN.dll`
- **Deployed to:** `E:\SPT 4.0 Dev\BepInEx\plugins\SAIN\SAIN.dll` + config JSON
- **Timestamp:** 2026-05-03 16:30

---

## Verification

1. Restart SPT, start Lighthouse raid
2. Enable **F12 → SAINPerfLog → `SAINPerfLog (F12)` → `2. Diagnostic Logging`**
3. Approach bots and shoot at them
4. Bots should **immediately return fire, seek cover, and flank** — no patrol/freeze behavior
5. Check `BepInEx/LogOutput.log` for `[SAIN DIAG] TierChange:` entries — bots near you should transition from Occluded/Audible → Visible when combat starts
6. No "Stuck check failed" warnings for bots near the player

---

## Related

- **[BUGFIX-SAINAILimit-Audibility.md](BUGFIX-SAINAILimit-Audibility.md)** — Earlier fix for the perception tier audibility checks (also deployed in this session)
- **[PROGRESS.md](PROGRESS.md)** — Overall project progress tracker
