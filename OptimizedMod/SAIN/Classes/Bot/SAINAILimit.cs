using System;
using SAIN.Components;
using SAIN.Preset.GlobalSettings;
using UnityEngine;

namespace SAIN.SAINComponent.Classes;

public class SAINAILimit : BotComponentClassBase
{
    public event Action<AILimitSetting> OnAILimitChanged;
    public event Action<PerceptionTier> OnPerceptionTierChanged;

    public AILimitSetting CurrentAILimit { get; private set; }
    public PerceptionTier CurrentPerceptionTier { get; private set; } = PerceptionTier.Occluded;
    public float ClosestPlayerDistanceSqr { get; private set; }

    // Cached perception data — updated at configured frequency
    private float _lastPerceptionCheckTime;
    private float _nextPerceptionCheckTime;
    private PerceptionTier _lastPerceptionTier;

    // Visibility tracking
    private float _lastVisibilityCheckTime;
    private bool _cachedIsVisible;
    private const float VisibilityCacheDuration = 0.5f; // Re-check visibility every 0.5s

    // Audibility tracking
    private float _lastAudibleCheckTime;
    private bool _cachedIsAudible;
    private const float AudibleCacheDuration = 1.0f; // Re-check audibility every 1s

    // Distance thresholds for tier assignment
    private const float AudibleGunfireRange = 500f;  // Gunfire can be heard up to ~500m
    private const float AudibleFootstepRange = 60f;   // Sprint footsteps audible through walls up to ~60m
    private const float AudibleDoorRange = 100f;      // Door breach audible at longer range

    public SAINAILimit(BotComponent sain)
        : base(sain)
    {
        TickRequirement = ESAINTickState.OnlyBotActive;
    }

    public override void ManualUpdate()
    {
        CheckAILimit();
        CheckPerceptionTier();
        base.ManualUpdate();
    }

    private void CheckAILimit()
    {
        AILimitSetting lastLimit = CurrentAILimit;
        if (Bot.EnemyController.ActiveHumanEnemy)
        {
            CurrentAILimit = AILimitSetting.None;
            ClosestPlayerDistanceSqr = -1f;
        }
        else if (_checkDistanceTime < Time.time)
        {
            _checkDistanceTime = Time.time + GlobalSettings.General.AILimit.AILimitUpdateFrequency * UnityEngine.Random.Range(0.9f, 1.1f);
            var gameWorld = GameWorldComponent.Instance;
            if (
                gameWorld != null
                && GameWorldComponent.Instance.PlayerTracker.FindClosestHumanPlayer(out float closestPlayerDistance, PlayerComponent, out _)
                    != null
            )
            {
                CurrentAILimit = CheckDistances(closestPlayerDistance);
                ClosestPlayerDistanceSqr = closestPlayerDistance;
            }
        }
        if (lastLimit != CurrentAILimit)
        {
            OnAILimitChanged?.Invoke(CurrentAILimit);
        }
    }

    /// <summary>
    /// Player-centric perception check — replaces the old distance-only CheckDistances.
    /// Determines whether the player can SEE this bot, HEAR this bot, or is UNAWARE of this bot.
    /// This replaces the bot-centric "how far am I?" with player-centric "does the player know I exist?"
    /// </summary>
    private void CheckPerceptionTier()
    {
        if (_nextPerceptionCheckTime > Time.time)
            return;

        // Perception check frequency — tied to config but never more than 30Hz
        float checkInterval = Mathf.Max(1f / 30f, GlobalSettings.General.AILimit.AILimitUpdateFrequency * 0.5f);
        _nextPerceptionCheckTime = Time.time + checkInterval * UnityEngine.Random.Range(0.8f, 1.2f);

        PerceptionTier newTier = DeterminePerceptionTier();

        if (newTier != CurrentPerceptionTier)
        {
            _lastPerceptionTier = CurrentPerceptionTier;
            CurrentPerceptionTier = newTier;
            OnPerceptionTierChanged?.Invoke(CurrentPerceptionTier);

            // Update TickInterval to match perception tier
            Bot.TickInterval = GetTickIntervalForTier(newTier);
        }
    }

    /// <summary>
    /// Determine perception tier: Visible > Audible > Occluded.
    /// Order of checks: if bot is actively fighting player, Visible. Then check frustum+raycast.
    /// Then check audibility. Default to Occluded.
    /// </summary>
    private PerceptionTier DeterminePerceptionTier()
    {
        // If bot has an active human enemy (fighting the player), it's Visible
        if (Bot.EnemyController.ActiveHumanEnemy)
            return PerceptionTier.Visible;

        // Check if player can SEE this bot (frustum + raycast, amortized)
        if (CheckPlayerCanSeeBot())
            return PerceptionTier.Visible;

        // Check if player can HEAR this bot (gunfire, footsteps, sprinting)
        if (CheckPlayerCanHearBot())
            return PerceptionTier.Audible;

        // Player is unaware of this bot
        return PerceptionTier.Occluded;
    }

    /// <summary>
    /// Visibility detection: camera frustum check + single raycast from camera to bot chest.
    /// Amortized: cached for VisibilityCacheDuration seconds.
    /// </summary>
    private bool CheckPlayerCanSeeBot()
    {
        if (Time.time - _lastVisibilityCheckTime < VisibilityCacheDuration)
            return _cachedIsVisible;

        _lastVisibilityCheckTime = Time.time;

        var mainCamera = Camera.main;
        if (mainCamera == null)
        {
            _cachedIsVisible = false;
            return false;
        }

        var planes = GeometryUtility.CalculateFrustumPlanes(mainCamera);
        var botPosition = Bot.Transform?.EyePosition ?? Bot.Position;
        var botBounds = new Bounds(botPosition, Vector3.one * 0.5f); // Small bounds around bot

        // Frustum check first (basically free)
        if (!GeometryUtility.TestPlanesAABB(planes, botBounds))
        {
            _cachedIsVisible = false;
            return false;
        }

        // Single raycast from camera to bot chest position
        var cameraPos = mainCamera.transform.position;
        var direction = botPosition - cameraPos;
        float distance = direction.magnitude;

        // Skip if too far (beyond max view distance)
        if (distance > 500f)
        {
            _cachedIsVisible = false;
            return false;
        }

        var raycastParams = new QueryParameters(LayerMaskClass.HighPolyWithTerrainNoGrassMask, false, QueryTriggerInteraction.Ignore);
        _cachedIsVisible = !Physics.Raycast(cameraPos, direction.normalized, distance, LayerMaskClass.HighPolyWithTerrainNoGrassMask);
        return _cachedIsVisible;
    }

    /// <summary>
    /// Audibility detection: zero-cost checks for gunfire, sprinting, door breach.
    /// No raycasts needed — just check bot state.
    /// </summary>
    private bool CheckPlayerCanHearBot()
    {
        if (Time.time - _lastAudibleCheckTime < AudibleCacheDuration)
            return _cachedIsAudible;

        _lastAudibleCheckTime = Time.time;

        // Gunfire: bot recently fired a weapon (last 3 seconds)
        if (Bot.BotOwner?.WeaponManager?.LastFireTime > 0f &&
            Time.time - Bot.BotOwner.WeaponManager.LastFireTime < 3f)
        {
            _cachedIsAudible = true;
            return true;
        }

        // Sprinting: bot is sprinting within hearing range
        if (Bot.Mover?.IsSprinting == true)
        {
            var gameWorld = GameWorldComponent.Instance;
            if (gameWorld != null)
            {
                var closestPlayer = gameWorld.PlayerTracker?.FindClosestHumanPlayer(
                    out float dist, Bot.PlayerComponent, out _);
                if (closestPlayer != null && dist < AudibleFootstepRange * AudibleFootstepRange)
                {
                    _cachedIsAudible = true;
                    return true;
                }
            }
        }

        // Recent grenade thrown (last 5 seconds)
        if (Bot.Grenade?.LastGrenadeThrowTime > 0f &&
            Time.time - Bot.Grenade.LastGrenadeThrowTime < 5f)
        {
            _cachedIsAudible = true;
            return true;
        }

        _cachedIsAudible = false;
        return false;
    }

    /// <summary>
    /// Get the tick interval for a given perception tier.
    /// Visible=30Hz, Audible=10Hz, Occluded=5Hz.
    /// These correspond to specific TickInterval values used by ShallTick().
    /// </summary>
    public static float GetTickIntervalForTier(PerceptionTier tier)
    {
        return tier switch
        {
            PerceptionTier.Visible => 1f / 30f,   // 30Hz — full responsiveness
            PerceptionTier.Audible => 1f / 10f,    // 10Hz — reduced, player just hears
            PerceptionTier.Occluded => 1f / 5f,    // 5Hz — minimal, navigation only
            _ => 1f / 30f,
        };
    }

    private AILimitSetting CheckDistances(float closestPlayerDist)
    {
        var aiLimit = GlobalSettingsClass.Instance.General.AILimit;
        if (closestPlayerDist < aiLimit.AILimitRanges[AILimitSetting.Far])
        {
            return AILimitSetting.None;
        }
        if (closestPlayerDist < aiLimit.AILimitRanges[AILimitSetting.VeryFar])
        {
            return AILimitSetting.Far;
        }
        if (closestPlayerDist < aiLimit.AILimitRanges[AILimitSetting.Narnia])
        {
            return AILimitSetting.VeryFar;
        }
        return AILimitSetting.Narnia;
    }

    private float _checkDistanceTime;

    /// <summary>
    /// Phase 4: Reset AI limit tracking for pool recycling.
    /// </summary>
    public void ResetForPoolRecycle()
    {
        _checkDistanceTime = 0f;
        _lastPerceptionCheckTime = 0f;
        _nextPerceptionCheckTime = 0f;
        _lastVisibilityCheckTime = 0f;
        _lastAudibleCheckTime = 0f;
        CurrentAILimit = AILimitSetting.None;
        CurrentPerceptionTier = PerceptionTier.Occluded;
    }
}
