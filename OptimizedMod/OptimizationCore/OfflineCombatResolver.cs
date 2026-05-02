using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace OptimizationCore;

public class OfflineCombatResolver
{
    public OfflineCombatResult ResolveCombat(IOfflineSquad squadA, IOfflineSquad squadB)
    {
        float powerA = CalculateSquadPower(squadA.Members);
        float powerB = CalculateSquadPower(squadB.Members);

        float rollA = powerA * Random.Range(0.7f, 1.3f);
        float rollB = powerB * Random.Range(0.7f, 1.3f);

        float winRatio = rollA / (rollA + rollB);
        bool squadAWins = winRatio > 0.5f;

        int totalA = squadA.Members.Count;
        int totalB = squadB.Members.Count;

        int casualtiesA = Mathf.RoundToInt((1f - winRatio) * totalA);
        int casualtiesB = Mathf.RoundToInt(winRatio * totalB);

        float combatDuration = Mathf.Lerp(3f, 30f, 1f - Mathf.Abs(winRatio - 0.5f) * 2f);

        var weaponTypes = CollectWeaponTypes(squadA, squadB);
        float shotDensity = Mathf.Lerp(0.5f, 3f, Mathf.Min(totalA + totalB, 10) / 10f);
        bool isAmbush = Mathf.Abs(powerA - powerB) / Mathf.Max(powerA, powerB) > 0.5f;

        return new OfflineCombatResult
        {
            WasResolved = true,
            CombatDuration = combatDuration,
            CasualtiesSideA = casualtiesA,
            CasualtiesSideB = casualtiesB,
            WinningSquadId = squadAWins ? squadA.SquadId : squadB.SquadId,
            WeaponTypesUsed = weaponTypes,
            ShotDensity = shotDensity,
            CombatZoneCenter = (squadA.SquadPosition + squadB.SquadPosition) * 0.5f,
            IsAmbush = isAmbush
        };
    }

    private static float CalculateSquadPower(IReadOnlyList<OfflineBotStats> members)
    {
        float power = 0f;
        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            power += m.WeaponDamageOutput * m.ArmorMitigation * m.HealthFactor;
        }
        return power;
    }

    private static List<string> CollectWeaponTypes(IOfflineSquad squadA, IOfflineSquad squadB)
    {
        var weapons = new HashSet<string>();
        AddWeapons(squadA, weapons);
        AddWeapons(squadB, weapons);
        return weapons.ToList();
    }

    private static void AddWeapons(IOfflineSquad squad, HashSet<string> weapons)
    {
        foreach (var m in squad.Members)
        {
            if (!string.IsNullOrEmpty(m.BotType))
                weapons.Add(m.BotType);
        }
    }
}
