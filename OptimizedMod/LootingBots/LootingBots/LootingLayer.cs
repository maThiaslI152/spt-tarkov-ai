using System.Text;
using System.Reflection;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using LootingBots.Components;
using LootingBots.Logic;
using LootingBots.Utilities;
using UnityEngine;

namespace LootingBots;

internal class LootingLayer : CustomLayer
{
    private readonly LootingBrain _lootingBrain;
    private readonly LootFinder _lootFinder;

    public LootingLayer(BotOwner botOwner, int priority)
        : base(botOwner, priority)
    {
        var lootingBrain = botOwner.GetPlayer.gameObject.AddComponent<LootingBrain>();
        var lootFinder = botOwner.GetPlayer.gameObject.AddComponent<LootFinder>();
        lootingBrain.Init(botOwner);
        lootFinder.Init(botOwner);

        _lootingBrain = lootingBrain;
        _lootFinder = lootFinder;
    }

    public override string GetName()
    {
        return "Looting";
    }

    public override bool IsActive()
    {
        var isBotActive = BotOwner.BotState == EBotState.Active;
        var isNotHealing = !BotOwner.Medecine.FirstAid.Have2Do && !BotOwner.Medecine.SurgicalKit.HaveWork;
        if (IsUnderCombatPressureViaSain(BotOwner))
        {
            // Hard safety gate: if SAIN reports combat pressure, do not allow looting layer to preempt combat.
            return false;
        }
        return isBotActive && isNotHealing && _lootingBrain.IsBrainEnabled && (_lootFinder.IsScheduledScan || _lootingBrain.IsBotLooting);
    }

    public override void Start()
    {
        _lootingBrain.UpdateGridStats();
        BotOwner.PatrollingData.Pause();
        base.Start();
    }

    public override void Stop()
    {
        _lootFinder.StopFindingLoot();
        _lootingBrain.StopLooting();
        _lootingBrain.UpdateGridStats();
        BotOwner.PatrollingData.Unpause();
        base.Stop();
    }

    public override Action GetNextAction()
    {
        if (_lootingBrain.IsBotLooting)
        {
            return new Action(typeof(LootingLogic), "Looting");
        }

        if (_lootFinder.IsScheduledScan)
        {
            return new Action(typeof(FindLootLogic), "Loot Scan");
        }

        return new Action(typeof(PeacefulLogic), "Peaceful");
    }

    public override bool IsCurrentActionEnding()
    {
        var currentActionType = CurrentAction?.Type;

        if (currentActionType == typeof(FindLootLogic))
        {
            return !_lootFinder.IsScanRunning;
        }

        var notLooting = !_lootingBrain.IsBotLooting;

        if (currentActionType == typeof(LootingLogic) && notLooting)
        {
            // Reset scan timer once looting has completed
            _lootFinder.ResetScanTimer();
        }

        return notLooting;
    }

    public override void BuildDebugText(StringBuilder debugPanel)
    {
        var lootName = _lootingBrain.ActiveLoot != null ? _lootingBrain.ActiveLoot.GetLootName() : "-";

        debugPanel.AppendLine(
            _lootingBrain.LootTaskRunning ? "Looting in progress..."
                : _lootFinder.IsScanRunning ? "Scan in progress..."
                : string.Empty,
            Color.green
        );
        debugPanel.AppendLabeledValue(
            "Target Loot",
            $" {lootName} ({_lootingBrain.ActiveLootType.ToString()})",
            Color.yellow,
            Color.yellow
        );

        debugPanel.AppendLabeledValue(
            "Distance to Loot",
            $" {(_lootingBrain.ActiveLootType is LootFinder.LootType.None || _lootingBrain.DistanceToLoot != float.MaxValue ? "Calculating path..." : $"{Mathf.Sqrt(_lootingBrain.DistanceToLoot):0.##}m")}",
            Color.grey,
            Color.grey
        );

        _lootingBrain.Stats.StatsDebugPanel(debugPanel);
    }

    public bool EndLooting()
    {
        return _lootingBrain.ActiveLoot == null;
    }

    private static bool IsUnderCombatPressureViaSain(BotOwner botOwner)
    {
        if (!_sainInteropInitialized)
        {
            _sainInteropInitialized = true;
            _sainExternalType = Type.GetType("SAIN.Interop.SAINExternal, SAIN");
            _sainPressureMethod = _sainExternalType?.GetMethod(
                "IsBotUnderCombatPressure",
                BindingFlags.Public | BindingFlags.Static,
                null,
                [typeof(BotOwner)],
                null);
        }

        if (_sainPressureMethod == null)
        {
            return false;
        }

        try
        {
            return _sainPressureMethod.Invoke(null, [botOwner]) is bool b && b;
        }
        catch
        {
            return false;
        }
    }

    private static bool _sainInteropInitialized;
    private static Type _sainExternalType;
    private static MethodInfo _sainPressureMethod;
}
