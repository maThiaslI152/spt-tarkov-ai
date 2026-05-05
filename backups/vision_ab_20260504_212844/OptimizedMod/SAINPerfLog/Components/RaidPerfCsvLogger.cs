using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using SAIN;
using SAIN.Components;
using SAIN.Interop;
using SAIN.Layers.Combat.Squad;
using UnityEngine;

namespace SAINPerfLog.Components;

public class RaidPerfCsvLogger : MonoBehaviour
{
    /// <summary>
    /// BigBrain snapshot CSV:
    /// v5 adds hierarchical-collapse telemetry: squad-command utilization, local decision skips/preemptions,
    /// and estimated decision CPU delta (executed vs skipped/saved).
    /// </summary>
    private const int BigBrainSchemaVersion = 6;

    private const int MaxMismatchExemplars = 6;
    private const float NearDistanceMeters = 30f;
    private const float MidDistanceMeters = 80f;

    /// <summary>Wait this long for <see cref="EFT.GameWorld.LocationId"/> before falling back to <c>unknown</c> in filenames.</summary>
    private const float WriterOpenTimeoutSec = 30f;

    /// <summary>Current per-raid perf CSV path, for F12 readouts; empty when not in a raid.</summary>
    public static string ActivePerfCsvPath { get; private set; } = string.Empty;

    /// <summary>Current per-raid BigBrain snapshot CSV path, if snapshots enabled.</summary>
    public static string ActiveBigBrainCsvPath { get; private set; } = string.Empty;

    private EFT.GameWorld _gameWorld;
    private DateTime _raidStartUtc;
    private float _raidStartTime;
    private string _sessionToken;
    private string _locationId;
    private bool _writersOpened;

    private StreamWriter _perfWriter;
    private StreamWriter _bigBrainWriter;
    private string _perfPath;
    private string _bigBrainPath;

    private float _nextPerfLogTime;
    private float _nextBigBrainSnapshotTime;
    private float _nextBigBrainHeartbeatTime;

    private int _budgetExhaustedFrames;
    private int _totalFrames;

    private string _lastLayerHistogram;
    private int _lastMismatchCount = -1;
    private readonly Stopwatch _runStopwatch = new();
    private long _lastDecisionTicksTotal;
    private long _lastDecisionSkipsTotal;
    private long _lastDecisionPreemptionsTotal;
    private long _lastSquadOrdersReceivedTotal;
    private double _lastDecisionExecutedCpuMsTotal;
    private double _lastDecisionSavedCpuMsTotal;

    public void Initialize(EFT.GameWorld gameWorld)
    {
        _gameWorld = gameWorld;
        _raidStartUtc = DateTime.UtcNow;
        _raidStartTime = Time.time;
        _sessionToken = Guid.NewGuid().ToString("N")[..8];
        _runStopwatch.Restart();
        _lastDecisionTicksTotal = 0;
        _lastDecisionSkipsTotal = 0;
        _lastDecisionPreemptionsTotal = 0;
        _lastSquadOrdersReceivedTotal = 0;
        _lastDecisionExecutedCpuMsTotal = 0d;
        _lastDecisionSavedCpuMsTotal = 0d;

        // Do not open writers here: LocationId is often still empty during GameWorldUnityTickListener.Create.
        GameWorld.OnDispose += OnGameWorldDispose;
    }

    private void Update()
    {
        if (_gameWorld == null || PerfLogPlugin.Instance == null)
        {
            return;
        }

        TryEnsureWritersOpen();
        if (!_writersOpened)
        {
            return;
        }

        _totalFrames++;
        AIFrameBudgetScheduler scheduler = AIFrameBudgetScheduler.Instance;
        if (scheduler != null && scheduler.BudgetExhaustedLastFrame)
        {
            _budgetExhaustedFrames++;
        }

        if (Time.time >= _nextPerfLogTime)
        {
            _nextPerfLogTime = Time.time + Mathf.Max(1f, PerfLogPlugin.PerfLogIntervalSec.Value);
            WritePerfRow();
        }

        if (PerfLogPlugin.EnableBigBrainSnapshots.Value && Time.time >= _nextBigBrainSnapshotTime)
        {
            _nextBigBrainSnapshotTime = Time.time + Mathf.Max(5f, PerfLogPlugin.BigBrainSnapshotIntervalSec.Value);
            WriteBigBrainSnapshot();
        }
    }

    private void OnDestroy()
    {
        Cleanup();
    }

    private void OnGameWorldDispose()
    {
        Cleanup();
    }

    private void Cleanup()
    {
        if (_gameWorld != null)
        {
            GameWorld.OnDispose -= OnGameWorldDispose;
            _gameWorld = null;
        }

        _runStopwatch.Stop();
        CloseWriter(ref _perfWriter);
        CloseWriter(ref _bigBrainWriter);
        ActivePerfCsvPath = string.Empty;
        ActiveBigBrainCsvPath = string.Empty;
        _writersOpened = false;
    }

    private void TryEnsureWritersOpen()
    {
        if (_writersOpened)
        {
            return;
        }

        string resolved = ResolveLocationIdBestEffort(_gameWorld);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            if (Time.time - _raidStartTime < WriterOpenTimeoutSec)
            {
                return;
            }

            resolved = "unknown";
        }

        _locationId = resolved.Trim();
        OpenWriters();
        _writersOpened = true;
    }

    private void OpenWriters()
    {
        string outputDir = PerfLogPlugin.Instance.GetOutputDirectory();
        string stamp = _raidStartUtc.ToString("yyyyMMdd_HHmmss");
        string safeLocation = Sanitize(_locationId);
        _perfPath = Path.Combine(outputDir, $"sain_perf_{stamp}_{safeLocation}_{_sessionToken}.csv");
        _bigBrainPath = Path.Combine(outputDir, $"sain_bigbrain_{stamp}_{safeLocation}_{_sessionToken}.csv");
        ActivePerfCsvPath = _perfPath;
        ActiveBigBrainCsvPath = string.Empty;

        UTF8Encoding utf8Bom = new(encoderShouldEmitUTF8Identifier: true);
        _perfWriter = new StreamWriter(_perfPath, append: false, utf8Bom) { AutoFlush = false };
        _perfWriter.WriteLine(
            "Timestamp,FPS,FrameTimeMs,BudgetMs,BudgetLimitMs,BudgetUtil%,BudgetExhaustedNow,BudgetExhausted%,BudgetHeadroomMs,ProcessedBots,SkippedBots,VisibleBots,AudibleBots,OccludedBots,OfflineSquads,TotalOnline,Pooled,ActivePool,RaidElapsedSec,LocationId,SessionId"
        );
        _perfWriter.Flush();

        if (PerfLogPlugin.EnableBigBrainSnapshots.Value)
        {
            _bigBrainWriter = new StreamWriter(_bigBrainPath, append: false, utf8Bom) { AutoFlush = false };
            ActiveBigBrainCsvPath = _bigBrainPath;
            _bigBrainWriter.WriteLine(
                "SchemaVersion,TimestampUtc,RaidElapsedSec,SainBotsTotal,SainBotsSampled,LayerHistogram,MismatchCombatSignals,LocationId,SessionId," +
                "CustomBigBrainActiveCount,SignalGoalEnemy,SignalCombatNonNone,SignalSquadNonNone,SignalPressure,SignalAnyBots," +
                "DistNearCount,DistMidCount,DistFarCount,EngagedNearCount,EngagedMidCount,EngagedFarCount,ExUsecEngagedNearCount,ExUsecEngagedMidCount,ExUsecEngagedFarCount,CanShootNowNearCount,CanShootNowMidCount,CanShootNowFarCount," +
                "GoalHumanCount,GoalHumanEnemyInfoVisibleCount,GoalHumanSainPartsVisibleCount,GoalHumanSainPartsLineOfSightCount,GoalHumanSainPartsCanShootCount,GoalHumanFinalVisibleCount,GoalHumanFinalCanShootCount,GoalHumanVisibleDisagreeEnemyInfo0Parts1Count,GoalHumanVisibleDisagreeEnemyInfo1Parts0Count," +
                "SquadCommandedNowCount,SquadCommandUtilNowPct,DecisionTicksDelta,DecisionSkipsDelta,DecisionSkipRatePct,DecisionPreemptionsDelta,SquadOrdersReceivedDelta,DecisionCpuExecutedDeltaMs,DecisionCpuSavedDeltaMs,DecisionCpuDeltaMs,DecisionCpuSavedPerSkipMs," +
                "MismatchLayerHistogram,MismatchReasonHistogram,PerceptionTierHistogram,MismatchExemplars");
            _bigBrainWriter.Flush();
            _nextBigBrainHeartbeatTime = Time.time + Mathf.Max(60f, PerfLogPlugin.SnapshotHeartbeatMinutes.Value * 60f);
        }

        _nextPerfLogTime = Time.time + Mathf.Max(1f, PerfLogPlugin.PerfLogIntervalSec.Value);
        _nextBigBrainSnapshotTime = Time.time + Mathf.Max(5f, PerfLogPlugin.BigBrainSnapshotIntervalSec.Value);
    }

    private void WritePerfRow()
    {
        if (_perfWriter == null)
        {
            return;
        }

        AIFrameBudgetScheduler scheduler = AIFrameBudgetScheduler.Instance;
        if (scheduler == null)
        {
            return;
        }

        float frameMs = Time.unscaledDeltaTime * 1000f;
        float fps = frameMs > 0f ? 1000f / frameMs : 0f;
        float budgetUsed = (float)scheduler.BudgetElapsedMs;
        float budgetLimit = scheduler.MaxAIBudgetMs;
        float budgetUtil = budgetLimit > 0f ? (budgetUsed / budgetLimit * 100f) : 0f;
        float budgetHeadroom = Mathf.Max(0f, budgetLimit - budgetUsed);
        float exhaustedRate = _totalFrames > 0 ? _budgetExhaustedFrames / (float)_totalFrames * 100f : 0f;

        int pooled = 0;
        int activePool = 0;
        TryReadPoolStats(ref pooled, ref activePool);

        float raidElapsed = Mathf.Max(0f, Time.time - _raidStartTime);
        string rowLocation = CsvEscape(ResolveLocationIdBestEffort(_gameWorld) ?? _locationId ?? "unknown");

        _perfWriter.WriteLine(
            $"{DateTime.UtcNow:O},{fps:F1},{frameMs:F2},{budgetUsed:F3},{budgetLimit:F1},{budgetUtil:F1},{(scheduler.BudgetExhaustedLastFrame ? 1 : 0)},{exhaustedRate:F1},{budgetHeadroom:F3},{scheduler.BotsProcessedThisFrame},{scheduler.BotsSkippedThisFrame},{scheduler.VisibleBotsLastFrame},{scheduler.AudibleBotsLastFrame},{scheduler.OccludedBotsLastFrame},{scheduler.OfflineSquadCount},{scheduler.TotalOnlineBots},{pooled},{activePool},{raidElapsed:F1},{rowLocation},{CsvEscape(_sessionToken)}"
        );
        _perfWriter.Flush();

        if (PerfLogPlugin.WriteLatestAlias.Value)
        {
            File.Copy(_perfPath, Path.Combine(Path.GetDirectoryName(_perfPath) ?? string.Empty, "sain_perf_latest.csv"), true);
        }
    }

    private void WriteBigBrainSnapshot()
    {
        if (_bigBrainWriter == null || BotManagerComponent.Instance == null)
        {
            return;
        }

        HashSet<BotComponent> bots = BotManagerComponent.Instance.BotSpawnController?.SAINBots;
        if (bots == null)
        {
            return;
        }

        Dictionary<string, int> histogram = new(StringComparer.Ordinal);
        Dictionary<string, int> mismatchLayerHist = new(StringComparer.Ordinal);
        Dictionary<string, int> mismatchReasonHist = new(StringComparer.Ordinal);
        Dictionary<string, int> tierHist = new(StringComparer.Ordinal);
        List<string> mismatchExemplars = new();

        int mismatchCount = 0;
        int sampled = 0;
        int customBigBrainActive = 0;
        int signalGoalEnemy = 0;
        int signalCombatNonNone = 0;
        int signalSquadNonNone = 0;
        int signalPressure = 0;
        int signalAnyBots = 0;
        int distNearCount = 0;
        int distMidCount = 0;
        int distFarCount = 0;
        int engagedNearCount = 0;
        int engagedMidCount = 0;
        int engagedFarCount = 0;
        int exUsecEngagedNearCount = 0;
        int exUsecEngagedMidCount = 0;
        int exUsecEngagedFarCount = 0;
        int canShootNowNearCount = 0;
        int canShootNowMidCount = 0;
        int canShootNowFarCount = 0;
        int goalHumanCount = 0;
        int goalHumanEnemyInfoVisibleCount = 0;
        int goalHumanSainPartsVisibleCount = 0;
        int goalHumanSainPartsLineOfSightCount = 0;
        int goalHumanSainPartsCanShootCount = 0;
        int goalHumanFinalVisibleCount = 0;
        int goalHumanFinalCanShootCount = 0;
        int goalHumanVisibleDisagreeEnemyInfo0Parts1Count = 0;
        int goalHumanVisibleDisagreeEnemyInfo1Parts0Count = 0;
        int squadCommandedNowCount = 0;
        long decisionTicksTotal = 0;
        long decisionSkipsTotal = 0;
        long decisionPreemptionsTotal = 0;
        long squadOrdersReceivedTotal = 0;
        double decisionExecutedCpuMsTotal = 0d;
        double decisionSavedCpuMsTotal = 0d;
        bool hasMainPlayer = TryGetMainPlayerPosition(out Vector3 mainPlayerPos);

        foreach (BotComponent bot in bots)
        {
            if (bot == null || bot.IsDead || !bot.BotActive)
            {
                continue;
            }

            BotOwner owner = bot.BotOwner;
            if (owner == null || owner.IsDead)
            {
                continue;
            }

            string layer = BrainManager.GetActiveLayerName(owner);
            if (string.IsNullOrEmpty(layer))
            {
                layer = "None";
            }

            histogram.TryGetValue(layer, out int count);
            histogram[layer] = count + 1;
            sampled++;

            if (BrainManager.IsCustomLayerActive(owner))
            {
                customBigBrainActive++;
            }

            bool goal = bot.GoalEnemy != null;
            bool combat = bot.Decision.CurrentCombatDecision != ECombatDecision.None;
            bool squad = bot.Decision.CurrentSquadDecision != ESquadDecision.None;
            bool canShootNow = bot.GoalEnemy != null && bot.GoalEnemy.IsVisible && bot.GoalEnemy.CanShoot;
            if (bot.GoalEnemy != null && !bot.GoalEnemy.IsAI)
            {
                goalHumanCount++;

                bool enemyInfoVisible = false;
                bool partsVisible = false;
                bool partsLineOfSight = false;
                bool partsCanShoot = false;
                bool finalVisible = false;
                bool finalCanShoot = false;

                try
                {
                    enemyInfoVisible = bot.GoalEnemy.EnemyInfo?.IsVisible == true;
                }
                catch
                {
                    enemyInfoVisible = false;
                }

                try
                {
                    var parts = bot.GoalEnemy.Vision?.EnemyParts;
                    partsVisible = parts?.CanBeSeen == true;
                    partsLineOfSight = parts?.LineOfSight == true;
                    partsCanShoot = parts?.CanShoot == true;
                }
                catch
                {
                    partsVisible = false;
                    partsLineOfSight = false;
                    partsCanShoot = false;
                }

                finalVisible = bot.GoalEnemy.IsVisible;
                finalCanShoot = bot.GoalEnemy.CanShoot;

                if (enemyInfoVisible)
                {
                    goalHumanEnemyInfoVisibleCount++;
                }
                if (partsVisible)
                {
                    goalHumanSainPartsVisibleCount++;
                }
                if (partsLineOfSight)
                {
                    goalHumanSainPartsLineOfSightCount++;
                }
                if (partsCanShoot)
                {
                    goalHumanSainPartsCanShootCount++;
                }
                if (finalVisible)
                {
                    goalHumanFinalVisibleCount++;
                }
                if (finalCanShoot)
                {
                    goalHumanFinalCanShootCount++;
                }
                if (!enemyInfoVisible && partsVisible)
                {
                    goalHumanVisibleDisagreeEnemyInfo0Parts1Count++;
                }
                if (enemyInfoVisible && !partsVisible)
                {
                    goalHumanVisibleDisagreeEnemyInfo1Parts0Count++;
                }
            }
            bool pressure = false;
            try
            {
                pressure = SAINExternal.IsBotUnderCombatPressure(owner);
            }
            catch
            {
                pressure = false;
            }

            if (goal)
            {
                signalGoalEnemy++;
            }

            if (combat)
            {
                signalCombatNonNone++;
            }

            if (squad)
            {
                signalSquadNonNone++;
            }

            if (pressure)
            {
                signalPressure++;
            }

            if (goal || combat || squad || pressure)
            {
                signalAnyBots++;
            }

            bool squadCommandedNow = squad || SquadCombatCoordinator.HasActiveOrder(bot);
            if (squadCommandedNow)
            {
                squadCommandedNowCount++;
            }

            var decisionManager = bot.Decision?.DecisionManager;
            if (decisionManager != null)
            {
                decisionTicksTotal += decisionManager.DecisionTicksTotal;
                decisionSkipsTotal += decisionManager.DecisionSkipsSquadOrderTotal;
                decisionPreemptionsTotal += decisionManager.DecisionPreemptionsTotal;
                squadOrdersReceivedTotal += decisionManager.SquadOrdersReceivedTotal;
                decisionExecutedCpuMsTotal += decisionManager.DecisionCpuExecutedTotalMs;
                decisionSavedCpuMsTotal += decisionManager.DecisionCpuEstimatedSavedTotalMs;
            }

            if (hasMainPlayer)
            {
                float distanceToPlayer = Vector3.Distance(bot.Position, mainPlayerPos);
                bool engaged = goal || combat || pressure;
                bool isExUsec = bot.Info?.Profile?.WildSpawnType == WildSpawnType.exUsec;

                if (distanceToPlayer < NearDistanceMeters)
                {
                    distNearCount++;
                    if (engaged)
                    {
                        engagedNearCount++;
                        if (isExUsec)
                        {
                            exUsecEngagedNearCount++;
                        }
                    }
                    if (canShootNow)
                    {
                        canShootNowNearCount++;
                    }
                }
                else if (distanceToPlayer < MidDistanceMeters)
                {
                    distMidCount++;
                    if (engaged)
                    {
                        engagedMidCount++;
                        if (isExUsec)
                        {
                            exUsecEngagedMidCount++;
                        }
                    }
                    if (canShootNow)
                    {
                        canShootNowMidCount++;
                    }
                }
                else
                {
                    distFarCount++;
                    if (engaged)
                    {
                        engagedFarCount++;
                        if (isExUsec)
                        {
                            exUsecEngagedFarCount++;
                        }
                    }
                    if (canShootNow)
                    {
                        canShootNowFarCount++;
                    }
                }
            }

            string tierKey = bot.CurrentPerceptionTier.ToString();
            tierHist.TryGetValue(tierKey, out int tCount);
            tierHist[tierKey] = tCount + 1;

            bool mismatch = BotManagerComponent.EvaluateBigBrainPriorityMismatch(bot, owner, layer);
            if (mismatch)
            {
                mismatchCount++;
                mismatchLayerHist.TryGetValue(layer, out int ml);
                mismatchLayerHist[layer] = ml + 1;

                string reason = BotManagerComponent.DescribeBigBrainMismatchReason(bot, owner, layer);
                mismatchReasonHist.TryGetValue(reason, out int mr);
                mismatchReasonHist[reason] = mr + 1;

                if (mismatchExemplars.Count < MaxMismatchExemplars)
                {
                    mismatchExemplars.Add(BuildMismatchExemplar(bot, owner, layer, goal, combat, squad, pressure));
                }
            }
        }

        string histogramString = BuildHistogram(histogram);
        bool isHeartbeat = Time.time >= _nextBigBrainHeartbeatTime;
        bool unchanged = histogramString == _lastLayerHistogram && mismatchCount == _lastMismatchCount;
        bool shouldCoalesce = PerfLogPlugin.CoalesceIdleSnapshots.Value && unchanged && mismatchCount == 0 && !isHeartbeat;
        if (shouldCoalesce)
        {
            return;
        }

        if (isHeartbeat)
        {
            _nextBigBrainHeartbeatTime = Time.time + Mathf.Max(60f, PerfLogPlugin.SnapshotHeartbeatMinutes.Value * 60f);
        }

        float raidElapsed = Mathf.Max(0f, Time.time - _raidStartTime);
        string snapLocation = CsvEscape(ResolveLocationIdBestEffort(_gameWorld) ?? _locationId ?? "unknown");
        string mismatchLayerString = BuildHistogram(mismatchLayerHist);
        string mismatchReasonString = BuildHistogram(mismatchReasonHist);
        string tierString = BuildHistogram(tierHist);
        string exemplarsJoined = mismatchExemplars.Count == 0 ? "-" : string.Join("||", mismatchExemplars);
        long decisionTicksDelta = Math.Max(0, decisionTicksTotal - _lastDecisionTicksTotal);
        long decisionSkipsDelta = Math.Max(0, decisionSkipsTotal - _lastDecisionSkipsTotal);
        long decisionPreemptionsDelta = Math.Max(0, decisionPreemptionsTotal - _lastDecisionPreemptionsTotal);
        long squadOrdersReceivedDelta = Math.Max(0, squadOrdersReceivedTotal - _lastSquadOrdersReceivedTotal);
        double decisionExecutedCpuDeltaMs = Math.Max(0d, decisionExecutedCpuMsTotal - _lastDecisionExecutedCpuMsTotal);
        double decisionSavedCpuDeltaMs = Math.Max(0d, decisionSavedCpuMsTotal - _lastDecisionSavedCpuMsTotal);
        double decisionCpuDeltaMs = decisionSavedCpuDeltaMs - decisionExecutedCpuDeltaMs;
        double decisionSkipRatePct = decisionTicksDelta > 0 ? decisionSkipsDelta / (double)decisionTicksDelta * 100d : 0d;
        double squadCommandUtilNowPct = sampled > 0 ? squadCommandedNowCount / (double)sampled * 100d : 0d;
        double decisionCpuSavedPerSkipMs = decisionSkipsDelta > 0 ? decisionSavedCpuDeltaMs / decisionSkipsDelta : 0d;

        _bigBrainWriter.WriteLine(
            $"{BigBrainSchemaVersion},{DateTime.UtcNow:O},{raidElapsed:F1},{bots.Count},{sampled},\"{histogramString}\",{mismatchCount},{snapLocation},{CsvEscape(_sessionToken)}," +
            $"{customBigBrainActive},{signalGoalEnemy},{signalCombatNonNone},{signalSquadNonNone},{signalPressure},{signalAnyBots}," +
            $"{distNearCount},{distMidCount},{distFarCount},{engagedNearCount},{engagedMidCount},{engagedFarCount},{exUsecEngagedNearCount},{exUsecEngagedMidCount},{exUsecEngagedFarCount},{canShootNowNearCount},{canShootNowMidCount},{canShootNowFarCount}," +
            $"{goalHumanCount},{goalHumanEnemyInfoVisibleCount},{goalHumanSainPartsVisibleCount},{goalHumanSainPartsLineOfSightCount},{goalHumanSainPartsCanShootCount},{goalHumanFinalVisibleCount},{goalHumanFinalCanShootCount},{goalHumanVisibleDisagreeEnemyInfo0Parts1Count},{goalHumanVisibleDisagreeEnemyInfo1Parts0Count}," +
            $"{squadCommandedNowCount},{squadCommandUtilNowPct:F1},{decisionTicksDelta},{decisionSkipsDelta},{decisionSkipRatePct:F1},{decisionPreemptionsDelta},{squadOrdersReceivedDelta},{decisionExecutedCpuDeltaMs:F3},{decisionSavedCpuDeltaMs:F3},{decisionCpuDeltaMs:F3},{decisionCpuSavedPerSkipMs:F4}," +
            $"\"{mismatchLayerString}\",\"{mismatchReasonString}\",\"{tierString}\",{CsvEscape(exemplarsJoined)}"
        );
        _bigBrainWriter.Flush();

        _lastLayerHistogram = histogramString;
        _lastMismatchCount = mismatchCount;
        _lastDecisionTicksTotal = decisionTicksTotal;
        _lastDecisionSkipsTotal = decisionSkipsTotal;
        _lastDecisionPreemptionsTotal = decisionPreemptionsTotal;
        _lastSquadOrdersReceivedTotal = squadOrdersReceivedTotal;
        _lastDecisionExecutedCpuMsTotal = decisionExecutedCpuMsTotal;
        _lastDecisionSavedCpuMsTotal = decisionSavedCpuMsTotal;

        if (PerfLogPlugin.WriteLatestAlias.Value)
        {
            File.Copy(_bigBrainPath, Path.Combine(Path.GetDirectoryName(_bigBrainPath) ?? string.Empty, "sain_bigbrain_latest.csv"), true);
        }
    }

    private static string BuildMismatchExemplar(
        BotComponent bot,
        BotOwner owner,
        string bigBrainLayer,
        bool goal,
        bool combat,
        bool squad,
        bool pressure)
    {
        string nick = owner.Profile?.Nickname ?? owner.Profile?.Id ?? owner.name ?? "bot";
        string spawn = bot.Info?.Profile?.WildSpawnType.ToString() ?? "?";
        string goalShort =
            bot.GoalEnemy?.EnemyPlayer?.Profile?.Nickname
            ?? bot.GoalEnemy?.EnemyProfileId
            ?? "-";
        bool customBb = BrainManager.IsCustomLayerActive(owner);
        return string.Join(
            "~",
            TelemetryToken(nick, 20),
            TelemetryToken(spawn, 24),
            TelemetryToken(bigBrainLayer, 36),
            goal ? "G1" : "G0",
            combat ? "C1" : "C0",
            squad ? "S1" : "S0",
            pressure ? "P1" : "P0",
            bot.SAINLayersActive ? "SL1" : "SL0",
            TelemetryToken(bot.ActiveLayer.ToString(), 22),
            customBb ? "BB1" : "BB0",
            TelemetryToken(goalShort, 18));
    }

    private bool TryGetMainPlayerPosition(out Vector3 position)
    {
        position = default;
        try
        {
            Player mainPlayer = _gameWorld?.MainPlayer;
            if (mainPlayer == null)
            {
                return false;
            }

            position = mainPlayer.Transform != null ? mainPlayer.Transform.position : mainPlayer.Position;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string TelemetryToken(string value, int maxLen)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "-";
        }

        ReadOnlySpan<char> span = value.AsSpan().Trim();
        StringBuilder sb = new(Math.Min(maxLen, span.Length));
        for (int i = 0; i < span.Length && sb.Length < maxLen; i++)
        {
            char c = span[i];
            if (c is '~' or '|' or '"' or '\r' or '\n' or ',')
            {
                sb.Append('_');
            }
            else if (char.IsControl(c))
            {
                sb.Append('_');
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.Length == 0 ? "-" : sb.ToString();
    }

    private static string BuildHistogram(Dictionary<string, int> histogram)
    {
        if (histogram.Count == 0)
        {
            return "none=0";
        }

        StringBuilder sb = new();
        foreach ((string key, int value) in histogram.OrderByDescending(kvp => kvp.Value).ThenBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            if (sb.Length > 0)
            {
                sb.Append('|');
            }
            sb.Append(key.Replace('|', '/').Replace('"', '\''));
            sb.Append('=');
            sb.Append(value);
        }
        return sb.ToString();
    }

    private static void TryReadPoolStats(ref int pooled, ref int activePool)
    {
        BotGameObjectPool pool = BotGameObjectPool.Instance;
        if (pool == null)
        {
            return;
        }

        try
        {
            PropertyInfo totalProperty = typeof(BotGameObjectPool).GetProperty("TotalPooledCount", BindingFlags.Public | BindingFlags.Instance);
            if (totalProperty != null)
            {
                pooled = (int)totalProperty.GetValue(pool);
            }
        }
        catch
        {
            pooled = 0;
        }

        try
        {
            PropertyInfo activeProperty = typeof(BotGameObjectPool).GetProperty("ActivePooledCount", BindingFlags.Public | BindingFlags.Instance);
            if (activeProperty != null)
            {
                activePool = (int)activeProperty.GetValue(pool);
            }
        }
        catch
        {
            activePool = 0;
        }
    }

    /// <summary>
    /// Prefer direct <see cref="EFT.GameWorld.LocationId"/> (compile-time binding); reflection on <see cref="EFT.GameWorld"/> only
    /// as fallback — <see cref="Type.GetProperty(string)"/> does not search base types, which caused false "unknown" on subclasses.
    /// </summary>
    private static string ResolveLocationIdBestEffort(EFT.GameWorld gameWorld)
    {
        if (gameWorld == null)
        {
            return null;
        }

        try
        {
            string id = gameWorld.LocationId;
            if (!string.IsNullOrWhiteSpace(id))
            {
                return id.Trim();
            }
        }
        catch
        {
            // ignored
        }

        try
        {
            string mainPlayerLocation = gameWorld.MainPlayer?.Location;
            if (!string.IsNullOrWhiteSpace(mainPlayerLocation))
            {
                return mainPlayerLocation.Trim();
            }
        }
        catch
        {
            // ignored
        }

        try
        {
            PropertyInfo locationProperty = typeof(EFT.GameWorld).GetProperty(
                "LocationId",
                BindingFlags.Public | BindingFlags.Instance);
            if (locationProperty?.GetValue(gameWorld) is string reflected && !string.IsNullOrWhiteSpace(reflected))
            {
                return reflected.Trim();
            }
        }
        catch
        {
            // ignored
        }

        return null;
    }

    private static string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        StringBuilder sb = new(value.Length);
        foreach (char c in value)
        {
            if (Array.IndexOf(invalid, c) >= 0 || c == ' ')
            {
                sb.Append('_');
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static void CloseWriter(ref StreamWriter writer)
    {
        if (writer == null)
        {
            return;
        }

        try
        {
            writer.Flush();
            writer.Close();
        }
        catch
        {
            // ignored
        }
        finally
        {
            writer = null;
        }
    }
}
