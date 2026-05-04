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
        EnsureForkOptimizedPreset();
        BindConfigs();
        _patchManager.EnablePatches();

        // Phase 4: Initialize bot GameObject pool system
        new Components.BotGameObjectPool();

        BigBrainHandler.Init();
    }

    /// <summary>
    /// Fork bootstrap preset: harder PMC baseline (see <see cref="SAINDifficulty.harderpmcs"/>), tuned for
    /// player-centric LOD + frame budget (INDEX.md, docs/AI_BUDGET_LOD_PLAN.md). See docs/SAIN_FORK_PRESET.md.
    /// </summary>
    public const string ForkOptimizedPresetName = "Optimized (Harder PMCs)";

    private static void EnsureForkOptimizedPreset()
    {
        bool created = false;
        if (!PresetHandler.CustomPresetOptions.Exists(p => p.Name == ForkOptimizedPresetName))
        {
            created = true;
            var baseDef = SAINDifficultyClass.DefaultPresetDefinitions[SAINDifficulty.harderpmcs];
            var newDef = baseDef.Clone();
            newDef.Name = ForkOptimizedPresetName;
            newDef.Description =
                "Fork preset: Harder PMCs baseline, player-centric performance (LOD/budget), moderate hearing trim.";
            newDef.Creator = "spt-tarkov-ai";

            PresetHandler.SavePresetDefinition(newDef);
            PresetHandler.InitPresetFromDefinition(newDef, true, exportEditorDefaults: false);
            ApplyForkOptimizedTuning(PresetHandler.LoadedPreset);
            SAINPresetClass.ExportAll(PresetHandler.LoadedPreset);
        }

        if (!PresetHandler.EditorDefaultsLoadedFromDisk)
        {
            if (PresetHandler.LoadPresetDefinition(ForkOptimizedPresetName, out var forkDef))
            {
                if (!created)
                {
                    PresetHandler.InitPresetFromDefinition(forkDef);
                }
                else
                {
                    PresetHandler.EditorDefaults.SelectedDefaultPreset = SAINDifficulty.none;
                    PresetHandler.ExportEditorDefaults();
                }
            }
        }
        else if (created)
        {
            PresetHandler.InitPresetFromDefinition(null);
        }
    }

    private static void ApplyForkOptimizedTuning(SAINPresetClass preset)
    {
        var global = preset.GlobalSettings;
        global.General.Performance.PerformanceMode = true;
        global.General.Performance.MaxAiBudgetMilliseconds = Mathf.Clamp(3f, 1f, 10f);
        global.Difficulty.HearingDistanceCoef = 0.85f;
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
    }

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

}
