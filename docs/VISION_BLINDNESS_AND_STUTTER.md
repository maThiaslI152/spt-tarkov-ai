# AI “blindness” and stutter — stack diagnosis and fixes

This document is the **canonical** explanation for two related symptoms in the OptimizedMod stack: bots that **do not visually acquire** the player (SAIN part-based LOS stays dead) and **work–stop / layer flapping** (loot, quest, patrol layers winning while combat pressure is still true).

**Project goal:** reliable combat fidelity under budget (see [PERFORMANCE_ARCHITECTURE.md](PERFORMANCE_ARCHITECTURE.md), [AI_BUDGET_LOD_PLAN.md](AI_BUDGET_LOD_PLAN.md)) without third-party layers starving SAIN vision or combat ticks.

---

## 1. Symptom definitions

| Symptom | Player-visible | Typical telemetry |
|--------|----------------|-------------------|
| **Blind** | Bot reacts to audio/LKP but hesitates or fails to commit LOS/shoot decisions aligned with line of sight | `GoalHumanSainParts*` / `GoalHumanFinal*` near zero while `SignalGoalEnemy` or pressure signals are high; **`VisionRayAttempt*Total` stuck at zero** (broken upstream pipeline) |
| **Stutter / work–stop** | Bot starts an action then idles, loots, or quests mid-fight | `MismatchCombatSignals` with `thirdPartyOrVanilla`; active layer names like `Looting`, `BotMind_Questing`, `PtrlBirdEye` under pressure |

---

## 2. Stack map (what must agree)

1. **BigBrain** — elects **one** active brain layer per bot each evaluation.
2. **SAIN layers** (`SAINLayer`, combat solo/squad) — drive combat when active; `SAINLayersActive` gates combat tick groups in `BotComponent`.
3. **`VisionRaycastJob`** — batched Unity `RaycastCommand` for SAIN body-part LOS / vision / shoot; feeds `EnemyPartDataClass` → `EnemyVisionClass` “can see” synthesis **independent** of the AI frame budget coroutine (separate `WaitForSeconds` loop on `BotManagerComponent`).
4. **`UpdateEFTVision`** — same class; drives `BotLook.UpdateLook` for EFT’s own visibility channel.
5. **`AIFrameBudgetScheduler`** — throttles `BotComponent.ManualUpdate` groups; **does not** replace the vision job; starvation here causes stepped AI, not necessarily zero ray attempts.
6. **Third-party layers** — LootingBots, BotMind / QuestingBots-style layers, vanilla patrol — can **preempt** SAIN if priority and `IsActive()` win.

---

## 3. Root causes (ordered by evidence)

### 3.1 Vision: native buffer / batch size mismatch (**fixed in code**)

**Problem:** `totalRaycasts` was computed as `enemyCount × partCount × raycastChecks`, but **`CreateCommands` wrote fewer** entries when:

- an enemy was **VeryFar** AI (skipped entirely),
- **Far** AI used at most two ray types,
- **distant** enemies used a single body part.

`RaycastCommand.ScheduleBatch(commands, hits, …)` processes the **full** `NativeArray` length. Trailing slots contained **default-initialized** commands (undefined / zero-length rays), producing **undefined job behavior** and misalignment risk between scheduled work and `AnalyzeHits` consumption — consistent with **`VisionRayAttempt*` never incrementing** despite combat context.

**Fix:** `CountScheduledRayCommands` + shared `TryGetEnemyRaycastSchedule` mirror scheduling; allocate `NativeArray` to the **exact** command count; DEBUG assert written count; early-out when count is zero. **Bounds:** `effectivePartCount` is clamped to `PartsArray.Length` so empty or short arrays do not index out of range.

**Implementation:** `OptimizedMod/SAIN/SAIN/Classes/BotManager/Jobs/VisionRaycastJob.cs`.

### 3.2 Vision: selection starvation (**mitigated earlier**)

`FindEnemies` must still include goal / current / known-human enemies when `ShallCheckLook` throttles false — combat fast path in the same file. If `VisionRayAttempt*` rises but `GoalHumanSainParts*` stays zero, use **schema 8+ `VisionRayEffective*`** vs attempts first; then masks / `SetLineOfSight` / `EnemyPartDataClass` if effective LOS is still low.

### 3.3 Stutter: BigBrain arbitration vs combat pressure

Non-SAIN layers can win while `SAINExternal.IsBotUnderCombatPressure` is true — logged as mismatch in SAINPerfLog. **LootingBots:** `LootingLayer.IsActive()` reflects combat pressure via reflection (see [INTEGRATION.md](INTEGRATION.md)). **BotMind / other mods:** may still need priority tuning or interop if they remain dominant in your CSV.

### 3.4 Stutter: `SAINLayer` active cache + `SAINLayersActive`

Brief inactive windows plus `CheckIsActiveWithCache` can delay reactivation; combat tick groups require `SAINLayersActive && GoalEnemy`. See [BUGFIX-AIWorkStop-LayerCacheAndSquad.md](BUGFIX-AIWorkStop-LayerCacheAndSquad.md).

### 3.5 Telemetry: cumulative vision counters

Diagnostics were **process-lifetime cumulative**, which made single-raid CSV interpretation ambiguous.

**Fix:** `VisionRaycastJob.ResetDiagnosticsForNewRaid()` from `BotManagerComponent.Activate` so BigBrain CSV `VisionRay*Total` columns reflect **this raid** only.

### 3.6 Telemetry: `VisionRayTarget*` vs `VisionRayEffective*` vs gameplay

**Gameplay** (`RaycastResult.Update` / `RaycastResult.CountsAsGameplaySuccess`): a ray **succeeds** when the hit is **`null`** (clear to cast point) **or** the first hit is the **intended body collider** (exact match or same `transform.root` as that body part).

**CSV (schema 8+):**

| Column group | Meaning |
|--------------|---------|
| **`VisionRayNull*` / `VisionRayBlocked*` / `VisionRayTarget*`** | **Strict first-hit classification:** null collider, hit something else (blocked), or hit the **exact** expected body collider (target). **Indoor LOS** often yields **high `Blocked*` and `VisionRayTarget* = 0`** because the first hit is cover/geometry, not the player mesh — that does **not** by itself mean bots are blind. |
| **`VisionRayEffectiveLosTotal`**, **`VisionRayEffectiveVisionTotal`**, **`VisionRayEffectiveShootTotal`** | Count of rays that match **gameplay success** (same predicate as `RaycastResult.CountsAsGameplaySuccess`). Compare to **`VisionRayAttempt*`** to see whether LOS/vision/shoot channels are granting success windows. |
| **`GoalHumanSainParts*`**, **`GoalHumanFinal*`** | **End-to-end** “does SAIN think it sees the player?” — use these to validate feel, not `Target*` alone. |

### 3.7 Distance: single-part vision beyond a threshold (preset)

Beyond **`General.Performance.VisionSinglePartBeyondDistanceMeters`** (default **150**, clamped **50–500** in code), `VisionRaycastJob` schedules **one** body part per enemy batch entry to save CPU. **`VisionUseFullPartsForHumanBeyondDistance`** (default **false**): when **true**, **human** targets keep **full** part coverage beyond that distance; **AI** targets still use single-part beyond the threshold so AI-vs-AI cost does not explode on open maps.

---

## 4. How to validate after deploy

1. Build inner SAIN tree: `dotnet build OptimizedMod/SAIN/SAIN.csproj -c Release` — deploy **`OptimizedMod/SAIN/SAIN/bin/Release/netstandard2.1/SAIN.dll`** (see [MOD_BUILD_AND_DEPLOY.md](MOD_BUILD_AND_DEPLOY.md)).
2. Run a short Factory raid with SAINPerfLog CSV enabled.
3. Confirm **`VisionRayAttemptLosTotal` (and related totals) increase** during contact.
4. If attempts are positive but `GoalHumanSainParts*` stays zero, focus on masks / `SetLineOfSight` / human collider matching.
5. On **schema 8+** CSVs, compare **`VisionRayEffective*Total`** to **`VisionRayAttempt*Total`**; use **`VisionRayTarget*`** only for “first hit was the body collider,” not overall perception health.

---

## 5. Related documents

| Doc | Use |
|-----|-----|
| [SAIN_PERFLOG.md](SAIN_PERFLOG.md) | Schema 8 columns, `VisionRay*Total` + `VisionRayEffective*`, BigBrain mismatch heuristics |
| [BUGFIX-VisionRaycastJob-ABRollback.md](BUGFIX-VisionRaycastJob-ABRollback.md) | Historical A/B rollback note for cadence experiments |
| [BUGFIX-VisibleBots-TelemetryAndVisibility.md](BUGFIX-VisibleBots-TelemetryAndVisibility.md) | Scheduler tier counting vs visibility telemetry |
| [INTEGRATION.md](INTEGRATION.md) | Layer priority, Looting ↔ SAIN interop |

---

## 6. Changelog (this investigation)

| Date | Change |
|------|--------|
| 2026-05-05 | Documented stack diagnosis; aligned `VisionRaycastJob` native buffer size with scheduled commands; per-raid diagnostic reset; null-safe LOS gizmo preset access. |
| 2026-05-06 | Schema 8 `VisionRayEffective*` counters (gameplay-aligned); preset `VisionSinglePartBeyondDistanceMeters` + `VisionUseFullPartsForHumanBeyondDistance`; doc `Target*` vs `Effective*` vs `GoalHuman*`. |
