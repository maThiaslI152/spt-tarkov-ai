using System.Collections.Generic;
using UnityEngine;

namespace OptimizationCore;

public interface IOfflineSquad
{
    string SquadId { get; }
    string BotZoneId { get; }
    Vector3 SquadPosition { get; }
    IReadOnlyList<OfflineBotStats> Members { get; }
    bool IsInCombat { get; }
    OfflineCombatResult LastCombatResult { get; }
    void TickOffline();
}
