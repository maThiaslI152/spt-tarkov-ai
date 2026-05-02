using System.Collections.Generic;
using UnityEngine;

namespace SAIN.Components;

/// <summary>
/// Phase 2.5: Offline combat resolution using statistical model (STALKER SMART terrain pattern).
/// Resolves AI-vs-AI combat entirely offline using bot equipment/level stats.
/// No GameObjects, no per-frame updates — just math.
/// </summary>
public static class OfflineCombatResolver
{
    /// <summary>
    /// Resolve combat between two squads of bot statistics.
    /// Returns the result with casualties and estimated combat duration.
    /// Combat power = Σ (bot_power × weapon_multiplier × armor_multiplier × health_factor)
    /// </summary>
    public static OfflineCombatResult ResolveCombat(
        List<BotCombatStats> squadA,
        List<BotCombatStats> squadB,
        Vector3 combatZoneCenter)
    {
        float powerA = CalculateSquadPower(squadA);
        float powerB = CalculateSquadPower(squadB);

        // Add randomness (fog of war)
        float rollA = powerA * Random.Range(0.7f, 1.3f);
        float rollB = powerB * Random.Range(0.7f, 1.3f);

        float winRatio = rollA / (rollA + rollB);

        int casualtiesA = Mathf.RoundToInt((1f - winRatio) * squadA.Count);
        int casualtiesB = Mathf.RoundToInt(winRatio * squadB.Count);

        // Estimate combat duration based on power ratio (more balanced = longer fight)
        float totalPower = powerA + powerB;
        float balanceRatio = Mathf.Min(powerA, powerB) / Mathf.Max(powerA, powerB, 0.01f);
        float estimatedDuration = Mathf.Lerp(3f, 15f, balanceRatio); // 3-15 seconds

        return new OfflineCombatResult
        {
            Winner = winRatio > 0.5f ? squadA : squadB,
            CasualtiesA = casualtiesA,
            CasualtiesB = casualtiesB,
            EstimatedCombatDuration = estimatedDuration,
            CombatZoneCenter = combatZoneCenter,
            WeaponsUsedA = CollectWeapons(squadA),
            WeaponsUsedB = CollectWeapons(squadB),
        };
    }

    private static float CalculateSquadPower(List<BotCombatStats> squad)
    {
        float total = 0f;
        foreach (var bot in squad)
        {
            float healthFactor = Mathf.Clamp01(bot.HealthPercent);
            float armorFactor = Mathf.Clamp(bot.ArmorClass / 6f, 0.1f, 1f); // Armor class 1-6 normalized
            float weaponFactor = Mathf.Clamp(bot.WeaponDamageOutput / 100f, 0.1f, 1.5f);
            total += bot.BasePower * weaponFactor * armorFactor * healthFactor;
        }
        return total;
    }

    private static List<string> CollectWeapons(List<BotCombatStats> squad)
    {
        var weapons = new List<string>();
        foreach (var bot in squad)
        {
            if (!string.IsNullOrEmpty(bot.WeaponTemplateId))
                weapons.Add(bot.WeaponTemplateId);
        }
        return weapons;
    }
}

/// <summary>
/// Statistical representation of a bot for offline combat resolution.
/// Derived from EFT bot profile data — no GameObject needed.
/// </summary>
public class BotCombatStats
{
    public string BotId;
    public string BotType;       // PMC, Scav, Boss, etc.
    public int Level;
    public float BasePower;      // Derived from bot type and level
    public float WeaponDamageOutput;
    public string WeaponTemplateId;
    public float ArmorClass;
    public float HealthPercent;
}

/// <summary>
/// Result of an offline combat engagement.
/// </summary>
public class OfflineCombatResult
{
    public List<BotCombatStats> Winner;       // Winning squad (non-null since it's a list reference)
    public int CasualtiesA;
    public int CasualtiesB;
    public float EstimatedCombatDuration;      // In seconds
    public Vector3 CombatZoneCenter;
    public List<string> WeaponsUsedA;
    public List<string> WeaponsUsedB;

    /// <summary>Total casualties across both sides.</summary>
    public int TotalCasualties => CasualtiesA + CasualtiesB;
}
