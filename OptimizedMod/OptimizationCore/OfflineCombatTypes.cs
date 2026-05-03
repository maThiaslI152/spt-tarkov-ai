using System.Collections.Generic;
using UnityEngine;

namespace OptimizationCore;

public class OfflineBotStats
{
    public string BotType;
    public int Level;
    public float WeaponDamageOutput;
    public float ArmorMitigation;
    public float HealthFactor;
    public float EffectiveRange;
}

public class OfflineCombatResult
{
    public bool WasResolved;
    public float CombatDuration;
    public int CasualtiesSideA;
    public int CasualtiesSideB;
    public string WinningSquadId;
    public List<string> WeaponTypesUsed;
    public float ShotDensity;
    public Vector3 CombatZoneCenter;
    public bool IsAmbush;
}
