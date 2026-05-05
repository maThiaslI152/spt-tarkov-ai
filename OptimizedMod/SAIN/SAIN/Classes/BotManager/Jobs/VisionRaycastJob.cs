using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using SAIN.Helpers;
using SAIN.Models.Enums;
using SAIN.Models.Structs;
using SAIN.Plugin;
using SAIN.Preset.GlobalSettings;
using SAIN.SAINComponent.Classes.EnemyClasses;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace SAIN.Components;

public class VisionRaycastJob : BotManagerBase
{
    public readonly struct VisionRaycastDiagnosticsSnapshot
    {
        public VisionRaycastDiagnosticsSnapshot(
            long attemptsLineOfSight,
            long attemptsVision,
            long attemptsShoot,
            long hitsNullLineOfSight,
            long hitsNullVision,
            long hitsNullShoot,
            long hitsTargetLineOfSight,
            long hitsTargetVision,
            long hitsTargetShoot,
            long hitsBlockedLineOfSight,
            long hitsBlockedVision,
            long hitsBlockedShoot,
            long effectiveSuccessLineOfSight,
            long effectiveSuccessVision,
            long effectiveSuccessShoot)
        {
            AttemptsLineOfSight = attemptsLineOfSight;
            AttemptsVision = attemptsVision;
            AttemptsShoot = attemptsShoot;
            HitsNullLineOfSight = hitsNullLineOfSight;
            HitsNullVision = hitsNullVision;
            HitsNullShoot = hitsNullShoot;
            HitsTargetLineOfSight = hitsTargetLineOfSight;
            HitsTargetVision = hitsTargetVision;
            HitsTargetShoot = hitsTargetShoot;
            HitsBlockedLineOfSight = hitsBlockedLineOfSight;
            HitsBlockedVision = hitsBlockedVision;
            HitsBlockedShoot = hitsBlockedShoot;
            EffectiveSuccessLineOfSight = effectiveSuccessLineOfSight;
            EffectiveSuccessVision = effectiveSuccessVision;
            EffectiveSuccessShoot = effectiveSuccessShoot;
        }

        public long AttemptsLineOfSight { get; }
        public long AttemptsVision { get; }
        public long AttemptsShoot { get; }
        public long HitsNullLineOfSight { get; }
        public long HitsNullVision { get; }
        public long HitsNullShoot { get; }
        public long HitsTargetLineOfSight { get; }
        public long HitsTargetVision { get; }
        public long HitsTargetShoot { get; }
        public long HitsBlockedLineOfSight { get; }
        public long HitsBlockedVision { get; }
        public long HitsBlockedShoot { get; }

        /// <summary>Rays that would advance <see cref="RaycastResult.TimeLastSuccess"/> (null hit OR target collider/root).</summary>
        public long EffectiveSuccessLineOfSight { get; }

        public long EffectiveSuccessVision { get; }
        public long EffectiveSuccessShoot { get; }
    }

    public static VisionRaycastDiagnosticsSnapshot GetDiagnosticsSnapshot()
    {
        return new VisionRaycastDiagnosticsSnapshot(
            Interlocked.Read(ref _attemptsLos),
            Interlocked.Read(ref _attemptsVision),
            Interlocked.Read(ref _attemptsShoot),
            Interlocked.Read(ref _hitsNullLos),
            Interlocked.Read(ref _hitsNullVision),
            Interlocked.Read(ref _hitsNullShoot),
            Interlocked.Read(ref _hitsTargetLos),
            Interlocked.Read(ref _hitsTargetVision),
            Interlocked.Read(ref _hitsTargetShoot),
            Interlocked.Read(ref _hitsBlockedLos),
            Interlocked.Read(ref _hitsBlockedVision),
            Interlocked.Read(ref _hitsBlockedShoot),
            Interlocked.Read(ref _effectiveSuccessLos),
            Interlocked.Read(ref _effectiveSuccessVision),
            Interlocked.Read(ref _effectiveSuccessShoot));
    }

    /// <summary>
    /// Clears cumulative vision-ray telemetry so per-raid CSV deltas match this raid only.
    /// Called from <see cref="BotManagerComponent.Activate"/>.
    /// </summary>
    public static void ResetDiagnosticsForNewRaid()
    {
        Interlocked.Exchange(ref _attemptsLos, 0);
        Interlocked.Exchange(ref _attemptsVision, 0);
        Interlocked.Exchange(ref _attemptsShoot, 0);
        Interlocked.Exchange(ref _hitsNullLos, 0);
        Interlocked.Exchange(ref _hitsNullVision, 0);
        Interlocked.Exchange(ref _hitsNullShoot, 0);
        Interlocked.Exchange(ref _hitsTargetLos, 0);
        Interlocked.Exchange(ref _hitsTargetVision, 0);
        Interlocked.Exchange(ref _hitsTargetShoot, 0);
        Interlocked.Exchange(ref _hitsBlockedLos, 0);
        Interlocked.Exchange(ref _hitsBlockedVision, 0);
        Interlocked.Exchange(ref _hitsBlockedShoot, 0);
        Interlocked.Exchange(ref _effectiveSuccessLos, 0);
        Interlocked.Exchange(ref _effectiveSuccessVision, 0);
        Interlocked.Exchange(ref _effectiveSuccessShoot, 0);
    }

    private static readonly QueryParameters _losParams = new(LayerMaskClass.HighPolyWithTerrainNoGrassMask);
    private static readonly QueryParameters _visParams = new(LayerMaskClass.AI);
    private static readonly QueryParameters _shootParams = new(LayerMaskClass.HighPolyWithTerrainMaskAI);

    private bool _disposed = false;
    private readonly List<Enemy> _enemies = [];
    private readonly List<EBodyPartColliderType> _colliderTypes = [];
    private readonly List<Vector3> _castPoints = [];

    private static PerformanceSettings PerfSettings
    {
        get { return SAINPlugin.LoadedPreset?.GlobalSettings?.General?.Performance; }
    }

    private static float VisionSinglePartBeyondDistanceMetersClamped
    {
        get
        {
            var s = PerfSettings;
            if (s == null)
            {
                return 150f;
            }

            return Mathf.Clamp(s.VisionSinglePartBeyondDistanceMeters, 50f, 500f);
        }
    }

    private static bool VisionUseFullPartsForHumanBeyondDistance
    {
        get { return PerfSettings != null && PerfSettings.VisionUseFullPartsForHumanBeyondDistance; }
    }

    private static float VisionJobInterval
    {
        get
        {
            var settings = PerfSettings;
            if (settings != null && settings.PerformanceMode)
            {
                float hz = Mathf.Max(1f, settings.VisionRaycastFrequency);
                return 1f / hz;
            }
            return 1f / 30f;
        }
    }

    private static float VisionUpdateInterval
    {
        get
        {
            var settings = PerfSettings;
            if (settings != null && settings.PerformanceMode)
            {
                float hz = Mathf.Max(1f, settings.LookUpdateFrequency);
                return 1f / hz;
            }
            return 1f / 30f;
        }
    }

    private static int MaxRaycastChecks
    {
        get
        {
            var settings = PerfSettings;
            if (settings != null && settings.PerformanceMode)
            {
                return Mathf.Clamp(settings.MaxRaycastsPerEnemy, 1, 3);
            }
            return 3;
        }
    }

    public VisionRaycastJob(BotManagerComponent botcontroller)
        : base(botcontroller)
    {
        botcontroller.StartCoroutine(EnemyVisionJob());
        botcontroller.StartCoroutine(UpdateEFTVision());
    }

    private WaitForSeconds _visionJobWait;
    private float _lastVisionJobInterval;

    private IEnumerator EnemyVisionJob()
    {
        yield return null;
        while (BotController != null && !_disposed)
        {
            float interval = VisionJobInterval;
            if (_visionJobWait == null || Mathf.Abs(_lastVisionJobInterval - interval) > 0.001f)
            {
                _lastVisionJobInterval = interval;
                _visionJobWait = new WaitForSeconds(interval);
            }
            WaitForSeconds wait = _visionJobWait;
            HashSet<BotComponent> bots = BotController.BotSpawnController?.SAINBots;
            if (bots != null && bots.Count > 0)
            {
                FindEnemies(bots, _enemies);
                int enemyCount = _enemies.Count;
                if (enemyCount > 0)
                {
                    int partCount = _enemies[0].Vision.EnemyParts.PartsArray.Length;
                    int raycastChecks = MaxRaycastChecks;
                    int commandCount = CountScheduledRayCommands(_enemies, enemyCount, partCount, raycastChecks);
                    if (commandCount <= 0)
                    {
                        yield return wait;
                        continue;
                    }

                    NativeArray<RaycastHit> hits = new(commandCount, Allocator.TempJob);
                    NativeArray<RaycastCommand> commands = new(commandCount, Allocator.TempJob);

                    int written = CreateCommands(commands, enemyCount, partCount, raycastChecks);
#if DEBUG
                    if (written != commandCount)
                    {
                        Logger.LogError(
                            $"[VisionRaycastJob] Command count mismatch: counted={commandCount}, written={written}. Vision batch may be wrong.");
                    }
#endif
                    _handle = RaycastCommand.ScheduleBatch(commands, hits, 32);

                    yield return null;

                    _handle.Complete();
                    float visionTime = Time.time;
                    AnalyzeHits(hits, commands, enemyCount, partCount, raycastChecks);
                    FinalizeVisionHandoffFromRayBatch(_enemies, enemyCount, visionTime);

                    hits.Dispose();
                    commands.Dispose();
                }
            }

            yield return wait;
        }
    }

    private IEnumerator UpdateEFTVision()
    {
        yield return null;
        while (BotController != null && !_disposed)
        {
            var allBots = BotController.BotSpawnController?.SAINBots;
            if (allBots != null && allBots.Count > 0)
            {
                float currentTime = Time.time;
                foreach (var bot in allBots)
                {
                    if (bot != null)
                    {
                        if (bot.CurrentAILimit >= AILimitSetting.VeryFar)
                            continue;
                        bot.Vision.BotLook.UpdateLook(currentTime);
                    }
                }
            }
            yield return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_handle.IsCompleted)
        {
            _handle.Complete();
        }

        _enemies.Clear();
        _colliderTypes.Clear();
        _castPoints.Clear();
    }

    private JobHandle _handle;

    /// <summary>
    /// Mirrors per-enemy scheduling in <see cref="CreateCommands"/> so native buffer length matches
    /// <see cref="RaycastCommand.ScheduleBatch"/> work (skipped VeryFar / reduced parts and checks).
    /// </summary>
    private static int CountScheduledRayCommands(List<Enemy> enemies, int enemyCount, int partCount, int raycastChecks)
    {
        int total = 0;
        for (int i = 0; i < enemyCount; i++)
        {
            if (!TryGetEnemyRaycastSchedule(enemies[i], partCount, raycastChecks, out int effectivePartCount, out int effectiveChecks))
            {
                continue;
            }

            int perPart = 1 + (effectiveChecks >= 2 ? 1 : 0) + (effectiveChecks >= 3 ? 1 : 0);
            total += effectivePartCount * perPart;
        }

        return total;
    }

    /// <summary>
    /// False when no rays are scheduled for this enemy (VeryFar AI, missing parts, etc.).
    /// </summary>
    private static bool TryGetEnemyRaycastSchedule(
        Enemy enemy,
        int partCount,
        int raycastChecks,
        out int effectivePartCount,
        out int effectiveChecks)
    {
        effectivePartCount = 0;
        effectiveChecks = 0;

        if (enemy == null)
        {
            return false;
        }

        if (enemy.IsAI && enemy.Bot.CurrentAILimit >= AILimitSetting.VeryFar)
        {
            return false;
        }

        effectiveChecks = raycastChecks;
        if (enemy.IsAI && enemy.Bot.CurrentAILimit >= AILimitSetting.Far)
        {
            effectiveChecks = Mathf.Min(raycastChecks, 2);
        }

        effectivePartCount = partCount;
        float singlePartBeyond = VisionSinglePartBeyondDistanceMetersClamped;
        if (enemy.RealDistance > singlePartBeyond
            && !(VisionUseFullPartsForHumanBeyondDistance && !enemy.IsAI))
        {
            effectivePartCount = 1;
        }

        var parts = enemy.Vision?.EnemyParts?.PartsArray;
        if (parts == null || parts.Length == 0)
        {
            return false;
        }

        effectivePartCount = Mathf.Min(effectivePartCount, parts.Length);
        return true;
    }

    private int CreateCommands(NativeArray<RaycastCommand> raycastCommands, int enemyCount, int partCount, int raycastChecks)
    {
        _colliderTypes.Clear();
        _castPoints.Clear();

        int commands = 0;

        const float MinDist = 0.01f;
        const float Padding = 0.05f;

        for (int i = 0; i < enemyCount; i++)
        {
            var enemy = _enemies[i];
            if (!TryGetEnemyRaycastSchedule(enemy, partCount, raycastChecks, out int effectivePartCount, out int effectiveChecks))
            {
                continue;
            }

            var botTransform = enemy.Bot.Transform;

            Vector3 eyePosition = botTransform.EyePosition;
            Vector3 weaponFirePort = botTransform.WeaponData.FirePort;

            var parts = enemy.Vision.EnemyParts.PartsArray;

            for (int j = 0; j < effectivePartCount; j++)
            {
                var part = parts[j];

                SAINBodyPartRaycast raycastData = part.GetRaycast();
                Vector3 castPoint = raycastData.CastPoint;

                _colliderTypes.Add(raycastData.ColliderType);
                _castPoints.Add(castPoint);

                Vector3 eyeVec = castPoint - eyePosition;
                float eyeMag = eyeVec.magnitude;
                Vector3 eyeDir = eyeMag > 1e-6f ? (eyeVec / eyeMag) : Vector3.forward;
                float eyeDist = Mathf.Max(eyeMag, MinDist);

                // Always do LineOfSight check (1st)
                raycastCommands[commands++] = new RaycastCommand(eyePosition, eyeDir, _losParams, eyeDist + Padding);

                if (effectiveChecks >= 2)
                {
                    // Vision check (2nd)
                    raycastCommands[commands++] = new RaycastCommand(eyePosition, eyeDir, _visParams, eyeDist + Padding);
                }

                if (effectiveChecks >= 3)
                {
                    // Shoot check (3rd)
                    Vector3 weaponVec = castPoint - weaponFirePort;
                    float weaponMag = weaponVec.magnitude;
                    Vector3 weaponDir = weaponMag > 1e-6f ? (weaponVec / weaponMag) : Vector3.forward;
                    float weaponDist = Mathf.Max(eyeMag, MinDist);
                    raycastCommands[commands++] = new RaycastCommand(weaponFirePort, weaponDir, _shootParams, weaponDist + Padding);
                }
            }
        }

        return commands;
    }

    /// <summary>
    /// Enemy <see cref="Enemy.TickEnemy"/> (and thus <see cref="EnemyVisionClass.TickEnemy"/>) runs at the enemy-controller
    /// cadence (~10–20 Hz) while <see cref="AnalyzeHits"/> writes <see cref="EnemyPartDataClass"/> ray results as soon as
    /// the batch completes. Re-run part aggregation and <see cref="EnemyVisionClass.UpdateVisibleState"/> here so
    /// <see cref="Enemy.IsVisible"/> / <see cref="Enemy.CanShoot"/> match fresh rays in the same frame (fixes sustained
    /// <c>GoalHumanFinal*</c> near zero on large maps when telemetry still shows ray attempts).
    /// </summary>
    private static void FinalizeVisionHandoffFromRayBatch(List<Enemy> enemies, int enemyCount, float time)
    {
        for (int i = 0; i < enemyCount; i++)
        {
            Enemy enemy = enemies[i];
            if (enemy == null || !enemy.CheckValid())
            {
                continue;
            }

            try
            {
                EnemyVisionClass vision = enemy.Vision;
                vision.EnemyParts.Update(time);
                vision.UpdateVisibleState(time);
            }
            catch (Exception ex)
            {
                // Never fault the vision coroutine: one bad enemy would stop all ray batches for every bot.
                Logger.LogWarning($"[VisionRaycastJob] FinalizeVisionHandoff failed for enemy index {i}: {ex.Message}");
            }
        }
    }

    private void AnalyzeHits(NativeArray<RaycastHit> raycastHits, NativeArray<RaycastCommand> commands, int enemyCount, int partCount, int raycastChecks)
    {
        float time = Time.time;
        int hits = 0;
        int colliderTypeCount = 0;

        for (int i = 0; i < enemyCount; i++)
        {
            var enemy = _enemies[i];
            if (!TryGetEnemyRaycastSchedule(enemy, partCount, raycastChecks, out int effectivePartCount, out int effectiveChecks))
            {
                continue;
            }

            var parts = enemy.Vision.EnemyParts.PartsArray;
            for (int j = 0; j < effectivePartCount; j++)
            {
                var part = parts[j];
                EBodyPartColliderType colliderType = _colliderTypes[colliderTypeCount];
                Vector3 castPoint = _castPoints[colliderTypeCount];
                colliderTypeCount++;

                var gizmos = SAINPlugin.LoadedPreset?.GlobalSettings?.General?.Debug?.Gizmos;
                if (gizmos != null && gizmos.DrawLineOfSightGizmos)
                {
                    var cmd = commands[hits];
                    var hit = raycastHits[hits];

                    Vector3 from = cmd.from;

                    if (hit.collider == null)
                    {
                        DebugGizmos.DrawSphere(castPoint, 0.03f, Color.yellow, 0.2f);
                        DebugGizmos.DrawLine(from, castPoint, Color.green, 0.025f, 0.2f);
                    }
                }

                RaycastHit losHit = raycastHits[hits++];
                ApplyRaycastAndRecord(part, castPoint, colliderType, losHit, ERaycastCheck.LineofSight, time);

                if (effectiveChecks >= 2)
                {
                    RaycastHit visionHit = raycastHits[hits++];
                    ApplyRaycastAndRecord(part, castPoint, colliderType, visionHit, ERaycastCheck.Vision, time);
                }

                if (effectiveChecks >= 3)
                {
                    RaycastHit shootHit = raycastHits[hits++];
                    ApplyRaycastAndRecord(part, castPoint, colliderType, shootHit, ERaycastCheck.Shoot, time);
                }
            }
        }
    }

    private static void ApplyRaycastAndRecord(
        EnemyPartDataClass part,
        Vector3 castPoint,
        EBodyPartColliderType colliderType,
        RaycastHit hit,
        ERaycastCheck checkType,
        float time)
    {
        RecordAttempt(checkType);
        BodyPartCollider expectedCollider = part.GetBodyPartCollider(colliderType);
        if (hit.collider == null)
        {
            RecordNullHit(checkType);
        }
        else if (IsTargetCollider(hit, expectedCollider))
        {
            RecordTargetHit(checkType);
        }
        else
        {
            RecordBlockedHit(checkType);
        }

        if (RaycastResult.CountsAsGameplaySuccess(hit, expectedCollider))
        {
            RecordEffectiveSuccess(checkType);
        }

        part.SetLineOfSight(castPoint, colliderType, hit, checkType, time);
    }

    private static bool IsTargetCollider(RaycastHit hit, BodyPartCollider expectedCollider)
    {
        if (hit.collider == null || expectedCollider?.Collider == null)
        {
            return false;
        }

        if (ReferenceEquals(hit.collider, expectedCollider.Collider))
        {
            return true;
        }

        Transform hitRoot = hit.collider.transform?.root;
        Transform targetRoot = expectedCollider.Collider.transform?.root;
        return hitRoot != null && targetRoot != null && ReferenceEquals(hitRoot, targetRoot);
    }

    private static void RecordAttempt(ERaycastCheck checkType)
    {
        switch (checkType)
        {
            case ERaycastCheck.LineofSight:
                Interlocked.Increment(ref _attemptsLos);
                break;
            case ERaycastCheck.Vision:
                Interlocked.Increment(ref _attemptsVision);
                break;
            case ERaycastCheck.Shoot:
                Interlocked.Increment(ref _attemptsShoot);
                break;
        }
    }

    private static void RecordNullHit(ERaycastCheck checkType)
    {
        switch (checkType)
        {
            case ERaycastCheck.LineofSight:
                Interlocked.Increment(ref _hitsNullLos);
                break;
            case ERaycastCheck.Vision:
                Interlocked.Increment(ref _hitsNullVision);
                break;
            case ERaycastCheck.Shoot:
                Interlocked.Increment(ref _hitsNullShoot);
                break;
        }
    }

    private static void RecordTargetHit(ERaycastCheck checkType)
    {
        switch (checkType)
        {
            case ERaycastCheck.LineofSight:
                Interlocked.Increment(ref _hitsTargetLos);
                break;
            case ERaycastCheck.Vision:
                Interlocked.Increment(ref _hitsTargetVision);
                break;
            case ERaycastCheck.Shoot:
                Interlocked.Increment(ref _hitsTargetShoot);
                break;
        }
    }

    private static void RecordBlockedHit(ERaycastCheck checkType)
    {
        switch (checkType)
        {
            case ERaycastCheck.LineofSight:
                Interlocked.Increment(ref _hitsBlockedLos);
                break;
            case ERaycastCheck.Vision:
                Interlocked.Increment(ref _hitsBlockedVision);
                break;
            case ERaycastCheck.Shoot:
                Interlocked.Increment(ref _hitsBlockedShoot);
                break;
        }
    }

    private static void RecordEffectiveSuccess(ERaycastCheck checkType)
    {
        switch (checkType)
        {
            case ERaycastCheck.LineofSight:
                Interlocked.Increment(ref _effectiveSuccessLos);
                break;
            case ERaycastCheck.Vision:
                Interlocked.Increment(ref _effectiveSuccessVision);
                break;
            case ERaycastCheck.Shoot:
                Interlocked.Increment(ref _effectiveSuccessShoot);
                break;
        }
    }

    private static void FindEnemies(HashSet<BotComponent> bots, List<Enemy> enemies)
    {
        float currentTime = Time.time;
        enemies.Clear();
        foreach (BotComponent bot in bots)
        {
            if (bot != null)
            {
                foreach (Enemy enemy in bot.EnemyController.EnemiesArray)
                {
                    if (enemy == null)
                    {
                        continue;
                    }

                    bool shouldCheckLook = enemy.ShallCheckLook(currentTime, out _);

                    // Combat-critical fast path:
                    // keep raycasts alive for active/current enemies (especially human goal enemies)
                    // even when the normal look-throttle gate is currently false.
                    if (!shouldCheckLook)
                    {
                        if (ReferenceEquals(bot.GoalEnemy, enemy) || enemy.IsCurrentEnemy || (!enemy.IsAI && enemy.EnemyKnown))
                        {
                            shouldCheckLook = true;
                        }
                    }

                    if (shouldCheckLook)
                    {
                        enemies.Add(enemy);
                    }
                }
            }
        }
    }

    private static long _attemptsLos;
    private static long _attemptsVision;
    private static long _attemptsShoot;
    private static long _hitsNullLos;
    private static long _hitsNullVision;
    private static long _hitsNullShoot;
    private static long _hitsTargetLos;
    private static long _hitsTargetVision;
    private static long _hitsTargetShoot;
    private static long _hitsBlockedLos;
    private static long _hitsBlockedVision;
    private static long _hitsBlockedShoot;
    private static long _effectiveSuccessLos;
    private static long _effectiveSuccessVision;
    private static long _effectiveSuccessShoot;
}
