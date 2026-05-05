using UnityEngine;

namespace SAIN.Components;

/// <summary>
/// Documents and exercises <see cref="BotGameObjectPool.TryGetFromPool"/> so perf telemetry can show pool miss/hit paths.
/// Full EFT <c>BotCreator</c> integration remains a follow-up (spawn needs <c>BotCreationDataClass</c>).
/// </summary>
public static class BotSpawnPoolBridge
{
    /// <summary>
    /// One no-op pull per raid so <c>PoolMissCount</c> advances when the pool is empty (CSV sanity check).
    /// </summary>
    public static void RaidStartProbePoolPull()
    {
        BotGameObjectPool pool = BotGameObjectPool.Instance;
        if (pool == null)
        {
            return;
        }

        _ = pool.TryGetFromPool("__raid_probe__", Vector3.zero);
    }
}
