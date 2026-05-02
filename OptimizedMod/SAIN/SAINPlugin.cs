global using EFTMath = GClass856;
using BepInEx;
using BepInEx.Configuration;
using SAIN.Editor;
using SAIN.Helpers;
using SAIN.Plugin;
using SAIN.Preset;
using SAIN.Preset.GlobalSettings;
using SPT.Reflection.Patching;
using UnityEngine;
using static SAIN.AssemblyInfoClass;

namespace SAIN;

[BepInPlugin(SAINGUID, SAINName, SAINVersion)]
[BepInDependency(BigBrainGUID, BigBrainVersion)]
//[BepInDependency(SPTGUID, SPTVersion)]
[BepInProcess(EscapeFromTarkov)]
[BepInIncompatibility("com.dvize.BushNoESP")]
[BepInIncompatibility("com.dvize.NoGrenadeESP")]
public class SAINPlugin : BaseUnityPlugin
{
    private PatchManager _patchManager;

    public static DebugSettings DebugSettings
    {
        get { return LoadedPreset.GlobalSettings.General.Debug; }
    }

    public static bool DebugMode
    {
        get { return DebugSettings.Logs.GlobalDebugMode; }
    }

    public static bool ProfilingMode
    {
        get { return DebugSettings.Logs.GlobalProfilingToggle; }
    }

    public static bool DrawDebugGizmos
    {
        get { return DebugSettings.Gizmos.DrawDebugGizmos; }
    }

    public static PresetEditorDefaults EditorDefaults
    {
        get { return PresetHandler.EditorDefaults; }
    }

    public static ECombatDecision ForceSoloDecision = ECombatDecision.None;

    public static ESquadDecision ForceSquadDecision = ESquadDecision.None;

    public static ESelfActionType ForceSelfDecision = ESelfActionType.None;

    public void Awake()
    {
        _patchManager = new(this, true);

        PresetHandler.Init();
        BindConfigs();
        _patchManager.EnablePatches();

        // Phase 4: Initialize bot GameObject pool system
        new Components.BotGameObjectPool();

        BigBrainHandler.Init();
    }

    private void BindConfigs()
    {
        string category = "SAIN Editor";
        OpenEditorButton = Config.Bind(category, "Open Editor", false, "Opens the Editor on press");
        OpenEditorConfigEntry = Config.Bind(
            category,
            "Open Editor Shortcut",
            new KeyboardShortcut(KeyCode.F6),
            "The keyboard shortcut that toggles editor"
        );

        // ── Performance Monitor (F12 → SAIN → Performance) ─────────────
        string perfCat = "SAIN Performance";
        var readOnly = new ConfigurationManagerAttributes { ReadOnly = true };
        var isAdvanced = new ConfigurationManagerAttributes { IsAdvanced = true };

        PerfMonEnabled = Config.Bind(perfCat, "1. Monitor Enabled", true,
            "Master toggle for the SAIN performance monitor. Disable to stop all logging.");
        PerfMonLogInterval = Config.Bind(perfCat, "2. Log Interval (sec)", 5f,
            new ConfigDescription("How often to write stats to CSV and BepInEx log.",
                new AcceptableValueRange<float>(1f, 60f)));
        PerfMonLogToCSV = Config.Bind(perfCat, "3. CSV Logging", true,
            "Write structured performance data to BepInEx/LogOutput/sain_perf.csv");
        PerfMonVerbose = Config.Bind(perfCat, "4. Verbose BepInEx Log", false,
            "Also log performance summaries to BepInEx console each interval.");

        // Dump Now button — toggle true to dump, monitor resets it
        PerfMonDumpNow = Config.Bind(perfCat, "Dump Stats Now", false,
            new ConfigDescription("Toggle ON to dump a full performance snapshot to BepInEx log.",
                null, new ConfigurationManagerAttributes { IsAdvanced = false }));

        // Read-only status displays (updated every frame via Update loop below)
        PerfMonFPS = Config.Bind(perfCat, "─ FPS / Frame Time", "--",
            new ConfigDescription("Current FPS and frame time (read-only).", null, readOnly));
        PerfMonBudget = Config.Bind(perfCat, "─ AI Budget", "--",
            new ConfigDescription("Budget used/max, utilization %, exhaustion rate (read-only).", null, readOnly));
        PerfMonBots = Config.Bind(perfCat, "─ Bot Distribution", "--",
            new ConfigDescription("Visible / Audible / Occluded / Offline bot counts (read-only).", null, readOnly));
        PerfMonCSVPath = Config.Bind(perfCat, "─ CSV Log File", "--",
            new ConfigDescription("Path to the CSV performance log (read-only).", null, readOnly));
    }

    // ── F12 Performance Monitor Config ───────────────────────────────────
    public static ConfigEntry<bool> PerfMonEnabled { get; private set; }
    public static ConfigEntry<float> PerfMonLogInterval { get; private set; }
    public static ConfigEntry<bool> PerfMonLogToCSV { get; private set; }
    public static ConfigEntry<bool> PerfMonVerbose { get; private set; }
    public static ConfigEntry<bool> PerfMonDumpNow { get; private set; }
    // Read-only status displays
    public static ConfigEntry<string> PerfMonFPS { get; private set; }
    public static ConfigEntry<string> PerfMonBudget { get; private set; }
    public static ConfigEntry<string> PerfMonBots { get; private set; }
    public static ConfigEntry<string> PerfMonCSVPath { get; private set; }

    public static ConfigEntry<bool> OpenEditorButton { get; private set; }

    public static ConfigEntry<KeyboardShortcut> OpenEditorConfigEntry { get; private set; }

    public static SAINPresetClass LoadedPreset
    {
        get { return PresetHandler.LoadedPreset; }
    }

    public void Update()
    {
        ModDetection.ManualUpdate();
        SAINEditor.ManualUpdate();
        DebugGizmos.ManualUpdate();

        // Sync F12 config to live PerformanceMonitor
        SyncPerfMonitor();
    }

    public void Start()
    {
        SAINEditor.Init();
    }

    public void LateUpdate()
    {
        SAINEditor.LateUpdate();
    }

    public void OnGUI()
    {
        SAINEditor.OnGUI();
    }

    private static float _perfSyncTimer;
    private static void SyncPerfMonitor()
    {
        var mon = SAINPerformanceMonitor.Instance;
        if (mon == null) return;

        // Push F12 config values into the monitor
        mon.LoggingEnabled = PerfMonEnabled.Value;
        mon.LogIntervalSeconds = PerfMonLogInterval.Value;
        mon.LogToCSV = PerfMonLogToCSV.Value;
        mon.VerboseLogging = PerfMonVerbose.Value;
        mon.DumpStatsRequested = PerfMonDumpNow.Value;

        if (PerfMonDumpNow.Value)
            PerfMonDumpNow.Value = false;

        // Update read-only status displays (throttled to avoid GC alloc)
        _perfSyncTimer += Time.unscaledDeltaTime;
        if (_perfSyncTimer >= 0.5f)
        {
            _perfSyncTimer = 0f;
            PerfMonFPS.Value = $"{mon.CurrentFPS:F0} FPS / {mon.AvgFrameTimeMs:F1}ms avg";
            PerfMonBudget.Value = $"{mon.BudgetUsedMs:F2}/{mon.BudgetLimitMs:F1}ms ({mon.BudgetUtilizationPercent:F0}%) exh={mon.BudgetExhaustedRate:F0}%";
            PerfMonBots.Value = $"V:{mon.VisibleBots} A:{mon.AudibleBots} O:{mon.OccludedBots} Off:{mon.OfflineSquadCount}";
            PerfMonCSVPath.Value = mon.LogToCSV ? "BepInEx/LogOutput/sain_perf.csv" : "(disabled)";
        }
    }
}
