using System;
using System.Collections.Generic;
using System.Reflection;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using SAIN.Layers;
using SAIN.Layers.Combat.Run;
using SAIN.Layers.Combat.Solo;
using SAIN.Layers.Combat.Squad;
using SAIN.Preset.GlobalSettings;
using SAIN.Preset.GlobalSettings.Categories;

namespace SAIN;

public static class BigBrainHandler
{
    public const bool INCLUDE_RAIDER_BRAIN_FOR_PMCS = true;
    private const int AvoidThreatPriority = 80;
    private static bool _priorityValidationLogged;

    private static readonly string[] _commonVanillaLayersToRemove =
    [
        "Help",
        "AdvAssaultTarget",
        "AssaultEnemyFar",
        "Hit",
        "Simple Target",
        "Pmc",
        "AssaultHaveEnemy",
        "Assault Building",
        "Enemy Building",
        "PushAndSup",
        "Pursuit",
        // Stationary / patrol-style vanilla layers (EFT build–dependent names). Extend from [SAIN DIAG] + active layer logs when missing.
        "StationaryWS",
        "StationaryWeapon",
        "PatrolAssault",
        "SimplePatrol",
        // Common non-SAIN takeover layers observed in Lighthouse ExUsec audits.
        // If left active, they can preempt SAIN combat/squad under pressure.
        "PatrolFollower",
        "GroupForce",
        "StandBy",
    ];

    private static readonly List<Type> _SAINLayers = [];
    private static readonly List<string> _SAINLayerNames = [];

    public static List<string> SAINLayerNames
    {
        get { return FindAllSAINLayers(); }
    }

    public static List<Type> SAINLayers
    {
        get
        {
            if (_SAINLayers.Count == 0)
            {
                Type[] allTypes = typeof(SAINPlugin).Assembly.GetTypes();
                for (int i = 0; i < allTypes.Length; i++)
                {
                    Type type = allTypes[i];
                    if (type.IsSubclassOf(typeof(SAINLayer)))
                    {
                        _SAINLayers.Add(type);
                    }
                }
            }

            return _SAINLayers;
        }
    }

    public static void Init()
    {
        BrainAssignment.Init();
    }

    private static List<string> FindAllSAINLayers()
    {
        if (_SAINLayerNames.Count != 0)
        {
            return _SAINLayerNames;
        }

        foreach (Type layerType in SAINLayers)
        {
            FieldInfo nameFieldInfo = layerType.GetField("Name", BindingFlags.Public | BindingFlags.Static);
            if (nameFieldInfo == null)
            {
                Logger.LogError(
                    $"{layerType.Name} does not have a public static Name field. This is required for enabling vanilla layers!"
                );
                continue;
            }

            _SAINLayerNames.Add((string)nameFieldInfo.GetValue(null));
        }

        return _SAINLayerNames;
    }

    public static bool BigBrainInitialized;

    public static class BrainAssignment
    {
        private readonly struct LayerPrioritySet
        {
            public LayerPrioritySet(int squad, int solo, int extract)
            {
                Squad = squad;
                Solo = solo;
                Extract = extract;
            }

            public int Squad { get; }
            public int Solo { get; }
            public int Extract { get; }
        }

        private static LayerPrioritySet GetValidatedLayerPriorities()
        {
            var settings = SAINPlugin.LoadedPreset.GlobalSettings.General.Layers;

            int squad = Math.Clamp(settings.SAINCombatSquadLayerPriority, 0, AvoidThreatPriority - 1);
            int solo = Math.Clamp(settings.SAINCombatSoloLayerPriority, 0, AvoidThreatPriority - 2);
            int extract = Math.Clamp(settings.SAINExtractLayerPriority, 0, AvoidThreatPriority - 3);

            if (solo >= squad)
            {
                solo = Math.Max(0, squad - 1);
            }

            if (extract >= solo)
            {
                extract = Math.Max(0, solo - 1);
            }

            if (!_priorityValidationLogged
                && (squad != settings.SAINCombatSquadLayerPriority
                    || solo != settings.SAINCombatSoloLayerPriority
                    || extract != settings.SAINExtractLayerPriority))
            {
                _priorityValidationLogged = true;
                Logger.LogWarning(
                    $"[SAIN] Adjusted invalid layer priorities from preset: "
                    + $"Squad={settings.SAINCombatSquadLayerPriority}, "
                    + $"Solo={settings.SAINCombatSoloLayerPriority}, "
                    + $"Extract={settings.SAINExtractLayerPriority} "
                    + $"-> Squad={squad}, Solo={solo}, Extract={extract} "
                    + $"(must satisfy AvoidThreat(80) > Squad > Solo > Extract)."
                );
            }

            return new LayerPrioritySet(squad, solo, extract);
        }

        public static void Init()
        {
            AddCustomLayersToPMCs();
            AddCustomLayersToScavs();
            AddCustomLayersToRaiders([WildSpawnType.pmcBot]);
            AddCustomLayersToRogues();
            AddCustomLayersToBloodHounds();
            AddCustomLayersToBosses();
            AddCustomLayersToFollowers();
            AddCustomLayersToGoons();
            AddCustomLayersToOthers();

            ToggleVanillaLayersForPMCs(false);
            ToggleVanillaLayersForOthers(false);
            ToggleVanillaLayersForAllBots();
        }

        public static void ToggleVanillaLayersForAllBots()
        {
            ToggleVanillaLayersForScavs(VanillaBotSettings.VanillaScavs);
            ToggleVanillaLayersForRogues(VanillaBotSettings.VanillaRogues);
            ToggleVanillaLayersForRaiders([WildSpawnType.pmcBot], false); // _vanillaBotSettings.VanillaRaiders);
            ToggleVanillaLayersForBloodHounds(VanillaBotSettings.VanillaBloodHounds);
            ToggleVanillaLayersForBosses(VanillaBotSettings.VanillaBosses);
            ToggleVanillaLayersForFollowers(VanillaBotSettings.VanillaFollowers);
            ToggleVanillaLayersForGoons(VanillaBotSettings.VanillaGoons);
        }

        public static void ToggleVanillaLayersForPMCs(bool useVanillaLayers)
        {
            List<string> brainList = GetBrainList(AIBrains.PMCs);

            List<string> LayersToToggle =
            [
                "Request",
                //"FightReqNull",
                //"PeacecReqNull",
                "KnightFight",
                //"PtrlBirdEye",
                "PmcBear",
                "PmcUsec",
                .. _commonVanillaLayersToRemove,
            ];

            ToggleVanillaLayers(brainList, LayersToToggle, useVanillaLayers);

            if (INCLUDE_RAIDER_BRAIN_FOR_PMCS)
            {
                ToggleVanillaLayersForRaiders([WildSpawnType.pmcBEAR, WildSpawnType.pmcUSEC], useVanillaLayers);
            }
        }

        public static void ToggleVanillaLayersForScavs(bool useVanillaLayers)
        {
            List<string> brainList = GetBrainList(AIBrains.Scavs);

            List<string> LayersToToggle =
            [
                //"FightReqNull",
                //"PeacecReqNull",
                "PmcBear",
                "PmcUsec",
                .. _commonVanillaLayersToRemove,
            ];

            ToggleVanillaLayers(brainList, LayersToToggle, useVanillaLayers);

            ToggleVanillaLayersForRaiders([WildSpawnType.assaultGroup], useVanillaLayers);
        }

        public static void ToggleVanillaLayersForRaiders(List<WildSpawnType> roles, bool useVanillaLayers)
        {
            List<string> brainList = [nameof(EBrain.PMC)];

            List<string> LayersToToggle =
            [
                "Request",
                //"FightReqNull",
                //"PeacecReqNull",
                "KnightFight",
                //"PtrlBirdEye",
                "PmcBear",
                "PmcUsec",
                .. _commonVanillaLayersToRemove,
            ];

            ToggleVanillaLayers(brainList, LayersToToggle, roles, useVanillaLayers);
        }

        public static void ToggleVanillaLayersForOthers(bool useVanillaLayers)
        {
            List<string> brainList = GetBrainList(AIBrains.Others);

            List<string> LayersToToggle = ["Request", "KnightFight", "PmcBear", "PmcUsec", .. _commonVanillaLayersToRemove];

            ToggleVanillaLayers(brainList, LayersToToggle, useVanillaLayers);
        }

        public static void ToggleVanillaLayersForRogues(bool useVanillaLayers)
        {
            List<string> brainList = [nameof(EBrain.ExUsec)];

            List<string> LayersToToggle =
            [
                "Request",
                //"FightReqNull",
                //"PeacecReqNull",
                "KnightFight",
                //"PtrlBirdEye",
                "PmcBear",
                "PmcUsec",
                .. _commonVanillaLayersToRemove,
            ];

            ToggleVanillaLayers(brainList, LayersToToggle, useVanillaLayers);
        }

        public static void ToggleVanillaLayersForBloodHounds(bool useVanillaLayers)
        {
            List<string> brainList = [nameof(EBrain.ArenaFighter)];

            List<string> LayersToToggle =
            [
                "Request",
                //"FightReqNull",
                //"PeacecReqNull",
                "KnightFight",
                //"PtrlBirdEye",
                "PmcBear",
                "PmcUsec",
                .. _commonVanillaLayersToRemove,
            ];

            ToggleVanillaLayers(brainList, LayersToToggle, useVanillaLayers);
        }

        public static void ToggleVanillaLayersForBosses(bool useVanillaLayers)
        {
            List<string> brainList = GetBrainList(AIBrains.Bosses);

            List<string> LayersToToggle =
            [
                "KnightFight",
                "BirdEyeFight",
                "BossBoarFight",
                "BossGlFight",
                "PrtFight",
                "KojaniyB_Enemy",
                "Bully Layer",
                "KlnSolo",
                "KolontayFight",
                "KlnTrg",
                "BossSanitarFight",
                .. _commonVanillaLayersToRemove,
            ];
            ToggleVanillaLayers(brainList, LayersToToggle, useVanillaLayers);
        }

        public static void ToggleVanillaLayersForFollowers(bool useVanillaLayers)
        {
            List<string> brainList = GetBrainList(AIBrains.Followers);

            List<string> LayersToToggle =
            [
                "KnightFight",
                "BoarGrenadeDanger",
                "FBoarFght",
                "SecurityKln",
                "Kln_NIMH",
                "FolKojEnemy",
                "KlnForceAtk",
                "KolontayAP",
                "GluhAssKilla",
                "KlnTrg",
                "FlSanFight",
                .. _commonVanillaLayersToRemove,
            ];

            ToggleVanillaLayers(brainList, LayersToToggle, useVanillaLayers);
        }

        public static void ToggleVanillaLayersForGoons(bool useVanillaLayers)
        {
            List<string> brainList = GetBrainList(AIBrains.Goons);

            List<string> LayersToToggle =
            [
                //"FightReqNull",
                //"PeacecReqNull",
                "KnightFight",
                "BirdEyeFight",
                "Kill logic",
                .. _commonVanillaLayersToRemove,
            ];

            ToggleVanillaLayers(brainList, LayersToToggle, useVanillaLayers);
        }

        public static void ToggleVanillaLayersForBrains(List<string> brainList, List<string> layersToToggle, bool useVanillaLayers)
        {
            ToggleVanillaLayers(brainList, layersToToggle, useVanillaLayers);
        }

        public static void ToggleVanillaLayersForBrainsAndRoles(List<string> brainList, List<WildSpawnType> roles, List<string> layersToToggle, bool useVanillaLayers)
        {
            ToggleVanillaLayers(brainList, layersToToggle, roles, useVanillaLayers);
        }

        public static void AddCustomLayersToBrains(List<string> brainList, bool withExtract)
        {
            LayerPrioritySet priorities = GetValidatedLayerPriorities();

            BrainManager.AddCustomLayer(typeof(DebugLayer), brainList, 99);
            BrainManager.AddCustomLayer(typeof(SAINAvoidThreatLayer), brainList, AvoidThreatPriority);
            BrainManager.AddCustomLayer(typeof(CombatSquadLayer), brainList, priorities.Squad);
            BrainManager.AddCustomLayer(typeof(CombatSoloLayer), brainList, priorities.Solo);

            if (withExtract)
            {
                BrainManager.AddCustomLayer(typeof(ExtractLayer), brainList, priorities.Extract);
            }
        }

        public static void AddCustomLayersToBrainsAndRoles(List<string> brainList, List<WildSpawnType> roles, bool withExtract)
        {
            LayerPrioritySet priorities = GetValidatedLayerPriorities();

            BrainManager.AddCustomLayer(typeof(DebugLayer), brainList, 99, roles);
            BrainManager.AddCustomLayer(typeof(SAINAvoidThreatLayer), brainList, AvoidThreatPriority, roles);
            BrainManager.AddCustomLayer(typeof(CombatSquadLayer), brainList, priorities.Squad, roles);
            BrainManager.AddCustomLayer(typeof(CombatSoloLayer), brainList, priorities.Solo, roles);

            if (withExtract)
            {
                BrainManager.AddCustomLayer(typeof(ExtractLayer), brainList, priorities.Extract, roles);
            }
        }

        private static void ToggleVanillaLayers(List<string> brainNames, List<string> layerNames, bool useVanillaLayers)
        {
            if (useVanillaLayers)
            {
                BrainManager.RemoveLayers(SAINLayerNames, brainNames);
                BrainManager.RestoreLayers(layerNames, brainNames);
            }
            else
            {
                CheckExtractEnabled(layerNames);

                BrainManager.RestoreLayers(SAINLayerNames, brainNames);
                BrainManager.RemoveLayers(layerNames, brainNames);
            }
        }

        private static void ToggleVanillaLayers(
            List<string> brainNames,
            List<string> layerNames,
            List<WildSpawnType> roles,
            bool useVanillaLayers
        )
        {
            if (useVanillaLayers)
            {
                BrainManager.RemoveLayers(SAINLayerNames, brainNames, roles);
                BrainManager.RestoreLayers(layerNames, brainNames, roles);
            }
            else
            {
                CheckExtractEnabled(layerNames);

                BrainManager.RestoreLayers(SAINLayerNames, brainNames, roles);
                BrainManager.RemoveLayers(layerNames, brainNames, roles);
            }
        }

        private static void AddCustomLayersToPMCs()
        {
            List<string> pmcBrain = GetBrainList(AIBrains.PMCs);
            LayerPrioritySet priorities = GetValidatedLayerPriorities();

            BrainManager.AddCustomLayer(typeof(DebugLayer), pmcBrain, 99);
            BrainManager.AddCustomLayer(typeof(SAINAvoidThreatLayer), pmcBrain, AvoidThreatPriority);
            BrainManager.AddCustomLayer(typeof(CombatSquadLayer), pmcBrain, priorities.Squad);
            BrainManager.AddCustomLayer(typeof(CombatSoloLayer), pmcBrain, priorities.Solo);
            BrainManager.AddCustomLayer(typeof(ExtractLayer), pmcBrain, priorities.Extract);

            if (INCLUDE_RAIDER_BRAIN_FOR_PMCS)
            {
                AddCustomLayersToRaiders([WildSpawnType.pmcBEAR, WildSpawnType.pmcUSEC]);
            }
        }

        private static void AddCustomLayersToScavs()
        {
            List<string> brainList = GetBrainList(AIBrains.Scavs);
            LayerPrioritySet priorities = GetValidatedLayerPriorities();

            //BrainManager.AddCustomLayer(typeof(BotUnstuckLayer), stringList, 98);
            BrainManager.AddCustomLayer(typeof(DebugLayer), brainList, 99);
            BrainManager.AddCustomLayer(typeof(SAINAvoidThreatLayer), brainList, AvoidThreatPriority);
            BrainManager.AddCustomLayer(typeof(CombatSquadLayer), brainList, priorities.Squad);
            BrainManager.AddCustomLayer(typeof(CombatSoloLayer), brainList, priorities.Solo);
            BrainManager.AddCustomLayer(typeof(ExtractLayer), brainList, priorities.Extract);

            AddCustomLayersToRaiders([WildSpawnType.assaultGroup]);
        }

        private static void AddCustomLayersToRaiders(List<WildSpawnType> roles)
        {
            LayerPrioritySet priorities = GetValidatedLayerPriorities();
            List<string> raiderBrain = [nameof(EBrain.PMC)];

            BrainManager.AddCustomLayer(typeof(DebugLayer), raiderBrain, 99, roles);
            BrainManager.AddCustomLayer(typeof(SAINAvoidThreatLayer), raiderBrain, AvoidThreatPriority, roles);
            BrainManager.AddCustomLayer(typeof(CombatSquadLayer), raiderBrain, priorities.Squad, roles);
            BrainManager.AddCustomLayer(typeof(CombatSoloLayer), raiderBrain, priorities.Solo, roles);
            BrainManager.AddCustomLayer(typeof(ExtractLayer), raiderBrain, priorities.Extract, roles);
        }

        private static void AddCustomLayersToOthers()
        {
            List<string> brainList = GetBrainList(AIBrains.Others);

            LayerPrioritySet priorities = GetValidatedLayerPriorities();
            //BrainManager.AddCustomLayer(typeof(BotUnstuckLayer), stringList, 98);
            BrainManager.AddCustomLayer(typeof(DebugLayer), brainList, 99);
            BrainManager.AddCustomLayer(typeof(SAINAvoidThreatLayer), brainList, AvoidThreatPriority);
            BrainManager.AddCustomLayer(typeof(CombatSquadLayer), brainList, priorities.Squad);
            BrainManager.AddCustomLayer(typeof(CombatSoloLayer), brainList, priorities.Solo);
            BrainManager.AddCustomLayer(typeof(ExtractLayer), brainList, priorities.Extract);
        }

        private static void AddCustomLayersToRogues()
        {
            List<string> brainList = [nameof(EBrain.ExUsec)];

            LayerPrioritySet priorities = GetValidatedLayerPriorities();
            //BrainManager.AddCustomLayer(typeof(BotUnstuckLayer), stringList, 98);
            BrainManager.AddCustomLayer(typeof(DebugLayer), brainList, 99);
            BrainManager.AddCustomLayer(typeof(SAINAvoidThreatLayer), brainList, AvoidThreatPriority);
            BrainManager.AddCustomLayer(typeof(CombatSquadLayer), brainList, priorities.Squad);
            BrainManager.AddCustomLayer(typeof(CombatSoloLayer), brainList, priorities.Solo);
            BrainManager.AddCustomLayer(typeof(ExtractLayer), brainList, priorities.Extract);
        }

        private static void AddCustomLayersToBloodHounds()
        {
            List<string> brainList = [nameof(EBrain.ArenaFighter)];

            LayerPrioritySet priorities = GetValidatedLayerPriorities();
            //BrainManager.AddCustomLayer(typeof(BotUnstuckLayer), stringList, 98);
            BrainManager.AddCustomLayer(typeof(DebugLayer), brainList, 99);
            BrainManager.AddCustomLayer(typeof(SAINAvoidThreatLayer), brainList, AvoidThreatPriority);
            BrainManager.AddCustomLayer(typeof(CombatSquadLayer), brainList, priorities.Squad);
            BrainManager.AddCustomLayer(typeof(CombatSoloLayer), brainList, priorities.Solo);
            BrainManager.AddCustomLayer(typeof(ExtractLayer), brainList, priorities.Extract);
        }

        private static void AddCustomLayersToBosses()
        {
            List<string> brainList = GetBrainList(AIBrains.Bosses);
            LayerPrioritySet priorities = GetValidatedLayerPriorities();

            BrainManager.AddCustomLayer(typeof(DebugLayer), brainList, 99);
            BrainManager.AddCustomLayer(typeof(SAINAvoidThreatLayer), brainList, AvoidThreatPriority);
            BrainManager.AddCustomLayer(typeof(CombatSquadLayer), brainList, priorities.Squad);
            BrainManager.AddCustomLayer(typeof(CombatSoloLayer), brainList, priorities.Solo);
        }

        private static void AddCustomLayersToFollowers()
        {
            List<string> brainList = GetBrainList(AIBrains.Followers);
            LayerPrioritySet priorities = GetValidatedLayerPriorities();

            BrainManager.AddCustomLayer(typeof(DebugLayer), brainList, 99);
            BrainManager.AddCustomLayer(typeof(SAINAvoidThreatLayer), brainList, AvoidThreatPriority);
            BrainManager.AddCustomLayer(typeof(CombatSquadLayer), brainList, priorities.Squad);
            BrainManager.AddCustomLayer(typeof(CombatSoloLayer), brainList, priorities.Solo);
        }

        private static void AddCustomLayersToGoons()
        {
            List<string> brainList = GetBrainList(AIBrains.Goons);
            LayerPrioritySet priorities = GetValidatedLayerPriorities();

            BrainManager.AddCustomLayer(typeof(DebugLayer), brainList, 99);
            BrainManager.AddCustomLayer(typeof(SAINAvoidThreatLayer), brainList, AvoidThreatPriority);
            BrainManager.AddCustomLayer(typeof(CombatSquadLayer), brainList, priorities.Squad);
            BrainManager.AddCustomLayer(typeof(CombatSoloLayer), brainList, priorities.Solo);
        }

        private static void CheckExtractEnabled(List<string> layersToRemove)
        {
            if (GlobalSettingsClass.Instance.General.Extract.SAIN_EXTRACT_TOGGLE)
            {
                layersToRemove.Add("Exfiltration");
            }
        }

        private static List<string> GetBrainList(List<EBrain> brains)
        {
            List<string> brainList = [];
            for (int i = 0; i < brains.Count; i++)
            {
                brainList.Add(brains[i].ToString());
            }
            return brainList;
        }

        private static VanillaBotSettings VanillaBotSettings
        {
            get { return SAINPlugin.LoadedPreset.GlobalSettings.General.VanillaBots; }
        }
    }
}
