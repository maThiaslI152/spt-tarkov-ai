global using EFTMath = GClass856;
using BepInEx;
using BepInEx.Configuration;
using EFT;
using SAIN.Components;
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
        TryCreateCustomPreset();
        BindConfigs();
        _patchManager.EnablePatches();

        // Phase 4: Initialize bot GameObject pool system
        new Components.BotGameObjectPool();

        BigBrainHandler.Init();
    }

    private static void TryCreateCustomPreset()
    {
        const string presetName = "My Tuned Preset";
        if (PresetHandler.CustomPresetOptions.Exists(p => p.Name == presetName))
        {
            return;
        }

        var baseDef = SAINDifficultyClass.DefaultPresetDefinitions[SAINDifficulty.harderpmcs];
        var newDef = baseDef.Clone();
        newDef.Name = presetName;
        newDef.Description = "Custom preset based on Harder PMCs with veryhard difficulty tuning. Hearing reduced to 50% to prevent wall pre-aiming.";
        newDef.Creator = "user";

        PresetHandler.SavePresetDefinition(newDef);
        PresetHandler.InitPresetFromDefinition(newDef, true);

        var global = PresetHandler.LoadedPreset.GlobalSettings;

        // === Apply veryhard-level global settings ===
        global.Shoot.BOT_RECOIL_COEF = 0.75f;
        global.Difficulty.ScatteringCoef = 0.55f;
        global.Aiming.AimCenterMassGlobal = false;
        global.Difficulty.VisibleDistCoef = 1.25f;
        global.Difficulty.GainSightCoef = 1.25f;
        global.Difficulty.PRECISION_SPEED_COEF = 1.25f;
        global.Difficulty.ACCURACY_SPEED_COEF = 0.6f;

        // === Reduce hearing to prevent wall pre-aiming ===
        global.Difficulty.HearingDistanceCoef = 0.5f;

        // === Apply Harder PMCs bonuses (ApplyHarderPMCs equivalent) ===
        var botSettings = PresetHandler.LoadedPreset.BotSettings;
        foreach (var botsetting in botSettings.SAINSettings)
        {
            if (botsetting.Key == WildSpawnType.pmcUSEC || botsetting.Key == WildSpawnType.pmcBEAR)
            {
                foreach (var diff in botsetting.Value.Settings.Values)
                {
                    diff.Mind.WeaponProficiency = 0.75f;
                    diff.Difficulty.ScatteringCoef = 0.6f;
                    diff.Difficulty.PRECISION_SPEED_COEF = 1.33f;
                    diff.Difficulty.ACCURACY_SPEED_COEF = 0.6f;
                    diff.Difficulty.GainSightCoef = 1.25f;
                    diff.Difficulty.VisibleDistCoef = 1.25f;
                    diff.Difficulty.AggressionCoef = 1.2f;
                    diff.Aiming.AimCenterMass = false;
                }

                var pmcSettings = botsetting.Value.Settings;
                pmcSettings[BotDifficulty.easy].Aiming.MAX_AIM_TIME = 1.5f;
                pmcSettings[BotDifficulty.normal].Aiming.MAX_AIM_TIME = 1.35f;
                pmcSettings[BotDifficulty.hard].Aiming.MAX_AIM_TIME = 1.15f;
                pmcSettings[BotDifficulty.impossible].Aiming.MAX_AIM_TIME = 1.0f;
            }
        }

        // === Apply veryhard-level per-bot overrides ===
        foreach (var botsetting in botSettings.SAINSettings)
        {
            botsetting.Value.DifficultyModifier = Mathf.Clamp(botsetting.Value.DifficultyModifier * 1.33f, 0.01f, 2f);

            foreach (var setting in botsetting.Value.Settings)
            {
                setting.Value.Core.VisibleAngle = 170f;
                setting.Value.Shoot.FireratMulti = 1.5f;
                setting.Value.Shoot.BurstMulti = 2f;
                setting.Value.Aiming.AimCenterMass = false;
            }
        }

        // === Apply veryhard-level strafe speeds ===
        foreach (var botsetting in botSettings.SAINSettings)
        {
            var settings = botsetting.Value.Settings;
            if (botsetting.Key.IsBossOrFollower()
                || botsetting.Key.IsPmcBot()
                || botsetting.Key == WildSpawnType.exUsec
                || botsetting.Key == WildSpawnType.pmcBot
                || botsetting.Key == WildSpawnType.arenaFighter
                || botsetting.Key == WildSpawnType.arenaFighterEvent)
            {
                settings[BotDifficulty.easy].Move.STRAFE_SPEED = 0.75f;
                settings[BotDifficulty.normal].Move.STRAFE_SPEED = 0.85f;
                settings[BotDifficulty.hard].Move.STRAFE_SPEED = 0.9f;
                settings[BotDifficulty.impossible].Move.STRAFE_SPEED = 1.0f;
            }
            else if (botsetting.Key == WildSpawnType.assault || botsetting.Key == WildSpawnType.assaultGroup)
            {
                settings[BotDifficulty.easy].Move.STRAFE_SPEED = 0.65f;
                settings[BotDifficulty.normal].Move.STRAFE_SPEED = 0.7f;
                settings[BotDifficulty.hard].Move.STRAFE_SPEED = 0.75f;
                settings[BotDifficulty.impossible].Move.STRAFE_SPEED = 0.9f;
            }
        }

        // === Extended engagement distances ===
        var engagement = global.Shoot.EngagementDistance;
        engagement[EWeaponClass.Default] = 200f;
        engagement[EWeaponClass.assaultCarbine] = 200f;
        engagement[EWeaponClass.assaultRifle] = 250f;
        engagement[EWeaponClass.machinegun] = 200f;
        engagement[EWeaponClass.smg] = 150f;
        engagement[EWeaponClass.pistol] = 125f;
        engagement[EWeaponClass.marksmanRifle] = 250f;
        engagement[EWeaponClass.sniperRifle] = 250f;
        engagement[EWeaponClass.shotgun] = 125f;
        engagement[EWeaponClass.grenadeLauncher] = 175f;
        engagement[EWeaponClass.specialWeapon] = 175f;

        // === Raider-specific distance multiplier ===
        global.Shoot.RaiderEngagementDistanceMultiplier = 2f;

        SAINPresetClass.ExportAll(PresetHandler.LoadedPreset);
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
        PerfMonDiagnostic = Config.Bind(perfCat, "5. Diagnostic Logging", false,
            "Spammy: log every tier change, budget exhaustion, offline combat event to BepInEx. "
            + "Enable when debugging AI behavior issues.");

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
    public static ConfigEntry<bool> PerfMonDiagnostic { get; private set; }
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
        mon.DiagnosticLogging = PerfMonDiagnostic.Value;
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
