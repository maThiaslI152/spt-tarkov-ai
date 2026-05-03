using System;
using System.Collections.Generic;
using System.Diagnostics;
using EFT;
using SAIN.Models.Enums;
using SAIN.Plugin;
using UnityEngine;

namespace SAIN.Components;

/// <summary>
/// AI Frame Budget Scheduler — enforces a hard cap (default 2ms) on AI processing per frame.
/// Bots are tiered by player perception (Visible > Audible > Occluded). Time is split across
/// tiers so Audible combatants still get updates when many Visible bots exist (early-return
/// starvation caused search/shoot loops and stale vision).
/// This architecture is proven by STALKER Anomaly's Warfare mode, which handles 50+ squads
/// and hundreds of NPCs at stable 60 FPS using the same budget-time approach.
/// </summary>
public class AIFrameBudgetScheduler
{
    public static AIFrameBudgetScheduler Instance { get; private set; }

    /// <summary>Hard cap on AI processing time per frame, in milliseconds. STALKER-proven value.</summary>
    public float MaxAIBudgetMs = 2.0f;

    /// <summary>Backpressure: true when the scheduler ran out of budget last frame.</summary>
    public bool BudgetExhaustedLastFrame { get; private set; }

    /// <summary>Number of bots processed this frame (for diagnostics).</summary>
    public int BotsProcessedThisFrame { get; private set; }

    /// <summary>Number of bots skipped this frame due to budget exhaustion.</summary>
    public int BotsSkippedThisFrame => TotalOnlineBots - BotsProcessedThisFrame;

    /// <summary>Actual elapsed AI processing time in milliseconds for the last frame.</summary>
    public double BudgetElapsedMs { get; private set; }

    /// <summary>Total number of online bots last frame.</summary>
    public int TotalOnlineBots { get; private set; }

    /// <summary>Number of Visible-tier bots last frame.</summary>
    public int VisibleBotsLastFrame { get; private set; }

    /// <summary>Number of Audible-tier bots last frame.</summary>
    public int AudibleBotsLastFrame { get; private set; }

    /// <summary>Number of Occluded-tier bots last frame.</summary>
    public int OccludedBotsLastFrame { get; private set; }

    /// <summary>Number of offline squads currently tracked.</summary>
    public int OfflineSquadCount => _offlineSquads.Count;

    /// <summary>Registered offline combat squads for statistical resolution.</summary>
    public IReadOnlyList<OfflineSquad> OfflineSquads => _offlineSquads;

    private readonly Stopwatch _frameTimer = new();
    private readonly List<OfflineSquad> _offlineSquads = new();

    /// <summary>Round-robin cursor for each tier (fairness when budget ends mid-list).</summary>
    private int _resumeVisibleIndex;

    private int _resumeAudibleIndex;
    private int _resumeOccludedIndex;

    /// <summary>Max elapsed ms allocated to Visible-tier bots before Audible tier runs (ratio of <see cref="MaxAIBudgetMs"/>).</summary>
    private const double VisibleTierBudgetFraction = 0.45;

    /// <summary>Cumulative cap before Occluded tier runs (Visible + Audible share).</summary>
    private const double AudibleTierCumulativeFraction = 0.88;

    public AIFrameBudgetScheduler()
    {
        Instance = this;
    }

    /// <summary>
    /// Register an offline squad for statistical combat resolution.
    /// Offline squads are resolved via statistics only — zero CPU per frame.
    /// </summary>
    public void RegisterOfflineSquad(OfflineSquad squad)
    {
        _offlineSquads.Add(squad);
    }

    /// <summary>
    /// Remove an offline squad (e.g., when player enters zone and it materializes).
    /// </summary>
    public void UnregisterOfflineSquad(string squadId)
    {
        _offlineSquads.RemoveAll(s => s.SquadId == squadId);
    }

    /// <summary>
    /// Process all online bots within the AI budget. Bots are sorted by perception tier
    /// (Visible first, then Audible, then Occluded) and processed until the budget is exhausted.
    /// Offline combat between registered offline squads is resolved once per second.
    /// </summary>
    public void ProcessFrame(HashSet<BotComponent> allBots, float currentTime, float deltaTime)
    {
        _frameTimer.Restart();

        BotsProcessedThisFrame = 0;

        // Phase 0: Resolve offline squad combat (once per second, sub-0.1ms)
        if (currentTime - _lastOfflineCombatTime > 1f && _offlineSquads.Count >= 2)
        {
            ResolveOfflineSquadCombat(currentTime);
            _lastOfflineCombatTime = currentTime;
        }

        if (allBots == null || allBots.Count == 0)
        {
            BudgetExhaustedLastFrame = false;
            TotalOnlineBots = 0;
            VisibleBotsLastFrame = 0;
            AudibleBotsLastFrame = 0;
            OccludedBotsLastFrame = 0;
            BudgetElapsedMs = _frameTimer.Elapsed.TotalMilliseconds;
            return;
        }

        // Collect and tier all bots
        var visibleBots = new List<BotComponent>();
        var audibleBots = new List<BotComponent>();
        var occludedBots = new List<BotComponent>();

        // Bots under contact must tick every frame even if the 2ms budget would skip Occluded-tier
        // processing — otherwise vanilla EFT layers (obfuscated names like GClass228) drive behavior.
        var forceTickBots = new HashSet<BotComponent>();
        foreach (var bot in allBots)
        {
            if (bot == null || bot.IsDead || !bot.BotActive)
                continue;
            if (ShouldForceAiTick(bot))
            {
                forceTickBots.Add(bot);
            }
        }

        foreach (var bot in forceTickBots)
        {
            ProcessBot(bot, currentTime, deltaTime);
            BotsProcessedThisFrame++;
        }

        foreach (var bot in allBots)
        {
            if (bot == null || bot.IsDead || !bot.BotActive)
                continue;
            if (forceTickBots.Contains(bot))
                continue;

            var tier = bot.CurrentPerceptionTier;
            switch (tier)
            {
                case PerceptionTier.Visible:
                    visibleBots.Add(bot);
                    break;
                case PerceptionTier.Audible:
                    audibleBots.Add(bot);
                    break;
                default:
                    occludedBots.Add(bot);
                    break;
            }
        }

        VisibleBotsLastFrame = visibleBots.Count;
        AudibleBotsLastFrame = audibleBots.Count;
        OccludedBotsLastFrame = occludedBots.Count;
        TotalOnlineBots = visibleBots.Count + audibleBots.Count + occludedBots.Count;

        SortTierByCombatPriority(visibleBots);
        SortTierByCombatPriority(audibleBots);
        SortTierByCombatPriority(occludedBots);

        double visiblePhaseStopMs = MaxAIBudgetMs * VisibleTierBudgetFraction;
        double audiblePhaseStopMs = MaxAIBudgetMs * AudibleTierCumulativeFraction;

        ProcessTierRoundRobin(visibleBots, ref _resumeVisibleIndex, visiblePhaseStopMs, currentTime, deltaTime);
        if (_frameTimer.Elapsed.TotalMilliseconds < MaxAIBudgetMs)
        {
            ProcessTierRoundRobin(audibleBots, ref _resumeAudibleIndex, audiblePhaseStopMs, currentTime, deltaTime);
        }

        if (_frameTimer.Elapsed.TotalMilliseconds < MaxAIBudgetMs)
        {
            ProcessTierRoundRobin(occludedBots, ref _resumeOccludedIndex, MaxAIBudgetMs, currentTime, deltaTime);
        }

        BudgetExhaustedLastFrame = _frameTimer.Elapsed.TotalMilliseconds >= MaxAIBudgetMs;

        // Save actual elapsed time for the performance monitor
        BudgetElapsedMs = _frameTimer.Elapsed.TotalMilliseconds;

        // Diagnostic logging (throttled to every 2 seconds to avoid spam)
        if (SAINPerformanceMonitor.Instance?.DiagnosticLogging == true &&
            currentTime - _lastDiagLogTime >= 2f)
        {
            _lastDiagLogTime = currentTime;
            LogDiagnostics(currentTime);
        }
    }

    /// <summary>
    /// Resolve combat between offline squads using statistical model.
    /// Pairs squads in nearby zones, resolves combat, and schedules spoofed audio.
    /// </summary>
    private void ResolveOfflineSquadCombat(float currentTime)
    {
        var activeSquads = new List<OfflineSquad>();
        foreach (var squad in _offlineSquads)
        {
            if (squad.Members.Count > 0 && squad.IsHostileToOtherFaction)
                activeSquads.Add(squad);
        }

        int combatsResolved = 0;
        for (int i = 0; i < activeSquads.Count; i++)
        {
            for (int j = i + 1; j < activeSquads.Count; j++)
            {
                var squadA = activeSquads[i];
                var squadB = activeSquads[j];

                float distSqr = (squadA.CenterPosition - squadB.CenterPosition).sqrMagnitude;
                if (distSqr > 400f * 400f)
                    continue;

                var result = OfflineCombatResolver.ResolveCombat(
                    squadA.Members, squadB.Members,
                    (squadA.CenterPosition + squadB.CenterPosition) * 0.5f);

                combatsResolved++;

                // Diagnostic: log offline combat
                if (SAINPerformanceMonitor.Instance?.DiagnosticLogging == true)
                {
                    UnityEngine.Debug.Log(
                        $"[SAIN DIAG] OfflineCombat: {squadA.Faction}({squadA.Members.Count}) vs "
                        + $"{squadB.Faction}({squadB.Members.Count}) @ {Mathf.Sqrt(distSqr):F0}m | "
                        + $"Winner={result.Winner}, KIA: A={result.CasualtiesA} B={result.CasualtiesB}"
                    );
                }

                var audioSpoofer = BotManagerComponent.Instance?.GetComponent<CombatAudioSpoofer>();
                if (audioSpoofer == null && BotManagerComponent.Instance != null)
                {
                    audioSpoofer = BotManagerComponent.Instance.gameObject.AddComponent<CombatAudioSpoofer>();
                }
                audioSpoofer?.ScheduleOfflineCombatAudio(result);

                ApplyOfflineCasualties(squadA, squadB, result);
            }
        }

        if (combatsResolved > 0 && SAINPerformanceMonitor.Instance?.DiagnosticLogging == true)
        {
            UnityEngine.Debug.Log(
                $"[SAIN DIAG] OfflineCombat: Resolved {combatsResolved} engagement(s) across "
                + $"{OfflineSquadCount} offline squads"
            );
        }
    }

    private static void ApplyOfflineCasualties(OfflineSquad squadA, OfflineSquad squadB, OfflineCombatResult result)
    {
        // Mark bots as dead in offline tracking
        for (int i = 0; i < result.CasualtiesA && squadA.Members.Count > 0; i++)
        {
            squadA.Members.RemoveAt(squadA.Members.Count - 1);
        }
        for (int i = 0; i < result.CasualtiesB && squadB.Members.Count > 0; i++)
        {
            squadB.Members.RemoveAt(squadB.Members.Count - 1);
        }
    }

    private float _lastOfflineCombatTime;
    private float _lastDiagLogTime;

    private bool ShouldStopProcessing()
    {
        return _frameTimer.Elapsed.TotalMilliseconds >= MaxAIBudgetMs;
    }

    /// <summary>
    /// Bots actively fighting the human player get processed first within a tier so vision
    /// and shooting logic stay fresh under frame caps.
    /// </summary>
    private static void SortTierByCombatPriority(List<BotComponent> bots)
    {
        if (bots == null || bots.Count < 2)
        {
            return;
        }

        bots.Sort(static (a, b) =>
        {
            float da = GetHumanGoalEnemyDistance(a);
            float db = GetHumanGoalEnemyDistance(b);
            int cmp = da.CompareTo(db);
            if (cmp != 0)
            {
                return cmp;
            }

            return string.CompareOrdinal(a?.name, b?.name);
        });
    }

    private static float GetHumanGoalEnemyDistance(BotComponent bot)
    {
        var ge = bot?.EnemyController?.GoalEnemy;
        if (ge == null || ge.IsAI)
        {
            return float.MaxValue;
        }

        return ge.RealDistance;
    }

    /// <summary>
    /// One fair pass (or partial pass) over a tier: round-robin from resume cursor until
    /// <paramref name="phaseStopMs"/> or global <see cref="MaxAIBudgetMs"/> is reached.
    /// </summary>
    private void ProcessTierRoundRobin(
        List<BotComponent> tierBots,
        ref int resumeIndex,
        double phaseStopMs,
        float currentTime,
        float deltaTime
    )
    {
        int n = tierBots?.Count ?? 0;
        if (n == 0)
        {
            return;
        }

        int start = resumeIndex % n;
        for (int i = 0; i < n; i++)
        {
            double elapsed = _frameTimer.Elapsed.TotalMilliseconds;
            if (elapsed >= phaseStopMs || elapsed >= MaxAIBudgetMs)
            {
                resumeIndex = (start + i) % n;
                return;
            }

            int idx = (start + i) % n;
            ProcessBot(tierBots[idx], currentTime, deltaTime);
            BotsProcessedThisFrame++;
        }

        resumeIndex = 0;
    }

    private static void ProcessBot(BotComponent bot, float currentTime, float deltaTime)
    {
        bot.ManualUpdate(currentTime, deltaTime);
    }

    /// <summary>
    /// When true, run this bot's SAIN ManualUpdate before tier/budget scheduling so GoalEnemy /
    /// combat decisions stay aligned with EFT under fire / goal memory (vanilla fallback otherwise).
    /// </summary>
    private static bool ShouldForceAiTick(BotComponent bot)
    {
        BotOwner owner = bot?.BotOwner;
        if (owner == null)
        {
            return false;
        }

        var enemyController = bot.EnemyController;
        if (enemyController != null)
        {
            // Keep SAIN in control whenever we already have combat/threat context, even if EFT memory
            // briefly drops GoalEnemy between updates. This avoids fallback to vanilla obfuscated layers.
            if (enemyController.ActiveHumanEnemy
                || enemyController.HumanEnemyInLineofSight
                || enemyController.GoalEnemy != null
                || enemyController.KnownEnemies.Count > 0)
            {
                return true;
            }
        }

        var decision = bot.Decision;
        if (decision != null
            && (decision.CurrentCombatDecision != ECombatDecision.None
                || decision.CurrentSquadDecision != ESquadDecision.None
                || decision.CurrentSelfDecision != ESelfActionType.None))
        {
            return true;
        }

        BotMemoryClass memory = owner.Memory;
        if (memory != null && (memory.IsUnderFire || memory.GoalEnemy != null))
        {
            return true;
        }

        // Recent damage: Medical ticks in the same ManualUpdate pipeline — pairing with under-fire covers most cases.
        var medical = bot.Medical;
        if (medical != null && medical.TimeLastShot > 0f && medical.TimeSinceShot < 3f)
        {
            return true;
        }

        return false;
    }

    /// <summary>Throttled diagnostic log — summary of AI processing this frame.</summary>
    private void LogDiagnostics(float currentTime)
    {
        string status = BudgetExhaustedLastFrame
            ? $"BUDGET EXHAUSTED at {BudgetElapsedMs:F2}ms of {MaxAIBudgetMs}ms cap"
            : $"OK — {BudgetElapsedMs:F2}ms / {MaxAIBudgetMs}ms";

        UnityEngine.Debug.Log(
            $"[SAIN DIAG] Frame: {status} | "
            + $"Bots: V={VisibleBotsLastFrame} A={AudibleBotsLastFrame} O={OccludedBotsLastFrame} "
            + $"(processed={BotsProcessedThisFrame}, skipped={BotsSkippedThisFrame}) | "
            + $"Offline squads: {OfflineSquadCount}"
        );
    }

    /// <summary>
    /// Reset scheduler state for a new raid.
    /// </summary>
    public void Reset()
    {
        _resumeVisibleIndex = 0;
        _resumeAudibleIndex = 0;
        _resumeOccludedIndex = 0;
        BudgetExhaustedLastFrame = false;
        BotsProcessedThisFrame = 0;
        TotalOnlineBots = 0;
        VisibleBotsLastFrame = 0;
        AudibleBotsLastFrame = 0;
        OccludedBotsLastFrame = 0;
        _offlineSquads.Clear();
        _lastOfflineCombatTime = 0f;
    }
}

/// <summary>
/// Represents a squad tracked in the offline combat system.
/// Members are BotCombatStats (statistics only, no GameObjects).
/// When the player approaches, offline squads materialize into real bots.
/// </summary>
public class OfflineSquad
{
    public string SquadId;
    public string Faction;
    public Vector3 CenterPosition;
    public List<BotCombatStats> Members = new();
    public bool IsHostileToOtherFaction = true;
}
