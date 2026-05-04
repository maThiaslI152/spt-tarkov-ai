# Rogue Base Defense Plan

> Last updated: 2026-05-04
> Status: Partially implemented (coordinator + layer bootstrap shipped; full posture FSM and diagnostics checklist still open)
> Target: Rogue (`ExUsec`) behavior on Lighthouse/base-defense context

---

## Shipped: Combat squad layer bootstrap (fix)

**Problem:** Initial squad orders (`ApplyRogueDefenseOrders`) and LootingBots suppression (`SuppressRogueLooting`) run only inside `SquadCombatCoordinator.CoordinateSquad`, which was only invoked from `CombatSquadLayer` when `CurrentSquadDecision != None`. Vanilla `SquadDecisionClass` often leaves squad decision at `None` for idle Lighthouse exUsec squads, so the squad layer never became active, coordination never ran, and looting could win BigBrain arbitration — rogues looked passive and loot-focused.

**Fix (code):**

- `SquadCombatCoordinator.ShouldBootstrapRogueDefenseCombatLayer(BotComponent)` — returns true when rogue base-defense policy is enabled, the bot and vanilla `LeaderComponent` are in rogue-defense context (exUsec + Lighthouse when `OnlyOnLighthouse`), the squad has more than one member, and `CurrentSquadDecision` is still `None`.
- `CombatSquadLayer.IsActive()` — when self/solo combat are clear but squad decision is still `None`, if bootstrap applies, calls `CoordinateSquad(squadLeader, squadLeader.Decision)` so the first coordination tick can issue orders and run loot suppression; the layer activates only after `CurrentSquadDecision` is non-`None` for that bot.

**Files:** `OptimizedMod/SAIN/SAIN/Layers/Combat/Squad/SquadCombatCoordinator.cs`, `CombatSquadLayer.cs`.

**Not fixed by this alone:** If the player is never registered as a threat (perception / `GoalEnemy` / visible-enemy collection), engagement can still look weak; that is separate from the bootstrap deadlock.

---

## Posture vs BigBrain layers (Rogues vs Scavs)

- **This plan’s “Stationary / Patrol / Standby”** means **defense posture and squad orders** for **Rogues in base-defense context** (coordinator + settings), **not** a requirement to extend that policy to **Scavs**.
- **Scavs** stay out of scope for Rogue base-defense **behavior** (see regression note below). Their default SAIN behavior should remain as today.
- **EFT / BigBrain vanilla layer names** (e.g. `StationaryWS`, `PatrolAssault`) are a **separate concern**: SAIN removes many of them from the **shared** `_commonVanillaLayersToRemove` list for **all brains that use that list** (including `ExUsec` and Scav brains when vanilla is off), so vanilla “stationary / patrol” **layers** do not keep winning arbitration over SAIN. That is documented in `[BIGBRAIN_LAYER_MATRIX.md](BIGBRAIN_LAYER_MATRIX.md)`, not something this Rogue-only feature toggles per faction.

So: **Rogues** — yes, the plan cares about stationary/patrol **posture** in defense mode. **Scavs** — no for this plan’s **policy**; they still benefit from the **global** strip list like any other bot type using those layers.

---

## Objective

Implement Rogue-specific coordinated defense behavior that:

- uses stable squad leadership and communication,
- prioritizes `Stationary` / `Patrol` / `Standby` when not in contact,
- suppresses looting behavior on their own base-defense context,
- preserves normal SAIN combat transitions under threat.

---

## Behavioral Model

### Core state policy

- **Default (no immediate threat):**
  - `Standby -> Patrol -> Stationary` loop
- **Threat detected:**
  - transition to SAIN combat/squad actions
- **Post-combat cooldown:**
  - return to defense loop (not loot scan)

### Squad leadership

- Dynamic leader election among alive Rogue squad members.
- Score-based with deterministic tie-break.
- Hysteresis window to avoid frequent leader churn.
- Immediate replacement when leader is invalid (dead/incapacitated/no tactical context).

### Order lifecycle

- Orders include TTL.
- Orders are canceled when:
  - leader changes,
  - enemy is lost,
  - squad enters regroup/fallback state.
- Fallback command is hold/regroup when no actionable tactical order is available.

---

## Scope Guards

- Apply policy only when both are true:
  - bot faction/role is Rogue (`ExUsec`),
  - bot is in configured base-defense context (default Lighthouse Rogue base).
- Do not globally force no-loot behavior for every `ExUsec` spawn in all maps/mod contexts.

---

## LootingBots Interop Policy

- Use LootingBots suppression hooks only when LootingBots is loaded and interop is valid.
- If LootingBots is missing/fails interop:
  - do not throw,
  - keep Rogue base-defense and squad coordination active.

---

## Planned Config Controls

- `EnableRogueBaseDefensePolicy` (bool)
- `DisableRogueLootingOnBase` (bool)
- `RogueLeaderHoldSeconds` (float)

These controls enable tuning without rebuilding and help isolate regressions quickly.

---

## Diagnostics (for validation)

Under existing diagnostic logging:

- Leader selection/reselection events (with reason)
- Defense-state transitions (`Standby`, `Patrol`, `Stationary`, combat enter/exit)
- Loot suppression events (with scope reason)
- Squad order issue/cancel events (including TTL expiry)

Diagnostics should be concise and rate-limited to avoid log spam.

---

## Validation Checklist

### Functional

- Rogues keep base-defense posture when idle.
- Rogues transition to SAIN combat quickly when engaged.
- Squad behavior is coordinated (suppression/flank/hold/regroup).
- Rogues do not loot while in base-defense context.

### Stability

- No run-stop jitter from order churn.
- No stale tactical orders after target loss.
- Leader remains stable during active engagements.

### Regression

- PMCs, Scavs, Raiders behavior remains unchanged.
- Existing SAIN combat and BigBrain layer arbitration still function.

### Performance

- No major increase in skipped-bot spikes attributable to new Rogue policy.
- Diagnostic output remains usable and not excessively noisy.

---

## Code touchpoints

**Implemented (this plan’s core path):**

- `OptimizedMod/SAIN/SAIN/Layers/Combat/Squad/SquadCombatCoordinator.cs` — coordinator, rogue context, loot suppression, `ShouldBootstrapRogueDefenseCombatLayer`.
- `OptimizedMod/SAIN/SAIN/Layers/Combat/Squad/CombatSquadLayer.cs` — `IsActive` bootstrap path.
- `OptimizedMod/SAIN/SAIN/Preset/GlobalSettings/Categories/General/RogueBaseDefenseSettings.cs` — policy toggles and TTLs.

**Optional / future (not required for bootstrap):**

- `OptimizedMod/SAIN/SAIN/Classes/BotManager/Squad.cs` — further squad API or leader sync if needed.
- `OptimizedMod/SAIN/SAIN/Components/BotComponent.cs` — tick-level hooks only if coordination must run outside the squad layer.
- `OptimizedMod/SAIN/SAIN/Preset/GlobalSettings/Categories/General/LayerSettings.cs` — priority tuning relative to LootingBots / AvoidThreat.
- `OptimizedMod/LootingBots/LootingBots/External.cs` — interop surface (consumption is in SAIN coordinator).