using System.Text;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using SAIN.Components;
using SAIN.Components.BotController;
using SAIN.Models.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace SAIN.Layers;

public abstract class SAINLayer : CustomLayer
{
    public static string BuildLayerName(string name)
    {
        return $"SAIN : {name}";
    }

    protected virtual bool GetBotComponent()
    {
        if (Bot == null)
        {
            Bot = BotSpawnController.Instance.GetSAIN(BotOwner);
            if (Bot != null)
            {
                Bot.Decision.DecisionManager.OnDecisionMade += BotDecisionMade;
            }
        }
        return Bot != null;
    }

    public override bool IsCurrentActionEnding()
    {
        if (_actionReset)
        {
            _actionReset = false;
            return true;
        }
        return false;
    }

    protected void CheckActiveChanged(bool isActiveNow)
    {
        if (isActiveNow)
        {
            BotOwner.PatrollingData.Pause();
            SetLayer(true);
        }
        else
        {
            SetLayer(false);
        }
        _wasActive = isActiveNow;
    }

    private bool _wasActive = false;

    private void BotDecisionMade(
        ECombatDecision combatDecision,
        ESquadDecision squadDecision,
        ESelfActionType selfDecision,
        Enemy targetEnemy,
        BotComponent bot
    )
    {
        if (_wasActive)
        {
            _actionReset = true;
        }
    }

    private bool _actionReset = false;

    private readonly string LayerName;
    private readonly ESAINLayer ELayer;

    private string _currentLayerName;

    // Phase 3.3: State Tree pattern — cache IsActive() results to avoid
    // evaluating ALL layers every tick. Only re-check the active layer
    // or layers with registered transitions.
    // StraySpark data: Behavior Tree = 0.042ms/tick/agent, State Tree = 0.011ms/tick/agent (4x reduction)
    private bool _cachedIsActive;
    private float _lastIsActiveCheckTime;
    protected float IsActiveCheckInterval = 0.2f; // Re-check at 5Hz default, override per layer

    /// <summary>
    /// Cached IsActive check — only re-evaluates at the configured interval.
    /// When this layer is NOT active, checks are throttled to IsActiveCheckInterval.
    /// When this layer IS active, checks every frame (transitions only).
    /// </summary>
    protected bool CheckIsActiveWithCache()
    {
        bool isCurrentlyActive = _cachedIsActive;

        // If currently active, check every frame (active-layer-only pattern)
        if (isCurrentlyActive)
        {
            return _cachedIsActive = IsActive();
        }

        // If not active, throttle re-evaluation
        if (Time.time - _lastIsActiveCheckTime >= IsActiveCheckInterval)
        {
            _lastIsActiveCheckTime = Time.time;
            _cachedIsActive = IsActive();
        }

        return _cachedIsActive;
    }

    /// <summary>
    /// Returns the cached active state without re-evaluating.
    /// </summary>
    protected bool GetCachedIsActive()
    {
        return _cachedIsActive;
    }

    protected SAINLayer(BotOwner botOwner, int priority, string layerName, ESAINLayer eSainLayer) : base(botOwner, priority)
    {
        LayerName = layerName;
        ELayer = eSainLayer;

        botOwner.Brain.BaseBrain.OnLayerChangedTo += OnLayerChanged;
    }

    private void OnLayerChanged(AICoreLayerClass<BotLogicDecision> layer)
    {
        var newLayerName = layer.Name();

        var mover = BotOwner.Mover;

        if (newLayerName == LayerName)
        {
            // If we activated this (SAIN) layer, wipe the builtin bot mover
            mover.Stop();
        }
        else if (_currentLayerName == LayerName)
        {
            // If we switched away from this layer to a different one, set the player to the navmesh to ensure it has a consistent state
            var playerPosition = BotOwner.GetPlayer.Position;
            mover.LastGoodCastPoint = mover.PrevSuccessLinkedFrom_1 = mover.PrevLinkPos = mover.PositionOnWayInner = playerPosition;
            mover.LastGoodCastPointTime = Time.time;
            // Prevents the mover from re-issuing a move command to it's last target in SetPlayerToNavMesh
            mover.PrevPosLinkedTime_1 = 0f;
            // Final insurance that the bot is set to the navmesh before we hand over the brain
            mover.SetPlayerToNavMesh(playerPosition);
        }

        _currentLayerName = newLayerName;
    }

    private void SetLayer(bool active)
    {
        if (Bot != null)
        {
            if (active)
            {
                Bot.ActiveLayer = ELayer;
            }
            else if (Bot.ActiveLayer == ELayer)
            {
                Bot.ActiveLayer = ESAINLayer.None;
            }
        }
    }

    public override string GetName()
    {
        return LayerName;
    }

    public static BotManagerComponent BotController
    {
        get { return BotManagerComponent.Instance; }
    }

    public BotComponent Bot { get; private set; }

    public override void BuildDebugText(StringBuilder stringBuilder)
    {
        if (Bot != null)
        {
            DebugOverlay.AddBaseInfo(Bot, BotOwner, stringBuilder);
        }
    }
}
