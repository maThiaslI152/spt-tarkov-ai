# SAIN decision ranking vs squad / solo layer ranking

> **Purpose:** Single reference for (1) the **order** `BotDecisionManager` evaluates branches, (2) how **squad** vs **solo** combat decisions interact, and (3) how that maps to **BigBrain** `CustomLayer` numeric priorities.  
> **Code:** [`BotDecisionManager.cs`](../OptimizedMod/SAIN/SAIN/Classes/Bot/Decision/BotDecisionManager.cs), [`CombatSquadLayer.cs`](../OptimizedMod/SAIN/SAIN/Layers/Combat/Squad/CombatSquadLayer.cs), [`CombatSoloLayer.cs`](../OptimizedMod/SAIN/SAIN/Layers/Combat/Solo/CombatSoloLayer.cs), [`LayerSettings.cs`](../OptimizedMod/SAIN/SAIN/Preset/GlobalSettings/Categories/General/LayerSettings.cs).

---

## 1. BigBrain layer ranking (numeric, higher wins among `IsActive()` layers)

Configured in **`LayerSettings`** (preset) and registered in **`BigBrainHandler`**. Typical fork defaults:

| Priority (typ.) | Layer | Notes |
|-----------------|-------|--------|
| 99 | `DebugLayer` | Debug only |
| 80 | `SAINAvoidThreatLayer` | Grenade / artillery — must stay **above** combat so threats are dodged first |
| **78** | `CombatSquadLayer` | **Higher than solo** — evaluated first when both could apply (they are mutually exclusive on `CurrentCombatDecision`; see §3) |
| **77** | `CombatSoloLayer` | Solo combat actions |
| **74** | `ExtractLayer` | Below combat; above typical LootingBots / QB quest bands |
| ~62 | LootingBots (external) | BepInEx config |

**Rule:** BigBrain picks the **highest-priority** layer whose **`IsActive()`** is true. Changing **`SAINCombatSquadLayerPriority`** vs **`SAINCombatSoloLayerPriority`** swaps which SAIN combat layer wins if both were ever active; today **`CombatSquadLayer.IsActive`** requires **`CurrentCombatDecision == None`**, so solo and squad layers do not fight for the same bot state.

**Safety hardening (implemented):** registration now validates and normalizes preset priorities so runtime order always satisfies:

`SAINAvoidThreatLayer (80) > CombatSquadLayer > CombatSoloLayer > ExtractLayer`.

If preset values violate this, SAIN logs a warning once and applies corrected priorities for registration.

---

## 2. Decision pipeline ranking (`BotDecisionManager.getDecision`)

Evaluation is **strictly sequential** — first matching branch wins; later branches are skipped for that tick.

Higher in the list = **stronger precedence** (more urgent / higher-level behavior).

| Rank | Branch | Typical outcome (`solo`, `squad`, `self`) |
|------|--------|-------------------------------------------|
| 0 | `ChooseEnemy()` returns null | `(None, None, None)` — exit |
| 1 | `SelfActionDecisions.GetDecision` | Often `(SeekCover, None, self*)` |
| 2 | Tagilla boss melee rules | `(MeleeAttack, None, None)` |
| 3 | Zombie-only contact (`FightZombies` path) | `(FightZombies, squad?, self?)` from sub-decisions |
| 4 | `DogFightDecision.DogFightActive` | `(DogFight, None, None)` |
| 5 | `WeaponManager.IsMelee` (non-Tagilla) | `(MeleeAttack, None, None)` |
| 6 | `ContinueMoveToCover()` | `(SeekCover, None, current self)` |
| **7** | **`SquadDecisions.GetDecision`** | **`(None, ESquadDecision≠None, None)`** — team layer only |
| **8** | **`EnemyDecisions.GetDecision`** | **`(ECombatDecision≠None, None, None)`** — personal combat |
| 9 | Fallback | `(None, None, None)` |

**Squad vs solo in this pipeline:** **Squad is ranked above solo** (step 7 before 8). That is intentional: group suppress / help / group search / push suppressed can apply while the bot’s **personal** combat decision is still `None`, which is exactly what **`CombatSquadLayer`** requires (`CurrentCombatDecision == None` && `CurrentSquadDecision != None`).

**Hierarchical preemption (implemented):** when a coordinator order is active, the decision manager usually defers local recomputation. But if this member is under direct pressure (`IsUnderFire`, human LOS, active human enemy), local combat recomputation is allowed immediately, a short hold window is applied (~1.5s), and squad recoordination is requested immediately to sync leader-level orders with the new contact.

---

## 3. How squad layer and solo layer map to decisions

### `CombatSquadLayer` (`SAIN : Squad Layer`)

`IsActive()` is true only when:

- Bot active, `GetBotComponent()` ok  
- `CurrentSelfDecision == None`  
- **`CurrentCombatDecision == None`**  
- **`CurrentSquadDecision != None`**

So **squad BigBrain layer** drives behavior from **squad enum only** — no simultaneous solo enum from the same tick’s pipeline output for that mode.

### `CombatSoloLayer` (`SAIN : Combat Layer`)

`IsActive()` is true when:

- Bot active  
- **`CurrentCombatDecision != None`**

So **solo layer** owns all personal combat decisions (shoot, rush, search, retreat, …).

### Coordinator

[`SquadCombatCoordinator`](../OptimizedMod/SAIN/SAIN/Layers/Combat/Squad/SquadCombatCoordinator.cs) runs on the **leader** when the squad layer is active; it must not stomp an active **solo** combat decision (see [`BUGFIX-BigBrainPriority-QuestingBots.md`](BUGFIX-BigBrainPriority-QuestingBots.md) squad/solo guard notes). A direct-threat member can now request immediate recoordination so leader-issued plans refresh without waiting for the normal throttle interval.

---

## 4. Solo *engagement* ranking inside `EnemyDecisionClass.GetDecision`

Once the pipeline reaches **EnemyDecisions**, the first true branch sets `ECombatDecision`. Order (simplified):

| Order | Check | `ECombatDecision` |
|-------|--------|-------------------|
| 1 | No bullets / reloading | `Retreat` |
| 2 | `shallStandAndShoot` | `StandAndShoot` |
| 3 | `shallShootDistantEnemy` | `ShootDistantEnemy` |
| 4 | Aggressive allowed → `shallRushEnemy` | `RushEnemy` |
| 5 | Aggressive → `shallThrowGrenade` | `ThrowGrenade` |
| 6 | Aggressive → `shallSearch` | `Search` |
| 7 | `shallFreezeAndWait` | `Freeze` |
| … | (further branches in source) | … |

Tuning “which solo behavior wins” is done inside **`EnemyDecisionClass`** thresholds, not by BigBrain priority (solo layer already owns all of these once active).

---

## 5. Preset knobs that affect ranking

| Knob | File | Effect |
|------|------|--------|
| `SAINCombatSquadLayerPriority` / `SAINCombatSoloLayerPriority` / `SAINExtractLayerPriority` | `LayerSettings.cs` | BigBrain numeric order vs extract / third-party layers |
| Performance mode / `CurrentAILimit` | `BotDecisionManager.GetDecisionFrequency` | How often the whole pipeline runs |

There is **no separate preset** today for “swap squad before solo” inside `getDecision` — that order is fixed in code (§2).

---

## Related docs

- [ARCHITECTURE.md](ARCHITECTURE.md) — SAIN section overview  
- [INTEGRATION.md](INTEGRATION.md) — BigBrain + QuestingBots priority rules  
- [BIGBRAIN_LAYER_MATRIX.md](BIGBRAIN_LAYER_MATRIX.md) — layer registration matrix  
