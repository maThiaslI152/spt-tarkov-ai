using UnityEngine;

namespace SAIN.SAINComponent.Classes.EnemyClasses;

public class RaycastResult
{
    public float TimeLastChecked { get; private set; }
    public float TimeLastSuccess { get; private set; }
    public RaycastHit LastRaycastHit { get; private set; }
    public BodyPartCollider LastSuccessBodyPart { get; private set; }
    public Vector3? LastSuccessPoint { get; private set; }

    public void Update(Vector3 castPoint, BodyPartCollider bodyPartCollider, RaycastHit raycastHit, float time)
    {
        TimeLastChecked = time;
        LastRaycastHit = raycastHit;

        // Treat either "no obstruction" OR "direct hit on intended enemy body collider"
        // as a successful LOS/vision/shoot ray. Some EFT builds include target colliders
        // in these masks; null-only success causes false negatives (bots only use last-known/sound).
        if (CountsAsGameplaySuccess(raycastHit, bodyPartCollider))
        {
            LastSuccessBodyPart = bodyPartCollider;
            LastSuccessPoint = castPoint;
            TimeLastSuccess = time;
        }
        else
        {
            LastSuccessBodyPart = null;
            LastSuccessPoint = null;
        }
    }

    /// <summary>
    /// Same predicate as gameplay LOS/vision/shoot success (matches `VisionRaycastJob` schema-8 effective-success telemetry).
    /// </summary>
    public static bool CountsAsGameplaySuccess(RaycastHit hit, BodyPartCollider bodyPartCollider)
    {
        return hit.collider == null || IsTargetCollider(hit, bodyPartCollider);
    }

    private static bool IsTargetCollider(RaycastHit hit, BodyPartCollider targetBodyPart)
    {
        if (hit.collider == null || targetBodyPart?.Collider == null)
        {
            return false;
        }

        if (ReferenceEquals(hit.collider, targetBodyPart.Collider))
        {
            return true;
        }

        Transform hitRoot = hit.collider.transform?.root;
        Transform targetRoot = targetBodyPart.Collider.transform?.root;
        return hitRoot != null && targetRoot != null && ReferenceEquals(hitRoot, targetRoot);
    }
}
