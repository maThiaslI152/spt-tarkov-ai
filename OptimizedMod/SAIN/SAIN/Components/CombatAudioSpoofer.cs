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
        if (result == null || result.EstimatedCombatDuration < 0.2f)
        {
            return;
        }

        StartCoroutine(PlayCombatSequence(result));
    }

    private IEnumerator PlayCombatSequence(OfflineCombatResult result)
    {
        float duration = result.EstimatedCombatDuration;
        float elapsed = 0f;

        var shotTimes = new List<float>();
        int shotCount = Mathf.Max(4, Mathf.RoundToInt(duration * 3f));
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
    /// Play a gunshot sound at a position (Unity one-shot). EFT <c>BetterAudio</c> can be wired later
    /// once the exact <c>PlayAtPoint</c> overload is confirmed for the target game version.
    /// </summary>
    private static void PlayGunshotSound(Vector3 position, float volume)
    {
        AudioClip clip = GetFallbackGunshotClip();
        if (clip == null || volume <= 0.01f)
        {
            return;
        }

        AudioSource.PlayClipAtPoint(clip, position, volume);
    }

    /// <summary>
    /// Short procedural pop so offline combat is audible without shipping WAV assets.
    /// </summary>
    private static AudioClip GetFallbackGunshotClip()
    {
        if (_cachedFallbackClip != null)
        {
            return _cachedFallbackClip;
        }

        const int sampleRate = 44100;
        const float clipSeconds = 0.06f;
        int sampleCount = Mathf.Max(256, (int)(sampleRate * clipSeconds));
        var samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleCount;
            float envelope = Mathf.Exp(-t * 8f);
            float crack = Mathf.Sin(i * 0.31f) * (1f - t) * 0.35f;
            samples[i] = Mathf.Clamp((crack + (Mathf.PerlinNoise(i * 0.02f, 0f) - 0.5f) * 0.5f) * envelope, -1f, 1f);
        }

        var clip = AudioClip.Create("SAIN_OfflineGunshotFallback", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        _cachedFallbackClip = clip;
        return clip;
    }

    private static AudioClip _cachedFallbackClip;
}
