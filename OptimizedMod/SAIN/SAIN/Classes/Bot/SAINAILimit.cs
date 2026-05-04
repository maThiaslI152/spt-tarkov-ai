using System;
using SAIN.Components;
using SAIN.Interop;
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

            // Diagnostic logging: tier transitions
            if (SainPerfLogInterop.IsDiagnosticLoggingEnabled)
            {
                UnityEngine.Debug.Log(
                    $"[SAIN DIAG] TierChange: Bot[{Bot.name}] "
                    + $"{_lastPerceptionTier} → {newTier} "
                    + $"(ActiveEnemy={Bot.EnemyController.ActiveHumanEnemy}, "
                    + $"Enemies={Bot.EnemyController.Enemies.Count}, "
                    + $"IsVisible={CheckPlayerCanSeeBot()}, "
                    + $"GroupCombat={CheckGroupMemberInCombat()})"
                );
            }

            // Update TickInterval to match perception tier
            TickInterval = GetTickIntervalForTier(newTier);
        }
    }

    /// <summary>
    /// Determine perception tier: Visible > Audible > Occluded.
    /// CRITICAL: Bots in combat (has any enemy) are never Occluded — they get at minimum Audible tier.
    /// Only peaceful/patrolling bots that the player can't see or hear go Occluded (navigation only).
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

        // If any squad member is in combat, this bot should be at least Audible.
        // Fixes Big Pipe and other followers going passive when their leader fights.
        if (CheckGroupMemberInCombat())
            return PerceptionTier.Audible;

        // Bots with ANY tracked enemy stay Audible (AI-vs-AI combat, hunting).
        // They need movement + basic reactions — can't be navigation-only.
        if (Bot.EnemyController.Enemies.Count > 0)
            return PerceptionTier.Audible;

        // Human nearby but not yet an EFT/SAIN "enemy" (e.g. third-party quest layer finished
        // and bot idles): avoid full Occlusion so SAIN + enemy pipeline keep ticking until
        // engagement registers — fixes "frozen until I walk up to them".
        if (IsHumanPlayerWithinProximityWake())
            return PerceptionTier.Audible;

        // Player is unaware of this bot and it's peaceful — navigation only
        return PerceptionTier.Occluded;
    }

    /// <summary>Linear distance (m) from this bot to closest human — uses bot's OtherPlayersData.</summary>
    private bool IsHumanPlayerWithinProximityWake()
    {
        const float wakeMeters = 40f;
        var gw = GameWorldComponent.Instance;
        if (gw == null || Bot?.PlayerComponent == null)
            return false;

        if (gw.PlayerTracker.FindClosestHumanPlayer(out float dist, Bot.PlayerComponent, out _) == null)
            return false;

        return dist < wakeMeters;
    }

    /// <summary>
    /// Check if any member of this bot's group/squad is actively fighting.
    /// If one Goon is in combat, ALL Goons should respond — not stand still.
    /// Uses EFT ShootData (guaranteed available on BotOwner) to detect group combat.
    /// </summary>
    private bool CheckGroupMemberInCombat()
    {
        var botsGroup = Bot.BotOwner?.BotsGroup;
        if (botsGroup == null)
            return false;

        foreach (var ally in botsGroup.Allies)
        {
            if (ally?.AIData?.BotOwner == null)
                continue;

            var allyOwner = ally.AIData.BotOwner;

            // Check if ally is actively shooting — most reliable combat indicator
            if (allyOwner.ShootData?.Shooting == true)
                return true;

            // Check if ally has any enemy target (via EFT enemy system)
            if (allyOwner.Memory?.GoalEnemy != null)
                return true;
        }

        return false;
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

        _cachedIsVisible = !Physics.Raycast(
            cameraPos,
            direction.normalized,
            distance,
            LayerMaskClass.HighPolyWithTerrainNoGrassMask,
            QueryTriggerInteraction.Ignore
        );
        return _cachedIsVisible;
    }

    /// <summary>
    /// Audibility detection: zero-cost checks for gunfire, sprinting, door breach.
    /// No raycasts needed — just check bot state.
    /// FIXED: Previously all checks were commented out, causing bots to always be Occluded.
    /// </summary>
    private bool CheckPlayerCanHearBot()
    {
        if (Time.time - _lastAudibleCheckTime < AudibleCacheDuration)
            return _cachedIsAudible;

        _lastAudibleCheckTime = Time.time;

        // Gunfire: bot is actively shooting (SAIN tracked or EFT native)
        if (Bot.Shoot?.LastShotEnemy != null || Bot.BotOwner?.ShootData?.Shooting == true)
        {
            _cachedIsAudible = true;
            return true;
        }

        // Gunfire: bot was shooting recently (within last 3 seconds)
        // Track last shot time via EFT ShootData
        if (Bot.BotOwner?.ShootData != null)
        {
            // If the bot has been shooting recently, the ShootData will still be in shooting state
            // or the weapon is not holstered after recent fire
            if (Bot.BotOwner.ShootData.Shooting)
            {
                _lastShotTime = Time.time;
                _cachedIsAudible = true;
                return true;
            }
        }

        // Check if bot recently shot (tracked internally, within 3 seconds)
        if (Time.time - _lastShotTime < 3f)
        {
            _cachedIsAudible = true;
            return true;
        }

        // Sprinting / loud movement: bot moving fast near player
        if (Bot.Player?.IsSprintEnabled == true)
        {
            var gameWorld = GameWorldComponent.Instance;
            if (gameWorld != null)
            {
                gameWorld.PlayerTracker.FindClosestHumanPlayer(
                    out float dist, Bot.PlayerComponent, out _);
                if (dist < AudibleFootstepRange * AudibleFootstepRange)
                {
                    _cachedIsAudible = true;
                    return true;
                }
            }
        }

        // Nearby combat: if any group member is shooting, this bot is audible too
        // This catches Big Pipe standing near Knight who is actively shooting
        var botsGroup = Bot.BotOwner?.BotsGroup;
        if (botsGroup != null)
        {
            foreach (var ally in botsGroup.Allies)
            {
                if (ally?.AIData?.BotOwner?.ShootData?.Shooting == true)
                {
                    _cachedIsAudible = true;
                    return true;
                }
            }
        }

        _cachedIsAudible = false;
        return false;
    }

    /// <summary>Internal tracking for when bot last fired. Set when Bot.Shoot.LastShotEnemy changes.</summary>
    private float _lastShotTime;

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
        _lastShotTime = 0f;
        CurrentAILimit = AILimitSetting.None;
        CurrentPerceptionTier = PerceptionTier.Occluded;
    }
}
