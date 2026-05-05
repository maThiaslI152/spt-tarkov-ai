using System;
using System.IO;
using System.Text;
using SAIN.Components;
using SAIN.Components.BotController;
using UnityEngine;

namespace SAINPerfLog.Components;

/// <summary>
/// Optional per-frame CSV: one row per unit of spawn/despawn/pool counter delta (gated by F12 Spawn Event Log).
/// </summary>
internal sealed class SpawnEventCsvLogger
{
    /// <summary>Empty when no active spawn-event file.</summary>
    public static string ActivePath { get; private set; } = string.Empty;

    private StreamWriter _writer;
    private string _sessionToken;
    private float _raidStartTime;

    private long _prevSpawn;
    private long _prevDespawn;
    private long _prevHit;
    private long _prevMiss;
    private long _prevReturn;
    private long _prevReject;
    private long _prevSmartDematApplied;
    private long _prevSmartRematLos;
    private long _prevSmartRematNear;
    private long _prevAutoSpawnAttempts;
    private long _prevAutoSpawnFailures;
    private long _prevAutoMatApplied;
    private long _prevGc;

    public void Open(string outputDir, string stamp, string safeLocation, string sessionToken, float raidStartTime)
    {
        Close();
        _sessionToken = sessionToken ?? string.Empty;
        _raidStartTime = raidStartTime;

        string path = Path.Combine(outputDir, $"sain_spawn_events_{stamp}_{safeLocation}_{sessionToken}.csv");
        UTF8Encoding utf8Bom = new(encoderShouldEmitUTF8Identifier: true);
        _writer = new StreamWriter(path, append: false, utf8Bom) { AutoFlush = false };
        ActivePath = path;
        _writer.WriteLine(
            "Timestamp,RaidElapsedSec,Event,BotType,FrameMs,GcAllocDeltaKb,PoolHit,TotalOnline,Pooled,SessionId"
        );
        _writer.Flush();
        SnapshotBaselines();
    }

    private void SnapshotBaselines()
    {
        BotSpawnController spawn = BotManagerComponent.Instance?.BotSpawnController;
        _prevSpawn = spawn?.BotsAddedTotal ?? 0;
        _prevDespawn = spawn?.BotsRemovedTotal ?? 0;

        BotGameObjectPool pool = BotGameObjectPool.Instance;
        _prevHit = pool?.PoolHitCount ?? 0;
        _prevMiss = pool?.PoolMissCount ?? 0;
        _prevReturn = pool?.PoolReturnCount ?? 0;
        _prevReject = pool?.PoolReturnRejectedCount ?? 0;
        _prevSmartDematApplied = SmartDematTelemetry.SmartDematApplied;
        _prevSmartRematLos = SmartDematTelemetry.SmartRematLos;
        _prevSmartRematNear = SmartDematTelemetry.SmartRematNear;
        _prevAutoSpawnAttempts = SmartDematTelemetry.AutoSpawnAttempts;
        _prevAutoSpawnFailures = SmartDematTelemetry.AutoSpawnFailures;
        _prevAutoMatApplied = SmartDematTelemetry.AutoMatApplied;
        _prevGc = GC.GetTotalMemory(false);
    }

    public void Tick()
    {
        if (_writer == null)
        {
            return;
        }

        float frameMs = Time.unscaledDeltaTime * 1000f;
        long gcNow = GC.GetTotalMemory(false);
        double gcDeltaKb = (gcNow - _prevGc) / 1024.0;
        _prevGc = gcNow;

        AIFrameBudgetScheduler scheduler = AIFrameBudgetScheduler.Instance;
        int totalOnline = scheduler?.TotalOnlineBots ?? 0;
        int pooled = BotGameObjectPool.Instance?.TotalPooledCount ?? 0;

        BotSpawnController spawn = BotManagerComponent.Instance?.BotSpawnController;
        BotGameObjectPool pool = BotGameObjectPool.Instance;

        void Emit(string ev, int poolHitFlag)
        {
            float elapsed = Mathf.Max(0f, Time.time - _raidStartTime);
            string botType = "?";
            _writer.WriteLine(
                $"{DateTime.UtcNow:O},{elapsed:F3},{CsvEscape(ev)},{CsvEscape(botType)},{frameMs:F2},{gcDeltaKb:F2},{poolHitFlag},{totalOnline},{pooled},{CsvEscape(_sessionToken)}"
            );
        }

        if (spawn != null)
        {
            long da = spawn.BotsAddedTotal - _prevSpawn;
            for (long i = 0; i < da; i++)
            {
                Emit("Spawn", 0);
            }

            _prevSpawn = spawn.BotsAddedTotal;

            long dd = spawn.BotsRemovedTotal - _prevDespawn;
            for (long i = 0; i < dd; i++)
            {
                Emit("Despawn", 0);
            }

            _prevDespawn = spawn.BotsRemovedTotal;
        }

        if (pool != null)
        {
            long dh = pool.PoolHitCount - _prevHit;
            for (long i = 0; i < dh; i++)
            {
                Emit("PoolHit", 1);
            }

            _prevHit = pool.PoolHitCount;

            long dm = pool.PoolMissCount - _prevMiss;
            for (long i = 0; i < dm; i++)
            {
                Emit("PoolMiss", 0);
            }

            _prevMiss = pool.PoolMissCount;

            long dr = pool.PoolReturnCount - _prevReturn;
            for (long i = 0; i < dr; i++)
            {
                Emit("PoolReturn", 0);
            }

            _prevReturn = pool.PoolReturnCount;

            long dj = pool.PoolReturnRejectedCount - _prevReject;
            for (long i = 0; i < dj; i++)
            {
                Emit("PoolReturnRejected", 0);
            }

            _prevReject = pool.PoolReturnRejectedCount;
        }

        EmitSmartCapped("SmartDematApplied", SmartDematTelemetry.SmartDematApplied, ref _prevSmartDematApplied);
        EmitSmartCapped("SmartRematLos", SmartDematTelemetry.SmartRematLos, ref _prevSmartRematLos);
        EmitSmartCapped("SmartRematNear", SmartDematTelemetry.SmartRematNear, ref _prevSmartRematNear);
        EmitSmartCapped("AutoSpawnAttempt", SmartDematTelemetry.AutoSpawnAttempts, ref _prevAutoSpawnAttempts);
        EmitSmartCapped("AutoSpawnFailCap", SmartDematTelemetry.AutoSpawnFailures, ref _prevAutoSpawnFailures);
        EmitSmartCapped("AutoMat", SmartDematTelemetry.AutoMatApplied, ref _prevAutoMatApplied);

        _writer.Flush();
    }

    private void EmitSmartCapped(string eventName, long nowTotal, ref long prevTotal)
    {
        long d = nowTotal - prevTotal;
        prevTotal = nowTotal;
        if (d <= 0 || _writer == null)
        {
            return;
        }

        const int maxRows = 32;
        long n = Math.Min(d, maxRows);
        float frameMs = Time.unscaledDeltaTime * 1000f;
        float elapsed = Mathf.Max(0f, Time.time - _raidStartTime);
        AIFrameBudgetScheduler scheduler = AIFrameBudgetScheduler.Instance;
        int totalOnline = scheduler?.TotalOnlineBots ?? 0;
        int pooled = BotGameObjectPool.Instance?.TotalPooledCount ?? 0;

        for (long i = 0; i < n; i++)
        {
            _writer.WriteLine(
                $"{DateTime.UtcNow:O},{elapsed:F3},{CsvEscape(eventName)},{CsvEscape(eventName)},{frameMs:F2},{0:F2},0,{totalOnline},{pooled},{CsvEscape(_sessionToken)}"
            );
        }
    }

    private static string CsvEscape(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "\"\"";
        }

        if (s.IndexOfAny(new[] { '"', ',', '\r', '\n' }) >= 0)
        {
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        return s;
    }

    public void Close()
    {
        if (_writer != null)
        {
            try
            {
                _writer.Flush();
            }
            catch
            {
                // ignore
            }

            _writer.Dispose();
            _writer = null;
        }

        ActivePath = string.Empty;
    }
}
