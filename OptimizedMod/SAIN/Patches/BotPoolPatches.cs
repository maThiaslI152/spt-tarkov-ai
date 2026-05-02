using HarmonyLib;
using SAIN.Components;
using UnityEngine;

namespace SAIN.Patches;

/// <summary>
/// Phase 4: Harmony patches that intercept bot GameObject destruction and spawn
/// to route through the BotGameObjectPool instead.
/// </summary>
internal static class BotPoolPatches
{
    private static BotGameObjectPool Pool => BotGameObjectPool.Instance;

    /// <summary>
    /// Intercept GameObject.Destroy for bot objects and redirect to pool.
    /// This eliminates Destroy() GC spikes on every bot death.
    /// </summary>
    [HarmonyPatch(typeof(Object), nameof(Object.Destroy), new[] { typeof(Object) })]
    [HarmonyPrefix]
    private static bool InterceptDestroy(Object obj)
    {
        if (Pool == null || !(obj is GameObject go))
            return true; // Let normal Destroy proceed

        // Only intercept bot GameObjects
        if (!IsBotGameObject(go))
            return true;

        // Determine bot type from the GameObject
        string botType = GetBotType(go);
        if (string.IsNullOrEmpty(botType))
            return true;

        // Return to pool instead of destroying
        if (Pool.ReturnToPool(botType, go))
            return false; // Skip the actual Destroy call

        return true; // Pool full or error — let normal Destroy proceed
    }

    /// <summary>
    /// Check if a GameObject is a bot (has BotOwner or Player component with AI flag).
    /// </summary>
    private static bool IsBotGameObject(GameObject go)
    {
        if (go == null)
            return false;

        // Fast check via instance ID — if we already tracked it, it's a bot
        if (Pool.IsActiveBot(go.GetInstanceID()))
            return true;

        // Check for EFT Player component with AI flag
        var player = go.GetComponent<EFT.Player>();
        if (player != null && player.IsAI)
            return true;

        // Check for BotOwner component
        var botOwner = go.GetComponent<EFT.BotOwner>();
        if (botOwner != null)
            return true;

        return false;
    }

    /// <summary>
    /// Determine bot type string from the GameObject for pool keying.
    /// </summary>
    private static string GetBotType(GameObject go)
    {
        var player = go.GetComponent<EFT.Player>();
        if (player != null && player.Profile?.Info?.Settings?.Role != null)
        {
            return player.Profile.Info.Settings.Role.ToString();
        }

        var botOwner = go.GetComponent<EFT.BotOwner>();
        if (botOwner?.Profile?.Info?.Settings?.Role != null)
        {
            return botOwner.Profile.Info.Settings.Role.ToString();
        }

        return "Unknown";
    }
}
