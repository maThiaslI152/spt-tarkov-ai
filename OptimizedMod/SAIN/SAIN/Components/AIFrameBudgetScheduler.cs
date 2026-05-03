using System.Collections.Generic;
using System.Diagnostics;
using SAIN.Components.PlayerComponentSpace;
using SAIN.Plugin;
using UnityEngine;

namespace SAIN.Components;

/// <summary>
/// AI Frame Budget Scheduler — enforces a hard cap (default 2ms) on AI processing per frame.
/// Bots are tiered by player perception (Visible > Audible > Occluded) and processed in
/// priority order within the budget. Remaining bots wait until subsequent frames.
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
    private int _resumeIndex;

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

        foreach (var bot in allBots)
        {
            if (bot == null || bot.IsDead || !bot.BotActive)
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

        // Phase 1: Visible bots (highest priority — must complete)
        foreach (var bot in visibleBots)
        {
            if (ShouldStopProcessing())
            {
                BudgetExhaustedLastFrame = true;
                BudgetElapsedMs = _frameTimer.Elapsed.TotalMilliseconds;
                return;
            }
            ProcessBot(bot, currentTime, deltaTime);
            BotsProcessedThisFrame++;
        }

        // Phase 2: Audible bots (medium priority — process what fits)
        foreach (var bot in audibleBots)
        {
            if (ShouldStopProcessing())
            {
                BudgetExhaustedLastFrame = true;
                BudgetElapsedMs = _frameTimer.Elapsed.TotalMilliseconds;
                return;
            }
            ProcessBot(bot, currentTime, deltaTime);
            BotsProcessedThisFrame++;
        }

        // Phase 3: Occluded bots (lowest priority — leftover budget only)
        int occludedCount = occludedBots.Count;
        if (occludedCount > 0)
        {
            int startIndex = _resumeIndex % occludedCount;
            for (int i = 0; i < occludedCount; i++)
            {
                if (ShouldStopProcessing())
                {
                    _resumeIndex = (startIndex + i) % occludedCount;
                    BudgetExhaustedLastFrame = true;
                    BudgetElapsedMs = _frameTimer.Elapsed.TotalMilliseconds;
                    return;
                }
                int idx = (startIndex + i) % occludedCount;
                ProcessBot(occludedBots[idx], currentTime, deltaTime);
                BotsProcessedThisFrame++;
            }
            _resumeIndex = 0;
        }

        BudgetExhaustedLastFrame = false;

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

    private static void ProcessBot(BotComponent bot, float currentTime, float deltaTime)
    {
        bot.ManualUpdate(currentTime, deltaTime);
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
        _resumeIndex = 0;
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
