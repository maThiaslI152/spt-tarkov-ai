using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace OptimizationCore;

public class CombatAudioSpoofer : MonoBehaviour
{
    public float MaxAudioDistance = 500f;

    private readonly List<ScheduledShot> _activeSchedule = new();
    private Camera _playerCamera;

    private static CombatAudioSpoofer _instance;
    public static CombatAudioSpoofer Instance => _instance;

    public void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }
        _instance = this;
        _playerCamera = Camera.main;
    }

    public void ScheduleCombatAudio(OfflineCombatResult result)
    {
        if (result == null || !result.WasResolved) return;

        float distance = Vector3.Distance(_playerCamera.transform.position, result.CombatZoneCenter);
        if (distance > MaxAudioDistance) return;

        StartCoroutine(PlayCombatSequence(result, distance));
    }

    private IEnumerator PlayCombatSequence(OfflineCombatResult result, float distance)
    {
        float volumeMultiplier = Mathf.Clamp01(1f - (distance / MaxAudioDistance));
        float duration = result.CombatDuration;
        float shotInterval = 1f / Mathf.Max(result.ShotDensity, 0.2f);

        float elapsed = 0f;
        float nextShotTime = 0f;

        while (elapsed < duration)
        {
            if (elapsed >= nextShotTime)
            {
                Vector3 shotPos = result.CombatZoneCenter + Random.insideUnitSphere * Random.Range(5f, 20f);
                PlayGunshot(shotPos, volumeMultiplier, distance > 200f);
                nextShotTime += shotInterval * Random.Range(0.7f, 1.3f);
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!result.IsAmbush)
        {
            int finalShots = Random.Range(1, 4);
            for (int i = 0; i < finalShots; i++)
            {
                Vector3 shotPos = result.CombatZoneCenter + Random.insideUnitSphere * 10f;
                PlayGunshot(shotPos, volumeMultiplier * 0.7f, distance > 300f);
                yield return new WaitForSeconds(Random.Range(0.2f, 0.8f));
            }
        }
    }

    private static void PlayGunshot(Vector3 position, float volume, bool muffled)
    {
        float finalVolume = muffled ? volume * 0.3f : volume;
        AudioSource.PlayClipAtPoint(GetGenericGunshotClip(), position, finalVolume);
    }

    private static AudioClip GetGenericGunshotClip()
    {
        // In production (Windows SPT): load from EFT bundled assets or ship with mod bundle.
        // For example:
        //   return Resources.Load<AudioClip>("audio/weapons/generic_gunshot")
        //       ?? Resources.Load<AudioClip>("weapons/akm_fire");
        //
        // Returns null on Mac/non-SPT — audio spoofing is silently skipped.
        return null;
    }

    private struct ScheduledShot
    {
        public float Time;
        public Vector3 Position;
        public string WeaponType;
    }
}
