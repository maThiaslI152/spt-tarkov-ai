using System;
using System.Collections.Generic;
using EFT;
using SAIN.Components.PlayerComponentSpace;
using UnityEngine;

namespace SAIN.Components;

/// <summary>
/// Phase 4: Bot GameObject Pool System.
/// Intercepts EFT bot destroy/spawn to recycle GameObjects instead of destroying/recreating them.
/// Eliminates GameObject.Instantiate() and Destroy() GC spikes on every bot death/wave.
///
/// Pattern: Call of Duty's proven approach — 17 bots in dense CQB at 120+ FPS.
/// BotOwner GameObjects are pooled by bot type and reused across waves.
/// </summary>
public class BotGameObjectPool
{
    public static BotGameObjectPool Instance { get; private set; }

    /// <summary>Maximum pooled bots per type. Exceeding bots are destroyed normally.</summary>
    public int MaxPoolSizePerType = 10;

    /// <summary>Total pooled bots across all types.</summary>
    public int TotalPooledCount
    {
        get
        {
            int count = 0;
            foreach (var queue in _pool.Values)
                count += queue.Count;
            return count;
        }
    }

    /// <summary>Active (spawned, non-queued) bot GameObjects tracked by the pool.</summary>
    public int ActivePooledCount => _activeBotInstanceIds.Count;

    /// <summary>Cumulative successful pulls from the pool (telemetry).</summary>
    public long PoolHitCount => _poolHitCount;

    /// <summary>Cumulative failed pool pulls: empty queue or pooled GO destroyed (telemetry).</summary>
    public long PoolMissCount => _poolMissCount;

    /// <summary>Cumulative bots accepted into the pool from <see cref="ReturnToPool"/>.</summary>
    public long PoolReturnCount => _poolReturnCount;

    /// <summary>Cumulative rejections: null GO, or pool full per type.</summary>
    public long PoolReturnRejectedCount => _poolReturnRejectedCount;

    private long _poolHitCount;
    private long _poolMissCount;
    private long _poolReturnCount;
    private long _poolReturnRejectedCount;

    private readonly Dictionary<string, Queue<PooledBot>> _pool = new();
    private readonly HashSet<int> _activeBotInstanceIds = new();
    /// <summary>Instance IDs currently sitting in a pool queue (idempotent <see cref="ReturnToPool"/> / Destroy guard).</summary>
    private readonly HashSet<int> _pooledInstanceIds = new();

    public BotGameObjectPool()
    {
        Instance = this;
    }

    /// <summary>
    /// Get or create a bot GameObject from the pool.
    /// If a pooled bot of the requested type is available, pulls it from the pool.
    /// Otherwise, returns null to indicate a fresh creation should be used.
    /// </summary>
    public PooledBot TryGetFromPool(string botType, Vector3 spawnPosition)
    {
        if (!_pool.TryGetValue(botType, out var queue) || queue.Count == 0)
        {
            _poolMissCount++;
            return null;
        }

        var pooled = queue.Dequeue();
        if (pooled.GameObject == null)
        {
            // GameObject was destroyed externally — skip
            _poolMissCount++;
            return null;
        }

        int pulledId = pooled.GameObject.GetInstanceID();
        _pooledInstanceIds.Remove(pulledId);

        // Teleport to spawn position
        pooled.GameObject.transform.position = spawnPosition;
        pooled.GameObject.SetActive(true);

        _activeBotInstanceIds.Add(pulledId);

        _poolHitCount++;
        return pooled;
    }

    /// <summary>
    /// Return a bot to the pool instead of destroying it.
    /// Deactivates the GameObject and enqueues for reuse.
    /// </summary>
    public bool ReturnToPool(string botType, GameObject gameObject)
    {
        if (gameObject == null)
        {
            _poolReturnRejectedCount++;
            return false;
        }

        int instanceId = gameObject.GetInstanceID();
        _activeBotInstanceIds.Remove(instanceId);

        if (_pooledInstanceIds.Contains(instanceId))
        {
            return true;
        }

        if (!_pool.TryGetValue(botType, out var queue))
        {
            queue = new Queue<PooledBot>();
            _pool[botType] = queue;
        }

        if (queue.Count >= MaxPoolSizePerType)
        {
            // Pool is full — destroy this one
            _poolReturnRejectedCount++;
            return false;
        }

        // Deactivate and enqueue
        var pooled = new PooledBot(gameObject, botType);
        gameObject.SetActive(false);
        queue.Enqueue(pooled);
        _pooledInstanceIds.Add(instanceId);

        _poolReturnCount++;
        return true;
    }

    /// <summary>
    /// Remove a GameObject from any pool queue (same-raid rematerialize after <see cref="ReturnToPool"/>).
    /// Does not destroy the object; caller re-activates it.
    /// </summary>
    public bool TryRemoveFromPool(GameObject gameObject)
    {
        if (gameObject == null)
        {
            return false;
        }

        int instanceId = gameObject.GetInstanceID();
        foreach (var kv in _pool)
        {
            Queue<PooledBot> queue = kv.Value;
            if (queue == null || queue.Count == 0)
            {
                continue;
            }

            int n = queue.Count;
            var rebuilt = new Queue<PooledBot>(n);
            bool removed = false;
            while (n-- > 0)
            {
                PooledBot pooled = queue.Dequeue();
                if (!removed
                    && pooled.GameObject != null
                    && pooled.GameObject.GetInstanceID() == instanceId)
                {
                    removed = true;
                    continue;
                }

                rebuilt.Enqueue(pooled);
            }

            while (rebuilt.Count > 0)
            {
                queue.Enqueue(rebuilt.Dequeue());
            }

            if (removed)
            {
                _pooledInstanceIds.Remove(instanceId);
                return true;
            }
        }

        return false;
    }

    /// <summary>True if this instance id is currently queued in the pool (not active-tracked).</summary>
    public bool IsInstanceInPoolQueue(int instanceId)
    {
        return _pooledInstanceIds.Contains(instanceId);
    }

    /// <summary>
    /// Check if a GameObject instance is currently an active (non-pooled) bot.
    /// </summary>
    public bool IsActiveBot(int instanceId)
    {
        return _activeBotInstanceIds.Contains(instanceId);
    }

    /// <summary>
    /// Notify that a bot GameObject has been spawned (for tracking active bots).
    /// </summary>
    public void RegisterActiveBot(GameObject gameObject)
    {
        if (gameObject != null)
            _activeBotInstanceIds.Add(gameObject.GetInstanceID());
    }

    /// <summary>
    /// Clear all pooled bots. Called on raid end.
    /// </summary>
    public void ClearPool()
    {
        foreach (var queue in _pool.Values)
        {
            while (queue.Count > 0)
            {
                var pooled = queue.Dequeue();
                if (pooled.GameObject != null)
                {
                    GameObject.Destroy(pooled.GameObject);
                }
            }
        }
        _pool.Clear();
        _activeBotInstanceIds.Clear();
        _pooledInstanceIds.Clear();
    }
}

/// <summary>
/// Represents a pooled bot GameObject ready for reuse.
/// </summary>
public class PooledBot
{
    public GameObject GameObject { get; }
    public string BotType { get; }
    public float PooledAtTime { get; }

    public PooledBot(GameObject gameObject, string botType)
    {
        GameObject = gameObject;
        BotType = botType;
        PooledAtTime = Time.time;
    }
}
