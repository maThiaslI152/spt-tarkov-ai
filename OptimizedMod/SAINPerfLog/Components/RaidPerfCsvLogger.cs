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
using UnityEngine;

namespace SAINPerfLog.Components;

public class RaidPerfCsvLogger : MonoBehaviour
{
    private const int BigBrainSchemaVersion = 1;

    /// <summary>Current per-raid perf CSV path, for F12 readouts; empty when not in a raid.</summary>
    public static string ActivePerfCsvPath { get; private set; } = string.Empty;

    /// <summary>Current per-raid BigBrain snapshot CSV path, if snapshots enabled.</summary>
    public static string ActiveBigBrainCsvPath { get; private set; } = string.Empty;

    private EFT.GameWorld _gameWorld;
    private DateTime _raidStartUtc;
    private float _raidStartTime;
    private string _sessionToken;
    private string _locationId;

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

    public void Initialize(EFT.GameWorld gameWorld)
    {
        _gameWorld = gameWorld;
        _raidStartUtc = DateTime.UtcNow;
        _raidStartTime = Time.time;
        _locationId = ResolveLocationId(gameWorld);
        _sessionToken = Guid.NewGuid().ToString("N")[..8];
        _runStopwatch.Restart();

        OpenWriters();
        GameWorld.OnDispose += OnGameWorldDispose;
    }

    private void Update()
    {
        if (_gameWorld == null || PerfLogPlugin.Instance == null)
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

        _perfWriter = new StreamWriter(_perfPath, append: false) { AutoFlush = false };
        _perfWriter.WriteLine(
            "Timestamp,FPS,FrameTimeMs,BudgetMs,BudgetLimitMs,BudgetUtil%,BudgetExhaustedNow,BudgetExhausted%,BudgetHeadroomMs,ProcessedBots,SkippedBots,VisibleBots,AudibleBots,OccludedBots,OfflineSquads,TotalOnline,Pooled,ActivePool"
        );
        _perfWriter.Flush();

        if (PerfLogPlugin.EnableBigBrainSnapshots.Value)
        {
            _bigBrainWriter = new StreamWriter(_bigBrainPath, append: false) { AutoFlush = false };
            ActiveBigBrainCsvPath = _bigBrainPath;
            _bigBrainWriter.WriteLine("SchemaVersion,TimestampUtc,RaidElapsedSec,SainBotsTotal,SainBotsSampled,LayerHistogram,MismatchCombatSignals");
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

        _perfWriter.WriteLine(
            $"{DateTime.UtcNow:O},{fps:F1},{frameMs:F2},{budgetUsed:F3},{budgetLimit:F1},{budgetUtil:F1},{(scheduler.BudgetExhaustedLastFrame ? 1 : 0)},{exhaustedRate:F1},{budgetHeadroom:F3},{scheduler.BotsProcessedThisFrame},{scheduler.BotsSkippedThisFrame},{scheduler.VisibleBotsLastFrame},{scheduler.AudibleBotsLastFrame},{scheduler.OccludedBotsLastFrame},{scheduler.OfflineSquadCount},{scheduler.TotalOnlineBots},{pooled},{activePool}"
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
        int mismatchCount = 0;
        int sampled = 0;

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

            if (LooksLikePriorityMismatch(bot, layer))
            {
                mismatchCount++;
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
        _bigBrainWriter.WriteLine(
            $"{BigBrainSchemaVersion},{DateTime.UtcNow:O},{raidElapsed:F1},{bots.Count},{sampled},\"{histogramString}\",{mismatchCount}"
        );
        _bigBrainWriter.Flush();

        _lastLayerHistogram = histogramString;
        _lastMismatchCount = mismatchCount;

        if (PerfLogPlugin.WriteLatestAlias.Value)
        {
            File.Copy(_bigBrainPath, Path.Combine(Path.GetDirectoryName(_bigBrainPath) ?? string.Empty, "sain_bigbrain_latest.csv"), true);
        }
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

    private static bool LooksLikePriorityMismatch(BotComponent bot, string activeLayer)
    {
        if (string.IsNullOrEmpty(activeLayer))
        {
            return false;
        }

        bool sainCombatDriving =
            activeLayer.IndexOf("combat", StringComparison.OrdinalIgnoreCase) >= 0
            && (
                activeLayer.IndexOf("solo", StringComparison.OrdinalIgnoreCase) >= 0
                || activeLayer.IndexOf("squad", StringComparison.OrdinalIgnoreCase) >= 0
                || activeLayer.IndexOf("sain", StringComparison.OrdinalIgnoreCase) >= 0
            );
        bool obviousQuestDriving = activeLayer.IndexOf("quest", StringComparison.OrdinalIgnoreCase) >= 0;

        bool hasThreatSignals =
            bot.GoalEnemy != null
            || bot.Decision.CurrentCombatDecision != ECombatDecision.None
            || bot.Decision.CurrentSquadDecision != ESquadDecision.None;

        return hasThreatSignals && (obviousQuestDriving || !sainCombatDriving);
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

    private static string ResolveLocationId(EFT.GameWorld gameWorld)
    {
        const string fallback = "unknown";
        if (gameWorld == null)
        {
            return fallback;
        }

        try
        {
            PropertyInfo locationProperty = gameWorld.GetType().GetProperty("LocationId", BindingFlags.Public | BindingFlags.Instance);
            if (locationProperty?.GetValue(gameWorld) is string location && !string.IsNullOrWhiteSpace(location))
            {
                return location;
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
                return mainPlayerLocation;
            }
        }
        catch
        {
            // ignored
        }

        return fallback;
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
