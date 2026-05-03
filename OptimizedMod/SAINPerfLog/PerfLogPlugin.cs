using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using EFT;
using SAIN;
using SAIN.Components;
using SAINPerfLog.Components;
using UnityEngine;

namespace SAINPerfLog;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("me.sol.sain", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("xyz.drakia.bigbrain", BepInDependency.DependencyFlags.SoftDependency)]
[BepInProcess("EscapeFromTarkov.exe")]
public class PerfLogPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "me.sol.sain.perflog";
    public const string PluginName = "SAINPerfLog";
    public const string PluginVersion = "1.0.0";

    /// <summary>Reflection hook for SAIN — must remain a public static bool field named exactly this.</summary>
    public static bool DiagnosticLoggingEnabled;

    /// <summary>Reflection hook for SAIN — public static bool, field name must match <see cref="SainPerfLogInterop"/>.</summary>
    public static bool BigBrainDiagVerboseSampling;

    internal static PerfLogPlugin Instance { get; private set; }

    internal static ConfigEntry<bool> EnableAutoLogging { get; private set; }
    internal static ConfigEntry<float> PerfLogIntervalSec { get; private set; }
    internal static ConfigEntry<bool> EnableBigBrainSnapshots { get; private set; }
    internal static ConfigEntry<float> BigBrainSnapshotIntervalSec { get; private set; }
    internal static ConfigEntry<bool> CoalesceIdleSnapshots { get; private set; }
    internal static ConfigEntry<float> SnapshotHeartbeatMinutes { get; private set; }
    internal static ConfigEntry<bool> WriteLatestAlias { get; private set; }
    internal static ConfigEntry<string> OutputDirectoryRelative { get; private set; }

    private static ConfigEntry<bool> F12StatusEnabled { get; set; }
    private static ConfigEntry<bool> DiagnosticLogging { get; set; }
    private static ConfigEntry<bool> BigBrainDiagVerboseSamplingCfg { get; set; }
    private static ConfigEntry<string> F12FpsLine { get; set; }
    private static ConfigEntry<string> F12BudgetLine { get; set; }
    private static ConfigEntry<string> F12BotsLine { get; set; }
    private static ConfigEntry<string> F12CsvLine { get; set; }

    private Harmony _harmony;

    private static float _f12SyncTimer;
    private static readonly RollingAverage _frameTimeAverage = new(120);
    private static int _schedulerFrames;
    private static int _schedulerExhaustedFrames;

    private void Awake()
    {
        Instance = this;
        BindConfig();
        BindF12Readouts();

        _harmony = new Harmony($"{PluginGuid}.harmony");
        _harmony.PatchAll(typeof(PerfLogPlugin).Assembly);

        Logger.LogInfo($"{PluginName} initialized");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }

    private void Update()
    {
        DiagnosticLoggingEnabled = DiagnosticLogging != null && DiagnosticLogging.Value;
        BigBrainDiagVerboseSampling = BigBrainDiagVerboseSamplingCfg != null && BigBrainDiagVerboseSamplingCfg.Value;
        SyncF12StatusReadouts();
    }

    private void BindConfig()
    {
        const string category = "SAINPerfLog";
        EnableAutoLogging = Config.Bind(category, "Enable Auto Logging", true, "Enable per-raid SAIN performance CSV logging.");
        PerfLogIntervalSec = Config.Bind(
            category,
            "Perf CSV Interval (sec)",
            5f,
            new ConfigDescription("How often to append one row to the per-raid perf CSV.", new AcceptableValueRange<float>(1f, 60f))
        );
        EnableBigBrainSnapshots = Config.Bind(category, "Enable BigBrain Snapshots", true, "Write sparse aggregate BigBrain layer snapshots.");
        BigBrainSnapshotIntervalSec = Config.Bind(
            category,
            "BigBrain Snapshot Interval (sec)",
            30f,
            new ConfigDescription("How often to sample aggregate BigBrain layer distribution.", new AcceptableValueRange<float>(5f, 300f))
        );
        CoalesceIdleSnapshots = Config.Bind(category, "Coalesce Idle Snapshots", true, "Skip unchanged idle snapshots where mismatch count is zero.");
        SnapshotHeartbeatMinutes = Config.Bind(
            category,
            "Snapshot Heartbeat (min)",
            5f,
            new ConfigDescription("Force one snapshot at this interval even if unchanged.", new AcceptableValueRange<float>(1f, 30f))
        );
        WriteLatestAlias = Config.Bind(category, "Write latest aliases", false, "Also update sain_perf_latest.csv and sain_bigbrain_latest.csv");
        OutputDirectoryRelative = Config.Bind(category, "Output Directory", @"LogOutput\sain_perf", "Path relative to BepInEx root for output files.");
    }

    private void BindF12Readouts()
    {
        const string cat = "SAINPerfLog (F12)";
        var readOnly = new ConfigurationManagerAttributes { ReadOnly = true };

        F12StatusEnabled = Config.Bind(cat, "1. F12 Status Lines", true, "Show live FPS / scheduler / bot stats in Configuration Manager (F12).");
        DiagnosticLogging = Config.Bind(
            cat,
            "2. Diagnostic Logging",
            false,
            "Spammy: tier changes, budget exhaustion, offline combat events to BepInEx. Use when debugging AI behavior.");
        BigBrainDiagVerboseSamplingCfg = Config.Bind(
            cat,
            "3. BigBrain verbose sample",
            false,
            "When Diagnostic Logging is on: log active BigBrain layer for every human-proximate SAIN bot on the diag interval (not only mismatches).");

        F12FpsLine = Config.Bind(cat, "─ FPS / Frame Time", "--", new ConfigDescription("Rolling average (read-only).", null, readOnly));
        F12BudgetLine = Config.Bind(cat, "─ AI Budget", "--", new ConfigDescription("Scheduler budget (read-only).", null, readOnly));
        F12BotsLine = Config.Bind(cat, "─ Bot / Tier", "--", new ConfigDescription("Processed / skipped / perception counts (read-only).", null, readOnly));
        F12CsvLine = Config.Bind(cat, "─ Active CSV Paths", "--", new ConfigDescription("Current raid perf / snapshot files (read-only).", null, readOnly));
    }

    internal string GetOutputDirectory()
    {
        string relative = OutputDirectoryRelative.Value ?? @"LogOutput\sain_perf";
        string full = Path.Combine(BepInEx.Paths.BepInExRootPath, relative);
        Directory.CreateDirectory(full);
        return full;
    }

    private static void SyncF12StatusReadouts()
    {
        float frameMs = Time.unscaledDeltaTime * 1000f;
        _frameTimeAverage.Add(frameMs);

        _f12SyncTimer += Time.unscaledDeltaTime;
        if (_f12SyncTimer < 0.5f)
        {
            return;
        }

        _f12SyncTimer = 0f;

        if (F12StatusEnabled == null || !F12StatusEnabled.Value)
        {
            F12FpsLine.Value = "(disabled)";
            F12BudgetLine.Value = "(disabled)";
            F12BotsLine.Value = "(disabled)";
            F12CsvLine.Value = "(disabled)";
            return;
        }

        float avgFrameMs = _frameTimeAverage.Value;
        float fps = avgFrameMs > 0f ? 1000f / avgFrameMs : 0f;
        F12FpsLine.Value = $"{fps:F0} FPS / {avgFrameMs:F1}ms avg";

        AIFrameBudgetScheduler scheduler = AIFrameBudgetScheduler.Instance;
        if (scheduler == null)
        {
            F12BudgetLine.Value = "scheduler not initialized";
            F12BotsLine.Value = "scheduler not initialized";
        }
        else
        {
            _schedulerFrames++;
            if (scheduler.BudgetExhaustedLastFrame)
            {
                _schedulerExhaustedFrames++;
            }

            float usedMs = (float)scheduler.BudgetElapsedMs;
            float limitMs = scheduler.MaxAIBudgetMs;
            float util = limitMs > 0f ? usedMs / limitMs * 100f : 0f;
            float headroomMs = Mathf.Max(0f, limitMs - usedMs);
            float exhaustedRate = _schedulerFrames > 0
                ? _schedulerExhaustedFrames / (float)_schedulerFrames * 100f
                : 0f;

            F12BudgetLine.Value =
                $"{usedMs:F2}/{limitMs:F1}ms ({util:F0}%) head:{headroomMs:F2}ms " +
                $"now:{(scheduler.BudgetExhaustedLastFrame ? "Y" : "N")} exh:{exhaustedRate:F0}%";
            F12BotsLine.Value =
                $"Proc:{scheduler.BotsProcessedThisFrame} Skip:{scheduler.BotsSkippedThisFrame} " +
                $"V:{scheduler.VisibleBotsLastFrame} A:{scheduler.AudibleBotsLastFrame} O:{scheduler.OccludedBotsLastFrame} Off:{scheduler.OfflineSquadCount}";
        }

        string perf = string.IsNullOrEmpty(RaidPerfCsvLogger.ActivePerfCsvPath) ? "(no active raid)" : RaidPerfCsvLogger.ActivePerfCsvPath;
        string bb = string.IsNullOrEmpty(RaidPerfCsvLogger.ActiveBigBrainCsvPath) ? "(snapshots off or not started)" : RaidPerfCsvLogger.ActiveBigBrainCsvPath;
        F12CsvLine.Value = $"perf: {perf} | bigbrain: {bb}";
    }

    private sealed class RollingAverage
    {
        private readonly float[] _values;
        private int _index;
        private int _count;

        public RollingAverage(int windowSize)
        {
            _values = new float[Mathf.Max(1, windowSize)];
        }

        public void Add(float value)
        {
            _values[_index] = value;
            _index = (_index + 1) % _values.Length;
            if (_count < _values.Length)
            {
                _count++;
            }
        }

        public float Value
        {
            get
            {
                if (_count == 0)
                {
                    return 0f;
                }

                float sum = 0f;
                for (int i = 0; i < _count; i++)
                {
                    sum += _values[i];
                }

                return sum / _count;
            }
        }
    }
}

[HarmonyAfter("me.sol.sain")]
public static class PerfLogGameWorldPatch
{
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod()
    {
        Type listenerType = AccessTools.TypeByName("GameWorldUnityTickListener");
        return AccessTools.Method(listenerType, "Create");
    }

    [HarmonyPostfix]
    public static void Postfix(GameObject gameObject, EFT.GameWorld gameWorld)
    {
        if (gameWorld is HideoutGameWorld || !PerfLogPlugin.EnableAutoLogging.Value)
        {
            return;
        }

        if (ModDetection.ProjectFikaLoaded && ModDetection.FikaInterop.IsClient())
        {
            return;
        }

        RaidPerfCsvLogger logger = gameObject.GetComponent<RaidPerfCsvLogger>();
        if (logger == null)
        {
            logger = gameObject.AddComponent<RaidPerfCsvLogger>();
        }

        logger.Initialize(gameWorld);
    }
}
