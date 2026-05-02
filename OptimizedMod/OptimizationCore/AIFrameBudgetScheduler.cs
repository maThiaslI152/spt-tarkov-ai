using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace OptimizationCore;

public class AIFrameBudgetScheduler : MonoBehaviour
{
    public float MaxAIBudgetMs = 2.0f;

    private readonly Stopwatch _frameTimer = new();
    private readonly List<IBudgetedAI> _visibleBots = new();
    private readonly List<IBudgetedAI> _audibleBots = new();
    private readonly List<IBudgetedAI> _occludedBots = new();
    private readonly List<IOfflineSquad> _offlineSquads = new();

    private static AIFrameBudgetScheduler _instance;
    public static AIFrameBudgetScheduler Instance => _instance;

    public bool IsActive { get; set; } = true;

    public void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }
        _instance = this;
    }

    public void RegisterBot(IBudgetedAI bot, PerceptionTier tier)
    {
        UnregisterBot(bot);
        GetListForTier(tier).Add(bot);
    }

    public void UnregisterBot(IBudgetedAI bot)
    {
        _visibleBots.Remove(bot);
        _audibleBots.Remove(bot);
        _occludedBots.Remove(bot);
    }

    public void UpdateBotTier(IBudgetedAI bot, PerceptionTier oldTier, PerceptionTier newTier)
    {
        GetListForTier(oldTier).Remove(bot);
        GetListForTier(newTier).Add(bot);
    }

    public void RegisterOfflineSquad(IOfflineSquad squad)
    {
        if (!_offlineSquads.Contains(squad))
            _offlineSquads.Add(squad);
    }

    public void UnregisterOfflineSquad(IOfflineSquad squad)
    {
        _offlineSquads.Remove(squad);
    }

    public void Update()
    {
        if (!IsActive) return;

        _frameTimer.Restart();

        ProcessOfflineSquads();

        ProcessTier(_visibleBots);
        ProcessTier(_audibleBots);
        ProcessTier(_occludedBots);
    }

    private void ProcessOfflineSquads()
    {
        for (int i = 0; i < _offlineSquads.Count; i++)
        {
            if (_frameTimer.Elapsed.TotalMilliseconds >= MaxAIBudgetMs)
                return;

            _offlineSquads[i].TickOffline();
        }
    }

    private void ProcessTier(List<IBudgetedAI> bots)
    {
        for (int i = 0; i < bots.Count; i++)
        {
            if (_frameTimer.Elapsed.TotalMilliseconds >= MaxAIBudgetMs)
                return;

            bots[i]?.ProcessAITick();
        }
    }

    private List<IBudgetedAI> GetListForTier(PerceptionTier tier)
    {
        return tier switch
        {
            PerceptionTier.Visible => _visibleBots,
            PerceptionTier.Audible => _audibleBots,
            PerceptionTier.Occluded => _occludedBots,
            _ => _occludedBots
        };
    }

    public string GetBudgetReport()
    {
        return $"AI Budget: {_frameTimer.Elapsed.TotalMilliseconds:F2}ms / {MaxAIBudgetMs}ms | " +
               $"Visible:{_visibleBots.Count} Audible:{_audibleBots.Count} " +
               $"Occluded:{_occludedBots.Count} Offline:{_offlineSquads.Count}";
    }
}
