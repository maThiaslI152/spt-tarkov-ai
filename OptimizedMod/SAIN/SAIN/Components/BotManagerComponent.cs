using System;
using System.Collections.Generic;
using Comfort.Common;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.EnvironmentEffect;
using SAIN.BotController.Classes;
using SAIN.Components.BotController;
using SAIN.Components.BotControllerSpace.Classes;
using SAIN.Components.PlayerComponentSpace;
using SAIN.Layers.Combat.Solo;
using SAIN.Layers.Combat.Squad;
using SAIN.Plugin;
using SAIN;
using UnityEngine;

namespace SAIN.Components;

public class BotManagerComponent : MonoBehaviour
{
    public static BotManagerComponent Instance { get; private set; }

    public Dictionary<string, BotComponent> Bots
    {
        get { return BotSpawnController.Bots; }
    }

    public GameWorld GameWorld
    {
        get { return SAINGameWorld.GameWorld; }
    }

    public IBotGame BotGame
    {
        get { return Singleton<IBotGame>.Instance; }
    }

    public BotEventHandler BotEventHandler
    {
        get
        {
            if (_eventHandler == null)
            {
                _eventHandler = Singleton<BotEventHandler>.Instance;
                if (_eventHandler != null)
                {
                    GrenadeController.Subscribe(_eventHandler);
                }
            }
            return _eventHandler;
        }
    }

    private BotEventHandler _eventHandler;

    public GameWorldComponent SAINGameWorld { get; private set; }
    public BotsController DefaultController { get; set; }

    public BotSpawner BotSpawner
    {
        get { return _spawner; }
        set
        {
            BotSpawnController.Subscribe(value);
            _spawner = value;
        }
    }

    private BotSpawner _spawner;
    public GrenadeController GrenadeController { get; private set; }
    public BotJobsClass BotJobs { get; private set; }
    public BotExtractManager BotExtractManager { get; private set; }
    public TimeClass TimeVision { get; private set; }
    public SAINWeatherClass WeatherVision { get; private set; }
    public BotSpawnController BotSpawnController { get; private set; }
    public BotSquads BotSquads { get; private set; }
    public BotHearingClass BotHearing { get; private set; }
    public AIFrameBudgetScheduler BudgetScheduler { get; private set; }

    private float _nextBigBrainDiagTime;
    private const float BigBrainDiagIntervalSeconds = 3f;
    private const float HumanProximitySq = 90f * 90f;

    public void PlayerEnviromentChanged(string profileID, IndoorTrigger trigger)
    {
        SAINGameWorld.PlayerTracker.GetPlayerComponent(profileID)?.AIData.PlayerLocation.UpdateEnvironment(trigger);
    }

    public void Activate(GameWorldComponent gameWorldComp)
    {
        Instance = this;
        SAINGameWorld = gameWorldComp;
        BotSpawnController = new BotSpawnController(this);
        BotExtractManager = new BotExtractManager(this);
        TimeVision = new TimeClass(this);
        WeatherVision = new SAINWeatherClass(this);
        BotSquads = new BotSquads(this);
        BotHearing = new BotHearingClass(this);
        BotJobs = new BotJobsClass(this);
        GrenadeController = new GrenadeController(this);
        BudgetScheduler = new AIFrameBudgetScheduler();

        // Initialize performance monitor for F12 stats + CSV logging
        gameObject.GetOrAddComponent<SAINPerformanceMonitor>();
        SyncAiFrameBudgetFromPreset();

        GameWorld.OnDispose += Dispose;
    }

    /// <summary>
    /// Keeps <see cref="AIFrameBudgetScheduler.MaxAIBudgetMs"/> and perf-monitor display in sync with preset (hot-reload safe).
    /// </summary>
    private void SyncAiFrameBudgetFromPreset()
    {
        if (BudgetScheduler == null)
            return;

        float ms = SAINPlugin.LoadedPreset?.GlobalSettings?.General?.Performance?.MaxAiBudgetMilliseconds ?? 2f;
        ms = Mathf.Clamp(ms, 1f, 10f);

        if (Mathf.Abs(BudgetScheduler.MaxAIBudgetMs - ms) > 0.0001f)
            BudgetScheduler.MaxAIBudgetMs = ms;

        var perfMon = SAINPerformanceMonitor.Instance;
        if (perfMon != null && Mathf.Abs(perfMon.BudgetLimitMs - ms) > 0.0001f)
            perfMon.BudgetLimitMs = ms;
    }

    public void ManualUpdate(float currentTime, float deltaTime)
    {
        BotSpawnController.ManualUpdate(currentTime, deltaTime);
        BotExtractManager.Update(currentTime, deltaTime);
        TimeVision.Update(currentTime, deltaTime);
        WeatherVision.Update(currentTime, deltaTime);
        BotSquads.Update(currentTime, deltaTime);

        SyncAiFrameBudgetFromPreset();
        MaybeLogBigBrainArbitrationHints(currentTime);
        BudgetScheduler.ProcessFrame(BotSpawnController.SAINBots, currentTime, deltaTime);
    }

    /// <summary>
    /// Minimal diagnostics for BigBrain priority fights (QuestingBots vs SAIN combat).
    /// Runs only when DiagnosticLogging is enabled on <see cref="SAINPerformanceMonitor"/> and QuestingBots is loaded.
    /// </summary>
    private void MaybeLogBigBrainArbitrationHints(float currentTime)
    {
        var perfMon = SAINPerformanceMonitor.Instance;
        if (perfMon == null || !perfMon.DiagnosticLogging || !ModDetection.QuestingBotsLoaded)
        {
            return;
        }

        if (currentTime < _nextBigBrainDiagTime)
        {
            return;
        }
        _nextBigBrainDiagTime = currentTime + BigBrainDiagIntervalSeconds;

        PlayerSpawnTracker tracker = SAINGameWorld?.PlayerTracker;
        HashSet<PlayerComponent> humans = tracker?.AlivePlayerArray;
        if (humans == null || humans.Count == 0)
        {
            return;
        }

        foreach (BotComponent bot in BotSpawnController.SAINBots)
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

            if (!IsNearAnyHuman(owner.Position, humans))
            {
                continue;
            }

            string layer = BrainManager.GetActiveLayerName(owner);
            if (!LooksLikePriorityMismatch(bot, layer))
            {
                continue;
            }

            string goal =
                bot.GoalEnemy?.EnemyPlayer?.Profile?.Nickname
                ?? bot.GoalEnemy?.EnemyProfileId
                ?? "none";

            Logger.LogWarning(
                $"[SAIN DIAG][BigBrain] {owner.name} layer=\"{layer}\" " +
                $"SAINLayersActive={bot.SAINLayersActive} GoalEnemy={goal} " +
                $"Combat={bot.Decision.CurrentCombatDecision} Squad={bot.Decision.CurrentSquadDecision}"
            );
        }
    }

    private static bool IsNearAnyHuman(Vector3 botPos, HashSet<PlayerComponent> humans)
    {
        foreach (PlayerComponent pc in humans)
        {
            if (pc == null || pc.IsAI)
            {
                continue;
            }

            Player player = pc.Player;
            if (player == null || player.HealthController?.IsAlive != true)
            {
                continue;
            }

            if ((player.Position - botPos).sqrMagnitude <= HumanProximitySq)
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikePriorityMismatch(BotComponent bot, string activeLayer)
    {
        if (string.IsNullOrEmpty(activeLayer))
        {
            return false;
        }

        bool sainCombatDriving =
            string.Equals(activeLayer, CombatSoloLayer.Name, StringComparison.Ordinal)
            || string.Equals(activeLayer, CombatSquadLayer.Name, StringComparison.Ordinal);

        bool obviousQuestDriving =
            activeLayer.IndexOf("quest", StringComparison.OrdinalIgnoreCase) >= 0;

        bool hasThreatSignals =
            bot.GoalEnemy != null
            || bot.Decision.CurrentCombatDecision != ECombatDecision.None
            || bot.Decision.CurrentSquadDecision != ESquadDecision.None;

        if (!hasThreatSignals)
        {
            return false;
        }

        return obviousQuestDriving || !sainCombatDriving;
    }

    public void Dispose()
    {
        try
        {
            GameWorld.OnDispose -= Dispose;
            StopAllCoroutines();
            BotJobs.Dispose();
            BotSpawnController.UnSubscribe();

            if (BotEventHandler != null)
            {
                GrenadeController.UnSubscribe(BotEventHandler);
            }

            if (Bots != null && Bots.Count > 0)
            {
                foreach (var bot in Bots.Values)
                {
                    bot?.Dispose();
                }
            }

            Bots?.Clear();
        }
        catch (Exception ex)
        {
            Logger.LogError($"Dispose SAIN BotController Error: {ex}");
        }

        Destroy(this);
    }

    public bool GetSAIN(BotOwner botOwner, out BotComponent bot)
    {
        bot = BotSpawnController.GetSAIN(botOwner);
        return bot != null;
    }
}
