using System;
using System.Diagnostics;
using EFT;
using SAIN.Components;
using SAIN.Helpers.Events;
using SAIN.Interop;
using SAIN.Layers.Combat.Squad;
using SAIN.Models.Enums;
using SAIN.Plugin;
using SAIN.Preset.GlobalSettings;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.SAINComponent.SubComponents.CoverFinder;
using UnityEngine;

namespace SAIN.SAINComponent.Classes.Decision;

public class BotDecisionManager(SAINDecisionClass decisionClass) : BotSubClass<SAINDecisionClass>(decisionClass), IBotClass
{
    private const float DECISION_FREQUENCY = 1f / 10;
    private const float SQUAD_PREEMPT_HOLD_SECONDS = 1.5f;
    private const double DefaultDecisionCostMs = 0.05d;

    public event Action<ECombatDecision, ESquadDecision, ESelfActionType, Enemy, BotComponent> OnDecisionMade;

    public ToggleEvent HasDecisionToggle { get; } = new ToggleEvent();

    public ECombatDecision CurrentCombatDecision { get; private set; }
    public ECombatDecision PreviousCombatDecision { get; private set; }
    public ESquadDecision CurrentSquadDecision { get; private set; }
    public ESquadDecision PreviousSquadDecision { get; private set; }
    public ESelfActionType CurrentSelfDecision { get; private set; }
    public ESelfActionType PreviousSelfDecision { get; private set; }

    public bool HasDecision
    {
        get { return HasDecisionToggle.Value; }
    }

    public float ChangeDecisionTime { get; private set; }
    public float TimeSinceChangeDecision
    {
        get { return Time.time - ChangeDecisionTime; }
    }

    /// <summary>Total decision-loop ticks attempted for this bot (includes skipped ticks).</summary>
    public long DecisionTicksTotal { get; private set; }

    /// <summary>Decision ticks skipped because an active squad command short-circuited local recomputation.</summary>
    public long DecisionSkipsSquadOrderTotal { get; private set; }

    /// <summary>Count of member-local preemptions over active squad command due to direct threat.</summary>
    public long DecisionPreemptionsTotal { get; private set; }

    /// <summary>Count of squad orders applied by coordinator via <see cref="SetSquadDecision"/>.</summary>
    public long SquadOrdersReceivedTotal { get; private set; }

    /// <summary>Total measured CPU spent in executed (non-skipped) decision loops.</summary>
    public double DecisionCpuExecutedTotalMs { get; private set; }

    /// <summary>Estimated CPU avoided by skipped decision loops (using EMA cost model per bot).</summary>
    public double DecisionCpuEstimatedSavedTotalMs { get; private set; }

    /// <summary>Positive means estimated savings exceed measured execution cost.</summary>
    public double DecisionCpuDeltaSavedMinusExecutedMs => DecisionCpuEstimatedSavedTotalMs - DecisionCpuExecutedTotalMs;

    /// <summary>Last time this bot received a squad command from coordinator.</summary>
    public float LastSquadOrderReceivedTime { get; private set; }

    /// <summary>Most recent squad order pushed by coordinator.</summary>
    public ESquadDecision LastSquadOrderReceived { get; private set; } = ESquadDecision.None;

    public override void Init()
    {
        Bot.BotActivation.BotActiveToggle.OnToggle += resetDecisions;
        base.Init();
    }

    public override void ManualUpdate()
    {
        if (_nextGetDecisionTime < Time.time)
        {
            _nextGetDecisionTime = Time.time + GetDecisionFrequency();
            getDecision();
        }
    }

    private float GetDecisionFrequency()
    {
        var settings = SAINPlugin.LoadedPreset?.GlobalSettings?.General?.Performance;
        if (settings == null || !settings.PerformanceMode)
            return DECISION_FREQUENCY;

        float baseInterval = DECISION_FREQUENCY;
        if (Bot.CurrentAILimit >= AILimitSetting.VeryFar)
            return baseInterval * 3f;
        if (Bot.CurrentAILimit >= AILimitSetting.Far)
            return baseInterval * 1.5f;

        return baseInterval;
    }

    public override void Dispose()
    {
        Bot.BotActivation.BotActiveToggle.OnToggle -= resetDecisions;
        base.Dispose();
    }

    private bool shallTagillaHammerAttack(Enemy enemy)
    {
        if (enemy == null)
        {
            return false;
        }
        bool alreadyAttacking = CurrentCombatDecision == ECombatDecision.MeleeAttack;
        ETagStatus status = Bot.Memory.Health.HealthStatus;

        if (!alreadyAttacking)
        {
            if (CurrentSelfDecision != ESelfActionType.None)
            {
                return false;
            }
            if (status != ETagStatus.Healthy && status != ETagStatus.Injured)
            {
                return false;
            }
            if (enemy.Path.PathToEnemyStatus != UnityEngine.AI.NavMeshPathStatus.PathComplete)
            {
                return false;
            }
            if (enemy.RealDistance < 35 && enemy.Path.PathLength < 30 && enemy.Status.VulnerableAction != EEnemyAction.None)
            {
                enemy.BotOwner.WeaponManager.Melee.ShallEndRun = false;
                return true;
            }
            if (enemy.RealDistance < 20 && enemy.Path.PathLength < 15)
            {
                enemy.BotOwner.WeaponManager.Melee.ShallEndRun = false;
                return true;
            }
            return false;
        }
        if (enemy.BotOwner.WeaponManager.Melee.ShallEndRun)
        {
            return false;
        }
        if (status != ETagStatus.Dying && enemy.RealDistance < 40 && enemy.Path.PathLength < 35)
        {
            return true;
        }
        return false;
    }

    private void getDecision()
    {
        DecisionTicksTotal++;
        long startTicks = Stopwatch.GetTimestamp();

        // If the squad coordinator has issued an active, non-expired order,
        // defer to the coordinator rather than recomputing decisions locally.
        // The 10 Hz pipeline would otherwise override the 2 Hz coordinator,
        // causing the squad layer to deactivate and the bot to oscillate
        // between SAIN combat and LootingBots / BotMind_Questing.
        if (SquadCombatCoordinator.HasActiveOrder(Bot) && !ShouldPreemptSquadOrder())
        {
            DecisionSkipsSquadOrderTotal++;
            DecisionCpuEstimatedSavedTotalMs += GetEstimatedDecisionCostMs();
            return;
        }

        try
        {
            Enemy enemy = Bot.EnemyController.ChooseEnemy();
            if (TryApplyExUsecCombatPressureOverride(enemy))
            {
                return;
            }

            if (enemy == null)
            {
                SetDecisions(ECombatDecision.None, ESquadDecision.None, ESelfActionType.None, enemy);
                return;
            }
            BaseClass.EnemyDecisions.DebugShallSearch = null;
            if (BaseClass.SelfActionDecisions.GetDecision(out ESelfActionType selfDecision, enemy))
            {
                SetDecisions(ECombatDecision.SeekCover, ESquadDecision.None, selfDecision, enemy);
                return;
            }

            // TODO: Tagilla stays locked on one person here, if another enemy is closer we need to switch over to him
            if (Bot.Info.Profile.WildSpawnType is WildSpawnType.bossTagilla or WildSpawnType.bossTagillaAgro)
            {
                if (shallTagillaHammerAttack(enemy))
                {
                    SetDecisions(ECombatDecision.MeleeAttack, ESquadDecision.None, ESelfActionType.None, enemy);
                    return;
                }
                if (BotOwner.WeaponManager.IsMelee)
                {
                    BotOwner.WeaponManager.Selector.ChangeToMain();
                }
            }

            if (enemy != null && enemy.IsZombie)
            {
                bool hasShooterContact = false;
                foreach (var knownEnemy in Bot.EnemyController.KnownEnemies)
                {
                    if (knownEnemy?.IsZombie != true)
                    {
                        hasShooterContact = true;
                    }
                }

                if (!hasShooterContact)
                {
                    BaseClass.SelfActionDecisions.GetDecision(out ESelfActionType zombieDecision, enemy);
                    BaseClass.SquadDecisions.GetDecision(out ESquadDecision zombieSqdDecision, enemy);
                    SetDecisions(ECombatDecision.FightZombies, zombieSqdDecision, zombieDecision, enemy);
                    return;
                }
            }
            if (Bot.Decision.DogFightDecision.DogFightActive)
            {
                SetDecisions(ECombatDecision.DogFight, ESquadDecision.None, ESelfActionType.None, enemy);
                return;
            }
            if (BotOwner.WeaponManager.IsMelee)
            {
                SetDecisions(ECombatDecision.MeleeAttack, ESquadDecision.None, ESelfActionType.None, enemy);
                return;
            }
            if (ContinueMoveToCover())
            {
                SetDecisions(ECombatDecision.SeekCover, ESquadDecision.None, Bot.Decision.CurrentSelfDecision, enemy);
                return;
            }
            if (BaseClass.SquadDecisions.GetDecision(out ESquadDecision squadDecision, enemy))
            {
                SetDecisions(ECombatDecision.None, squadDecision, ESelfActionType.None, enemy);
                return;
            }
            if (BaseClass.EnemyDecisions.GetDecision(out ECombatDecision combatDecision, enemy, Bot.EnemyController.KnownEnemies))
            {
                SetDecisions(combatDecision, ESquadDecision.None, ESelfActionType.None, enemy);
                return;
            }
            SetDecisions(ECombatDecision.None, ESquadDecision.None, ESelfActionType.None, enemy);
        }
        finally
        {
            RecordExecutedDecisionCost(startTicks);
        }
    }

    /// <summary>
    /// Safety valve for hierarchical squad control:
    /// when this specific member is under direct pressure, allow local combat
    /// recomputation immediately and request a near-immediate squad recoordination.
    /// This prevents "leader-order tunnel vision" when the player suddenly attacks
    /// a non-leader member between coordinator ticks.
    /// </summary>
    private bool ShouldPreemptSquadOrder()
    {
        if (_squadPreemptUntil > Time.time)
        {
            return true;
        }

        if (Bot?.EnemyController == null || BotOwner?.Memory == null)
        {
            return false;
        }

        bool directThreat = BotOwner.Memory.IsUnderFire
            || Bot.EnemyController.HumanEnemyInLineofSight
            || Bot.EnemyController.ActiveHumanEnemy;
        if (!directThreat)
        {
            return false;
        }

        // Pull coordinator next tick forward for this squad so leader-driven
        // orders can absorb the new contact quickly.
        SquadCombatCoordinator.RequestImmediateRecoordination(Bot);
        DecisionPreemptionsTotal++;
        _squadPreemptUntil = Time.time + SQUAD_PREEMPT_HOLD_SECONDS;
        return true;
    }

    private void RecordExecutedDecisionCost(long startTicks)
    {
        long endTicks = Stopwatch.GetTimestamp();
        double elapsedMs = Math.Max(0d, (endTicks - startTicks) * 1000d / Stopwatch.Frequency);
        DecisionCpuExecutedTotalMs += elapsedMs;
        _emaDecisionCostMs = _emaDecisionCostMs <= 0d ? elapsedMs : (_emaDecisionCostMs * 0.9d) + (elapsedMs * 0.1d);
    }

    private double GetEstimatedDecisionCostMs()
    {
        if (_emaDecisionCostMs > 0d)
        {
            return _emaDecisionCostMs;
        }
        return DefaultDecisionCostMs;
    }

    private void SetDecisions(ECombatDecision solo, ESquadDecision squad, ESelfActionType self, Enemy enemy)
    {
#if DEBUG
        if (SAINPlugin.DebugMode)
        {
            if (SAINPlugin.ForceSoloDecision != ECombatDecision.None)
            {
                solo = SAINPlugin.ForceSoloDecision;
            }
            if (SAINPlugin.ForceSquadDecision != ESquadDecision.None)
            {
                squad = SAINPlugin.ForceSquadDecision;
            }
            if (SAINPlugin.ForceSelfDecision != ESelfActionType.None)
            {
                self = SAINPlugin.ForceSelfDecision;
            }
        }
#endif

        if (checkForNewDecision(solo, squad, self, enemy))
        {
            bool hasDecision = solo != ECombatDecision.None || self != ESelfActionType.None || squad != ESquadDecision.None;

            if (hasDecision)
            {
                BotOwner.PatrollingData.Pause();
            }

            ChangeDecisionTime = Time.time;
            HasDecisionToggle.CheckToggle(hasDecision, ChangeDecisionTime);
            OnDecisionMade?.Invoke(solo, squad, self, enemy, Bot);
        }
    }

    private bool checkForNewDecision(
        ECombatDecision newSoloDecision,
        ESquadDecision newSquadDecision,
        ESelfActionType newSelfDecision,
        Enemy enemy
    )
    {
        bool newDecision = false;

        if (_lastDecisionEnemy != enemy)
        {
            _lastDecisionEnemy = enemy;
            newDecision = true;
        }

        if (newSoloDecision != CurrentCombatDecision)
        {
            PreviousCombatDecision = CurrentCombatDecision;
            CurrentCombatDecision = newSoloDecision;
            newDecision = true;
        }

        if (newSquadDecision != CurrentSquadDecision)
        {
            PreviousSquadDecision = CurrentSquadDecision;
            CurrentSquadDecision = newSquadDecision;
            newDecision = true;
        }

        if (newSelfDecision != CurrentSelfDecision)
        {
            PreviousSelfDecision = CurrentSelfDecision;
            CurrentSelfDecision = newSelfDecision;
            newDecision = true;
        }

        return newDecision;
    }

    private Enemy _lastDecisionEnemy;

    /// <summary>
    /// Hard combat-pressure gate for Lighthouse rogues (ExUsec):
    /// when SAIN reports combat pressure, bias decisions toward immediate combat
    /// so third-party/vanilla looting/patrol layers cannot hold control.
    /// </summary>
    private bool TryApplyExUsecCombatPressureOverride(Enemy chosenEnemy)
    {
        if (Bot?.Info?.Profile?.WildSpawnType != WildSpawnType.exUsec)
        {
            return false;
        }

        if (!SAINExternal.IsBotUnderCombatPressure(BotOwner))
        {
            return false;
        }

        Enemy enemy = chosenEnemy ?? Bot.GoalEnemy;
        if (enemy == null)
        {
            return false;
        }

        if (enemy.IsVisible && enemy.CanShoot)
        {
            SetDecisions(ECombatDecision.StandAndShoot, ESquadDecision.None, ESelfActionType.None, enemy);
            return true;
        }

        if (enemy.TimeSinceSeen < 6f || enemy.TimeSinceHeard < 6f)
        {
            SetDecisions(ECombatDecision.MoveToEngage, ESquadDecision.None, ESelfActionType.None, enemy);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Set a squad-level decision from an external coordinator (e.g., SquadCombatCoordinator).
    /// Preserves existing solo decisions so both the solo combat layer and the squad layer
    /// can be active simultaneously — the coordinator's orders are authoritative for squad
    /// positioning and target distribution but the bot still needs its own combat decision
    /// for the solo CombatLayer.
    /// </summary>
    public void SetSquadDecision(ESquadDecision squadDecision)
    {
        SquadOrdersReceivedTotal++;
        LastSquadOrderReceivedTime = Time.time;
        LastSquadOrderReceived = squadDecision;
        Enemy enemy = Bot.EnemyController.ChooseEnemy();
        SetDecisions(CurrentCombatDecision, squadDecision, CurrentSelfDecision, enemy);
    }

    public void ResetDecisions(bool active)
    {
        bool hasDecision = HasDecision;
        resetDecisions(false);
        if (active && hasDecision)
        {
            //BotOwner.CalcGoal();
        }
    }

    private void resetDecisions(bool value)
    {
        if (!value)
        {
            SetDecisions(ECombatDecision.None, ESquadDecision.None, ESelfActionType.None, null);
        }
    }

    private bool ContinueMoveToCover()
    {
        bool runningToCover = Bot.Decision.RunningToCover;
        if (!runningToCover)
        {
            return false;
        }

        if (!Bot.Mover.Moving)
        {
            return false;
        }

        if (Bot.Cover.HasCover)
        {
            return false;
        }

        float timeChangeDec = Bot.Decision.TimeSinceChangeDecision;
        if (timeChangeDec < 0.5f)
        {
            return true;
        }

        //if (timeChangeDec > 3 &&
        //    !Bot.BotStuck.BotHasChangedPosition)
        //{
        //    return false;
        //}

        CoverPoint coverMovingTo = Bot.Cover.CoverPoint_MovingTo;
        return coverMovingTo != null
            && coverMovingTo.PathDistanceStatus switch
            {
                CoverStatus.InCover => false,
                CoverStatus.CloseToCover => true,
                _ => !coverMovingTo.CoverData.IsBad,
            };
    }

    private float _nextGetDecisionTime;
    private float _squadPreemptUntil;
    private double _emaDecisionCostMs;
}
