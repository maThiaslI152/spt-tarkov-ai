# Rogue Base Defense Plan

> Last updated: 2026-05-03
> Status: Planning
> Target: Rogue (`ExUsec`) behavior on Lighthouse/base-defense context

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

## Planned Code Touchpoints

- `OptimizedMod/SAIN/SAIN/Layers/Combat/Squad/SquadCombatCoordinator.cs`
- `OptimizedMod/SAIN/SAIN/Layers/Combat/Squad/CombatSquadLayer.cs`
- `OptimizedMod/SAIN/SAIN/Classes/BotManager/Squad.cs`
- `OptimizedMod/SAIN/SAIN/Components/BotComponent.cs`
- `OptimizedMod/SAIN/SAIN/Preset/GlobalSettings/Categories/General/LayerSettings.cs` (if needed for toggles)
- `OptimizedMod/LootingBots/LootingBots/External.cs` (interop hooks)

