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
using SAIN.Interop;
using SAIN.Layers;
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

    /// <summary>Recycled bot GameObjects (Phase 4 pool; Phase 2 will route AILimit through this).</summary>
    public BotGameObjectPool Pool { get; private set; }

    /// <summary>SMART dematerialize / rematerialize seam (Phase 1 API; AILimit calls in Phase 2).</summary>
    public BotDematerializationController Dematerialization { get; private set; }

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
        OfflineSquadWorldSync.ResetForNewRaid();
        VisionRaycastJob.ResetDiagnosticsForNewRaid();
        Pool = new BotGameObjectPool();
        Dematerialization = new BotDematerializationController(this);

        SyncAiFrameBudgetFromPreset();

        GameWorld.OnDispose += Dispose;
    }

    /// <summary>
    /// Keeps <see cref="AIFrameBudgetScheduler.MaxAIBudgetMs"/> in sync with preset (hot-reload safe).
    /// </summary>
    private void SyncAiFrameBudgetFromPreset()
    {
        if (BudgetScheduler == null)
            return;

        float ms = SAINPlugin.LoadedPreset?.GlobalSettings?.General?.Performance?.MaxAiBudgetMilliseconds ?? 2f;
        ms = Mathf.Clamp(ms, 1f, 10f);

        if (Mathf.Abs(BudgetScheduler.MaxAIBudgetMs - ms) > 0.0001f)
            BudgetScheduler.MaxAIBudgetMs = ms;

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
        OfflineSquadWorldSync.TrySync(this, currentTime);
        OfflineSquadMaterialization.TryRematerializeDematSquadsNearHumans(this, currentTime);
        BudgetScheduler.ProcessFrame(BotSpawnController.SAINBots, currentTime, deltaTime);
    }

    /// <summary>
    /// BigBrain arbitration hints: active layer vs SAIN combat/extract stack, gated by SAINPerfLog diagnostic toggle.
    /// Optional verbose mode logs every human-proximate bot (see SAINPerfLog F12 config).
    /// </summary>
    private void MaybeLogBigBrainArbitrationHints(float currentTime)
    {
        if (!SainPerfLogInterop.IsDiagnosticLoggingEnabled)
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

        bool verbose = SainPerfLogInterop.IsBigBrainVerboseSamplingEnabled;

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
            bool mismatch = EvaluateBigBrainPriorityMismatch(bot, owner, layer);
            if (verbose && !mismatch)
            {
                LogBigBrainDiagLine(bot, owner, layer, mismatch: false, infoOnly: true);
            }

            if (!mismatch)
            {
                continue;
            }

            LogBigBrainDiagLine(bot, owner, layer, mismatch: true, infoOnly: false);
        }
    }

    private static void LogBigBrainDiagLine(BotComponent bot, BotOwner owner, string layer, bool mismatch, bool infoOnly)
    {
        string goal =
            bot.GoalEnemy?.EnemyPlayer?.Profile?.Nickname
            ?? bot.GoalEnemy?.EnemyProfileId
            ?? "none";
        string brain = owner.Brain?.BaseBrain?.ShortName() ?? "?";
        bool pressure = SAINExternal.IsBotUnderCombatPressure(owner);
        string reason = DescribeBigBrainDiagReason(bot, owner, layer, pressure);
        bool exUsec = bot.Info?.Profile?.WildSpawnType == WildSpawnType.exUsec;
        bool canShootNow = bot.GoalEnemy != null && bot.GoalEnemy.IsVisible && bot.GoalEnemy.CanShoot;

        string msg =
            $"[SAIN DIAG][BigBrain]{(mismatch ? "" : "[sample]")} {owner.name} brain={brain} layer=\"{layer}\" " +
            $"reason={reason} SAINLayersActive={bot.SAINLayersActive} SAINActiveLayer={bot.ActiveLayer} " +
            $"pressure={pressure} GoalEnemy={goal} Combat={bot.Decision.CurrentCombatDecision} Squad={bot.Decision.CurrentSquadDecision}" +
            $" exUsec={exUsec} canShootNow={canShootNow}";

        if (mismatch && exUsec && pressure)
        {
            msg += " exUsecMismatchUnderPressure=1";
        }

        if (infoOnly)
        {
            Logger.LogInfo(msg);
        }
        else
        {
            Logger.LogWarning(msg);
        }
    }

    /// <summary>Human-readable bucket for CSV / BepInEx diagnostics (not identical to <see cref="EvaluateBigBrainPriorityMismatch"/>).</summary>
    public static string DescribeBigBrainMismatchReason(BotComponent bot, BotOwner owner, string layer)
    {
        bool pressure = SAINExternal.IsBotUnderCombatPressure(owner);
        return DescribeBigBrainDiagReason(bot, owner, layer, pressure);
    }

    /// <summary>Same rule as in-raid <c>[SAIN DIAG][BigBrain]</c> mismatch detection — used by SAINPerfLog CSV.</summary>
    public static bool EvaluateBigBrainPriorityMismatch(BotComponent bot, BotOwner owner, string activeLayer)
    {
        return LooksLikePriorityMismatch(bot, owner, activeLayer);
    }

    private static string DescribeBigBrainDiagReason(BotComponent bot, BotOwner owner, string layer, bool pressure)
    {
        bool sainCombat =
            string.Equals(layer, CombatSoloLayer.Name, StringComparison.Ordinal)
            || string.Equals(layer, CombatSquadLayer.Name, StringComparison.Ordinal);
        if (sainCombat)
        {
            return "sainCombatLayer";
        }

        if (LooksLikeThirdPartyOrVanillaLayer(layer))
        {
            return "thirdPartyOrVanilla";
        }

        if (string.Equals(layer, ExtractLayer.Name, StringComparison.Ordinal))
        {
            return "sainExtract";
        }

        if (pressure && !sainCombat)
        {
            return "combatPressureNonSainCombat";
        }

        return "otherNonCombat";
    }

    private static bool LooksLikeThirdPartyOrVanillaLayer(string activeLayer)
    {
        if (string.IsNullOrEmpty(activeLayer))
        {
            return false;
        }

        if (activeLayer.Contains("quest", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (activeLayer.Contains("Loot", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (activeLayer.Contains("Navigate", StringComparison.OrdinalIgnoreCase)
            || activeLayer.Contains("Navigation", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (activeLayer.Contains("Patrol", StringComparison.OrdinalIgnoreCase)
            || activeLayer.Contains("Stationary", StringComparison.OrdinalIgnoreCase)
            || activeLayer.Contains("StandBy", StringComparison.OrdinalIgnoreCase)
            || activeLayer.Contains("Peace", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
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

    private static bool LooksLikePriorityMismatch(BotComponent bot, BotOwner owner, string activeLayer)
    {
        if (string.IsNullOrEmpty(activeLayer))
        {
            return false;
        }

        bool sainCombatDriving =
            string.Equals(activeLayer, CombatSoloLayer.Name, StringComparison.Ordinal)
            || string.Equals(activeLayer, CombatSquadLayer.Name, StringComparison.Ordinal);

        bool hasThreatSignals =
            bot.GoalEnemy != null
            || bot.Decision.CurrentCombatDecision != ECombatDecision.None
            || bot.Decision.CurrentSquadDecision != ESquadDecision.None
            || SAINExternal.IsBotUnderCombatPressure(owner);

        if (!hasThreatSignals)
        {
            return false;
        }

        if (sainCombatDriving)
        {
            return false;
        }

        if (LooksLikeThirdPartyOrVanillaLayer(activeLayer))
        {
            return true;
        }

        return SAINExternal.IsBotUnderCombatPressure(owner);
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

            Dematerialization?.ResetForNewRaid();
            Pool?.ClearPool();

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
