using System.Collections.Generic;
using UnityEngine;

namespace OptimizationCore;

public class PerceptionSystem : MonoBehaviour
{
    public float VisibilityCheckInterval = 0.5f;
    public float AudibilityCheckInterval = 1.0f;
    public float MaxHearingDistance = 200f;
    public float SprintHearingDistance = 60f;
    public float GunfireHearingDuration = 3f;

    private Camera _playerCamera;
    private Plane[] _frustumPlanes = new Plane[6];
    private readonly Dictionary<int, float> _lastVisCheck = new();
    private readonly Dictionary<int, float> _lastAudCheck = new();
    private readonly Dictionary<int, PerceptionTier> _cachedTiers = new();

    private static PerceptionSystem _instance;
    public static PerceptionSystem Instance => _instance;

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

    public void Update()
    {
        if (_playerCamera == null)
        {
            _playerCamera = Camera.main;
            return;
        }

        GeometryUtility.CalculateFrustumPlanes(_playerCamera, _frustumPlanes);
    }

    public PerceptionTier EvaluateBot(Vector3 botPosition, int botId, bool isSprinting, bool recentlyFired, float timeSinceLastFire)
    {
        float now = Time.time;

        if (!_lastVisCheck.TryGetValue(botId, out float lastVis) || now - lastVis > VisibilityCheckInterval)
        {
            _lastVisCheck[botId] = now;
            if (IsVisible(botPosition))
            {
                _cachedTiers[botId] = PerceptionTier.Visible;
                return PerceptionTier.Visible;
            }
        }

        if (!_lastAudCheck.TryGetValue(botId, out float lastAud) || now - lastAud > AudibilityCheckInterval)
        {
            _lastAudCheck[botId] = now;

            float distance = Vector3.Distance(_playerCamera.transform.position, botPosition);
            bool audible = false;

            if (recentlyFired && timeSinceLastFire < GunfireHearingDuration && distance < MaxHearingDistance)
                audible = true;
            else if (isSprinting && distance < SprintHearingDistance)
                audible = true;

            if (audible)
            {
                _cachedTiers[botId] = PerceptionTier.Audible;
                return PerceptionTier.Audible;
            }
        }

        if (_cachedTiers.TryGetValue(botId, out PerceptionTier cached))
            return cached;

        _cachedTiers[botId] = PerceptionTier.Occluded;
        return PerceptionTier.Occluded;
    }

    private bool IsVisible(Vector3 botPosition)
    {
        if (_playerCamera == null) return false;

        Bounds botBounds = new(botPosition, Vector3.one * 0.5f);
        if (!GeometryUtility.TestPlanesAABB(_frustumPlanes, botBounds))
            return false;

        Vector3 direction = botPosition - _playerCamera.transform.position;
        if (Physics.Raycast(_playerCamera.transform.position, direction.normalized, out RaycastHit hit,
            direction.magnitude, LayerMaskClass.HighPolyWithTerrainNoGrassMask, QueryTriggerInteraction.Ignore))
        {
            return hit.collider != null && hit.distance >= direction.magnitude - 0.5f;
        }

        return true;
    }

    public void ClearCache(int botId)
    {
        _lastVisCheck.Remove(botId);
        _lastAudCheck.Remove(botId);
        _cachedTiers.Remove(botId);
    }
}
