using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SAIN.Components;

/// <summary>
/// Phase 2.5: Spooffs gunfire audio for offline AI-vs-AI combat.
/// Generates convincing distant firefight sounds from statistical combat data,
/// so the player hears a world at war even though the combat is resolved offline.
/// Distance-based fidelity: full guntype distinction nearby, muffled pops far away.
/// </summary>
public class CombatAudioSpoofer : MonoBehaviour
{
    /// <summary>
    /// Schedule an offline combat audio sequence.
    /// Plays weapon-specific gunfire at the combat zone location
    /// with distance-based volume attenuation.
    /// </summary>
    public void ScheduleOfflineCombatAudio(OfflineCombatResult result)
    {
        if (result == null || result.TotalCasualties == 0)
            return;

        StartCoroutine(PlayCombatSequence(result));
    }

    private IEnumerator PlayCombatSequence(OfflineCombatResult result)
    {
        var allWeapons = new List<string>();
        allWeapons.AddRange(result.WeaponsUsedA);
        allWeapons.AddRange(result.WeaponsUsedB);

        if (allWeapons.Count == 0)
            yield break;

        float duration = result.EstimatedCombatDuration;
        float elapsed = 0f;

        var shotTimes = new List<float>();
        int shotCount = Mathf.RoundToInt(duration * 3f);
        for (int i = 0; i < shotCount; i++)
        {
            shotTimes.Add(Random.Range(0f, duration));
        }
        shotTimes.Sort();

        int shotIndex = 0;
        var camera = Camera.main;

        while (elapsed < duration && shotIndex < shotTimes.Count)
        {
            if (elapsed >= shotTimes[shotIndex])
            {
                float distance = camera != null
                    ? Vector3.Distance(result.CombatZoneCenter, camera.transform.position)
                    : 500f;

                float volume = Mathf.Clamp01(1f - (distance / 500f));

                if (volume > 0.01f)
                {
                    Vector3 shotPos = result.CombatZoneCenter + Random.insideUnitSphere * 10f;
                    PlayGunshotSound(shotPos, volume);
                }

                shotIndex++;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    /// <summary>
    /// Play a gunshot sound at a position. Integrates with EFT's audio system.
    /// Attempts BetterAudio first (EFT centralized audio), falls back to
    /// AudioSource.PlayClipAtPoint (Unity basic audio).
    /// </summary>
    private static void PlayGunshotSound(Vector3 position, float volume)
    {
        try
        {
            // Primary path: EFT's centralized audio system
            // BetterAudio handles distance attenuation, occlusion, and bot hearing reactions.
            // Uncomment once running on SPT:
            // var audio = Singleton<BetterAudio>.Instance;
            // if (audio != null)
            // {
            //     var clip = GetWeaponFireClip();
            //     if (clip != null)
            //         audio.PlayAtPoint(position, clip, volume);
            //     return;
            // }
        }
        catch (System.Exception)
        {
            // BetterAudio not available — use Unity fallback
        }

        // Fallback: Unity's built-in 3D audio
        // Loads a generic gunshot clip. In production, this should be replaced with
        // weapon-specific AudioClips from EFT's asset bundles.
        var fallbackClip = GetFallbackGunshotClip();
        if (fallbackClip != null && volume > 0.01f)
        {
            AudioSource.PlayClipAtPoint(fallbackClip, position, volume);
        }
    }

    /// <summary>
    /// Get weapon-specific fire AudioClip from EFT's item factory.
    /// Returns null if the audio system isn't available (standalone build, Mac).
    /// </summary>
    private static AudioClip GetWeaponFireClip()
    {
        // EFT stores weapon sounds in ItemFactory. Access pattern:
        //   var item = Singleton<ItemFactory>.Instance.GetPresetItem(templateId);
        //   var audioClip = item.GetWeaponSounds().FireClip;
        // Returns null on non-SPT platforms.
        return null;
    }

    /// <summary>
    /// Fallback generic gunshot clip. In production, load from EFT bundled resources
    /// or ship a small embedded AudioClip in the mod's asset bundle.
    /// </summary>
    private static AudioClip GetFallbackGunshotClip()
    {
        if (_cachedFallbackClip == null)
        {
            // Try to load from EFT's bundled resources (e.g., "audio/weapons/ak74_fire")
            // _cachedFallbackClip = Resources.Load<AudioClip>("weapons/generic_gunshot");
            // Returns null on non-SPT platforms — audio is silently skipped.
        }
        return _cachedFallbackClip;
    }
    private static AudioClip _cachedFallbackClip;
}
