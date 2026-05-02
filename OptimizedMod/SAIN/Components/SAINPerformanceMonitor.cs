using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BepInEx.Logging;
using SAIN.Components;
using UnityEngine;

namespace SAIN.Components;

/// <summary>
/// Real-time performance monitor that tracks AI frame budget, perception tier distributions,
/// bot pool stats, and frame timing. Accessible via F12 BepInEx Configuration Manager.
/// Logs structured CSV data to BepInEx/LogOutput/sain_perf.csv for offline analysis.
/// </summary>
public class SAINPerformanceMonitor : MonoBehaviour
{
    // ── Singleton ────────────────────────────────────────────────────────────
    private static SAINPerformanceMonitor _instance;
    public static SAINPerformanceMonitor Instance => _instance;

    // ── Real-time Stats (updated every frame) ─────────────────────────────────
    public int VisibleBots { get; private set; }
    public int AudibleBots { get; private set; }
    public int OccludedBots { get; private set; }
    public int OfflineSquadCount { get; private set; }
    public int TotalOnlineBots => VisibleBots + AudibleBots + OccludedBots;

    public float BudgetUsedMs { get; private set; }
    public float BudgetLimitMs { get; private set; } = 2.0f;
    public float BudgetUtilizationPercent => BudgetLimitMs > 0f ? (BudgetUsedMs / BudgetLimitMs * 100f) : 0f;

    public int BotsProcessedThisFrame { get; private set; }
    public int BotsSkippedThisFrame { get; private set; }
    public bool BudgetExhaustedThisFrame { get; private set; }

    public float FrameDeltaTimeMs { get; private set; }
    public float CurrentFPS => FrameDeltaTimeMs > 0f ? (1000f / FrameDeltaTimeMs) : 0f;

    public int PooledBotCount { get; private set; }
    public int ActivePoolBotCount { get; private set; }

    // ── Rolling Averages (last N frames) ──────────────────────────────────────
    private readonly RollingAverage _avgFrameTime = new(120);
    private readonly RollingAverage _avgBudgetMs = new(120);
    private readonly RollingAverage _avgBudgetUtilization = new(120);
    private int _budgetExhaustedFrameCount;
    private int _totalFrameCount;

    public float AvgFrameTimeMs => _avgFrameTime.Value;
    public float AvgBudgetMs => _avgBudgetMs.Value;
    public float AvgBudgetUtilization => _avgBudgetUtilization.Value;
    public float BudgetExhaustedRate => _totalFrameCount > 0
        ? (float)_budgetExhaustedFrameCount / _totalFrameCount * 100f
        : 0f;

    // ── Config (set from SAINPlugin F12 config entries) ───────────────────────
    public bool LoggingEnabled { get; set; } = true;
    public float LogIntervalSeconds { get; set; } = 5f;
    public bool LogToCSV { get; set; } = true;
    public bool VerboseLogging { get; set; }
    public bool DumpStatsRequested { get; set; }

    // ── Internal state ────────────────────────────────────────────────────────
    private StreamWriter _csvWriter;
    private string _csvPath;
    private float _lastLogTime;
    private float _lastCSVWriteTime;
    private bool _csvHeaderWritten;
    private readonly Stopwatch _frameTimer = new();

    // ── Unity Lifecycle ───────────────────────────────────────────────────────

    public void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }
        _instance = this;

        if (LogToCSV)
            InitializeCSV();
    }

    public void Update()
    {
        // Track frame timing
        FrameDeltaTimeMs = Time.unscaledDeltaTime * 1000f;
        _avgFrameTime.Add(FrameDeltaTimeMs);
        _totalFrameCount++;

        // Collect stats from scheduler
        CollectSchedulerStats();

        // Collect pool stats
        CollectPoolStats();

        // Handle dump request from F12
        if (DumpStatsRequested)
        {
            DumpStatsRequested = false;
            DumpStatsToLog();
        }

        // Periodic CSV logging
        if (LogToCSV && LoggingEnabled && Time.time - _lastCSVWriteTime >= LogIntervalSeconds)
        {
            _lastCSVWriteTime = Time.time;
            WriteCSVRow();
        }

        // Periodic BepInEx log
        if (LoggingEnabled && Time.time - _lastLogTime >= LogIntervalSeconds && VerboseLogging)
        {
            _lastLogTime = Time.time;
            LogSummary();
        }
    }

    public void OnDestroy()
    {
        if (_csvWriter != null)
        {
            _csvWriter.Flush();
            _csvWriter.Close();
            _csvWriter = null;
        }
    }

    // ── Data Collection ───────────────────────────────────────────────────────

    private void CollectSchedulerStats()
    {
        var scheduler = AIFrameBudgetScheduler.Instance;
        if (scheduler == null) return;

        _frameTimer.Restart();

        // Read tier counts from scheduler
        VisibleBots = GetVisibleBotCount(scheduler);
        AudibleBots = GetAudibleBotCount(scheduler);
        OccludedBots = GetOccludedBotCount(scheduler);
        OfflineSquadCount = GetOfflineSquadCount(scheduler);

        BudgetUsedMs = (float)_frameTimer.Elapsed.TotalMilliseconds;
        _avgBudgetMs.Add(BudgetUsedMs);
        _avgBudgetUtilization.Add(BudgetUtilizationPercent);

        if (BudgetUsedMs >= BudgetLimitMs)
        {
            BudgetExhaustedThisFrame = true;
            _budgetExhaustedFrameCount++;
        }
        else
        {
            BudgetExhaustedThisFrame = false;
        }
    }

    private static int GetVisibleBotCount(AIFrameBudgetScheduler scheduler)
    {
        try { return (int)typeof(AIFrameBudgetScheduler)
            .GetField("_visibleBots", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(scheduler) ?? 0; }
        catch { return 0; }
    }

    private static int GetAudibleBotCount(AIFrameBudgetScheduler scheduler)
    {
        try { return (int)typeof(AIFrameBudgetScheduler)
            .GetField("_audibleBots", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(scheduler) ?? 0; }
        catch { return 0; }
    }

    private static int GetOccludedBotCount(AIFrameBudgetScheduler scheduler)
    {
        try { return (int)typeof(AIFrameBudgetScheduler)
            .GetField("_occludedBots", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(scheduler) ?? 0; }
        catch { return 0; }
    }

    private static int GetOfflineSquadCount(AIFrameBudgetScheduler scheduler)
    {
        try { return (int)typeof(AIFrameBudgetScheduler)
            .GetField("_offlineSquads", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(scheduler) ?? 0; }
        catch { return 0; }
    }

    private void CollectPoolStats()
    {
        var pool = BotGameObjectPool.Instance;
        if (pool == null) return;

        try
        {
            var totalField = typeof(BotGameObjectPool).GetProperty("TotalPooledCount",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (totalField != null)
                PooledBotCount = (int)totalField.GetValue(pool);
        }
        catch { PooledBotCount = -1; }
    }

    // ── CSV Logging ───────────────────────────────────────────────────────────

    private void InitializeCSV()
    {
        try
        {
            string logDir = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput");
            Directory.CreateDirectory(logDir);
            _csvPath = Path.Combine(logDir, "sain_perf.csv");
            _csvWriter = new StreamWriter(_csvPath, append: false);
            _csvWriter.AutoFlush = false;
            WriteCSVHeader();
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"[SAIN PerfMon] Failed to initialize CSV: {ex.Message}");
            _csvWriter = null;
        }
    }

    private void WriteCSVHeader()
    {
        if (_csvWriter == null) return;
        _csvWriter.WriteLine("Timestamp,FPS,FrameTimeMs,BudgetMs,BudgetLimitMs,BudgetUtil%,BudgetExhausted%," +
            "VisibleBots,AudibleBots,OccludedBots,OfflineSquads,TotalOnline,Pooled,ActivePool");
        _csvWriter.Flush();
    }

    private void WriteCSVRow()
    {
        if (_csvWriter == null) return;
        try
        {
            _csvWriter.WriteLine($"{DateTime.UtcNow:O},{CurrentFPS:F1},{FrameDeltaTimeMs:F2}," +
                $"{BudgetUsedMs:F3},{BudgetLimitMs:F1},{BudgetUtilizationPercent:F1}," +
                $"{BudgetExhaustedRate:F1},{VisibleBots},{AudibleBots},{OccludedBots}," +
                $"{OfflineSquadCount},{TotalOnlineBots},{PooledBotCount},{ActivePoolBotCount}");
            _csvWriter.Flush();
        }
        catch { /* Silently fail — don't spam if disk is full */ }
    }

    // ── Logging / Dump ────────────────────────────────────────────────────────

    private void LogSummary()
    {
        UnityEngine.Debug.Log(
            $"[SAIN Perf] FPS:{CurrentFPS:F0} | Frame:{FrameDeltaTimeMs:F1}ms(avg {AvgFrameTimeMs:F1}) | " +
            $"AI Budget:{BudgetUsedMs:F2}/{BudgetLimitMs:F1}ms ({BudgetUtilizationPercent:F0}%) " +
            $"Exhausted:{BudgetExhaustedRate:F0}% | " +
            $"Bots V:{VisibleBots} A:{AudibleBots} O:{OccludedBots} Off:{OfflineSquadCount} | " +
            $"Pool:{PooledBotCount}"
        );
    }

    public void DumpStatsToLog()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("═══════════════════════════════════════════════");
        sb.AppendLine("          SAIN PERFORMANCE SNAPSHOT");
        sb.AppendLine("═══════════════════════════════════════════════");
        sb.AppendLine($"  FPS:              {CurrentFPS:F1}");
        sb.AppendLine($"  Frame Time:       {FrameDeltaTimeMs:F2} ms (avg {AvgFrameTimeMs:F2} ms)");
        sb.AppendLine($"  AI Budget Used:   {BudgetUsedMs:F3} / {BudgetLimitMs:F1} ms ({BudgetUtilizationPercent:F1}%)");
        sb.AppendLine($"  Budget Exhausted: {BudgetExhaustedRate:F1}% of frames");
        sb.AppendLine($"  ─────────────────────────────────────────────");
        sb.AppendLine($"  Visible Bots:     {VisibleBots}");
        sb.AppendLine($"  Audible Bots:     {AudibleBots}");
        sb.AppendLine($"  Occluded Bots:    {OccludedBots}");
        sb.AppendLine($"  Total Online:     {TotalOnlineBots}");
        sb.AppendLine($"  Offline Squads:   {OfflineSquadCount}");
        sb.AppendLine($"  ─────────────────────────────────────────────");
        sb.AppendLine($"  Pooled Bots:      {PooledBotCount}");
        sb.AppendLine($"  Active Pool:      {ActivePoolBotCount}");
        sb.AppendLine($"  Processed:        {BotsProcessedThisFrame} this frame");
        sb.AppendLine($"  Skipped:          {BotsSkippedThisFrame} this frame");
        sb.AppendLine($"  CSV Log:          {(_csvWriter != null ? _csvPath : "disabled")}");
        sb.AppendLine("═══════════════════════════════════════════════");
        UnityEngine.Debug.Log(sb.ToString());
    }

    // ── Rolling Average Utility ───────────────────────────────────────────────
    private class RollingAverage
    {
        private readonly float[] _values;
        private int _index;
        private int _count;

        public RollingAverage(int windowSize) { _values = new float[windowSize]; }

        public void Add(float value)
        {
            _values[_index] = value;
            _index = (_index + 1) % _values.Length;
            if (_count < _values.Length) _count++;
        }

        public float Value
        {
            get
            {
                if (_count == 0) return 0f;
                float sum = 0f;
                for (int i = 0; i < _count; i++) sum += _values[i];
                return sum / _count;
            }
        }
    }
}
