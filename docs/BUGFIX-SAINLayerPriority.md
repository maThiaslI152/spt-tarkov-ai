# Bugfix: SAIN Combat Layers Blocked by BotMind — Bots Not Fighting (Priority Conflict)

> **Date:** 2026-05-03 | **Doc tidied:** 2026-05-06 (defaults + boss registration aligned with repo)  
> **Status:** Fixed & Deployed | **File:** `OptimizedMod/SAIN/SAIN/Preset/GlobalSettings/Categories/General/LayerSettings.cs` (+ `BigBrainHandler` validation)

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

### What Went Wrong (historical)

SAIN's combat layers for regular bots (PMCs, Scavs, Rogues, Raiders) were originally registered at **priority ~20–22** in preset defaults — below typical BotMind quest/medic layers (~25–50). Combat layers rarely won arbitration.

```csharp
// LayerSettings.cs (BEFORE first fix — illustrative)
SAINCombatSquadLayerPriority = 22;
SAINCombatSoloLayerPriority = 20;
SAINExtractLayerPriority = 24;   // also above solo combat — bad ordering
```

Meanwhile, BotMind's `MedicBuddyMedicLayer` / `MedicBuddyShooterLayer` and quest layers sit in a band that **beats** those low SAIN values when both are active.

**Result:** BotMind patrol/medic/quest could permanently outrank SAIN combat; bots looked “stuck” in non-combat brains.

### Bosses / followers / goons

Registration uses the **same validated preset priorities** as PMCs (not a separate hardcoded low tier). Example from `BigBrainHandler.AddCustomLayersToBosses()`:

```csharp
LayerPrioritySet priorities = GetValidatedLayerPriorities();
BrainManager.AddCustomLayer(typeof(CombatSquadLayer), brainList, priorities.Squad);
BrainManager.AddCustomLayer(typeof(CombatSoloLayer), brainList, priorities.Solo);
```

So if preset combat priorities are wrong, **everyone** is affected; the old story “bosses were fine because 70/69” applied only to an **older** codebase where bosses had literals and PMCs did not.

---

## Fix Applied (timeline)


| Milestone                 | `Squad` / `Solo` / `Extract` | Notes                                                                                                                                       |
| ------------------------- | ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- |
| Original bug              | ~22 / ~20 / ~24              | Combat below BotMind                                                                                                                        |
| First fix (2026-05-03)    | **70 / 69 / 65**             | Combat above BotMind; extract below combat                                                                                                  |
| **Current repo defaults** | **78 / 77 / 74**             | Room under `SAINAvoidThreatLayer` (80); `BigBrainHandler.GetValidatedLayerPriorities()` enforces `AvoidThreat(80) > Squad > Solo > Extract` |


Source of truth for numbers today: `LayerSettings.cs` defaults and `[MinMax]` caps (squad max 79, solo max 78, extract max 76).

### Layer stack (current fork defaults)

```
99: SAIN DebugLayer
80: SAIN AvoidThreatLayer
78: SAIN CombatSquadLayer       (preset default)
77: SAIN CombatSoloLayer
74: SAIN ExtractLayer
~62: LootingBots LootingLayer (BepInEx config; fork cap keeps it below extract)
```

Extraction must stay **below** solo combat so fights beat extract; squad remains **above** solo so coordinated squad arbitration can win when both are active.

---

## Files Changed


| File                                                                               | Role                                                                         |
| ---------------------------------------------------------------------------------- | ---------------------------------------------------------------------------- |
| `OptimizedMod/SAIN/SAIN/Preset/GlobalSettings/Categories/General/LayerSettings.cs` | Default priorities + min/max clamps                                          |
| `OptimizedMod/SAIN/SAIN/Plugin/BigBrainHandler.cs`                                 | `GetValidatedLayerPriorities()` — normalizes invalid presets at registration |


On disk, the active preset’s `GlobalSettings.json` can **override** C# defaults; keep JSON aligned with intended stack when tuning.

---

## Build & deployment

```bash
dotnet build OptimizedMod/SAIN/SAIN.csproj -c Release
```

Deploy `SAIN.dll` per [MOD_BUILD_AND_DEPLOY.md](MOD_BUILD_AND_DEPLOY.md).

---

## Verification

1. Restart SPT, start a raid
2. Optional: **F12 → SAINPerfLog → diagnostic logging** for `[SAIN DIAG]` lines
3. Engage bots — they should take cover and return fire instead of stuck patrol
4. BigBrain / perf CSV: combat layers should appear in histograms under pressure

---

## Related

- **[BUGFIX-SAINAILimit-Audibility.md](BUGFIX-SAINAILimit-Audibility.md)** — Perception tier audibility (orthogonal to BigBrain priority)
- **[BUGFIX-BigBrainPriority-QuestingBots.md](BUGFIX-BigBrainPriority-QuestingBots.md)** — QuestingBots vs combat arbitration
- **[PROGRESS.md](PROGRESS.md)** — Session log

