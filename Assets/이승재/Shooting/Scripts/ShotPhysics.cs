using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Reinforcement.Shooting
{
    public enum ContactKind { None, Obstacle, Actor }

    public enum ShotObstacleResponse { Default, Ignore, Block }

    // Scene modules can classify their colliders without replacing the legacy ShotObstacle component.
    public interface IShotObstacleResolver
    {
        ShotObstacleResponse Classify(Collider collider, out string obstacleId);
    }

    public struct ShotContact
    {
        public ContactKind Kind;
        public ShotActor Actor;
        public Collider Collider;
        public Vector3 Point;
        public float Distance;
        public string ObstacleId;
    }

    public static class ShotPhysics
    {
        // Small contact tolerance at the origin, in Unity units. Physics rays alone omit
        // colliders that enclose the ray origin. The probe ignores the owner's own colliders.
        private const float OriginRadius = 0.001f;
        private const int MissAttempts = 8;
        private static RaycastHit[] rayHits = new RaycastHit[16];
        private static Collider[] overlaps = new Collider[8];
        private static readonly List<Collider> hitShapes = new List<Collider>();

        public static ShotContact Trace(ShotActor owner, Vector3 origin, Vector3 direction, float length, int mask)
        {
            direction.Normalize();
            var nearest = new ShotContact { Point = origin + direction * length, Distance = length };
            if (!owner || length <= 0f || direction == Vector3.zero) return nearest;
            PhysicsScene scene = owner.gameObject.scene.GetPhysicsScene();
            if (ContactAtOrigin(scene, owner, origin, mask, out ShotContact inside)) return inside;

            int count;
            while ((count = scene.Raycast(origin, direction, rayHits, length, mask, QueryTriggerInteraction.Ignore))
                == rayHits.Length)
                Array.Resize(ref rayHits, rayHits.Length * 2);

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = rayHits[i];
                if (hit.distance > nearest.Distance) continue;
                ShotActor actor = hit.collider.GetComponentInParent<ShotActor>();
                if (actor == owner) continue;
                var contact = MakeContact(hit.collider, actor, hit.point, hit.distance);
                if (Prefer(contact, nearest)) nearest = contact;
            }
            return nearest;
        }

        public static bool CheckMount(ShotActor owner, Vector3 anchor, Vector3 muzzle, int mask, out ShotContact contact)
        {
            contact = default;
            if (!owner) return false;

            if (ContactAtOrigin(owner.gameObject.scene.GetPhysicsScene(), owner, muzzle, mask, out contact))
                return true;
            Vector3 segment = muzzle - anchor;
            float length = segment.magnitude;
            if (length == 0f) return false;
            contact = Trace(owner, anchor, segment, length, mask);
            if (contact.Kind == ContactKind.None) return false;
            // Keep the reported distance relative to the muzzle even for a mount obstruction.
            contact.Distance = Vector3.Distance(contact.Point, muzzle);
            return true;
        }

        public static bool ClearMissDirection(ShotActor target, Vector3 origin, Vector3 aimPoint, float range,
            int mask, System.Random random, out Vector3 direction)
        {
            direction = Vector3.zero;
            Vector3 toAim = aimPoint - origin;
            float distance = toAim.magnitude;
            if (!target || distance < OriginRadius || range <= 0f || random == null) return false;

            var scene = target.gameObject.scene.GetPhysicsScene();
            int count = FindOverlaps(scene, origin, mask);
            for (int i = 0; i < count; i++)
                if (overlaps[i].GetComponentInParent<ShotActor>() == target) return false;

            // Use the compound silhouette, including child hitboxes, even if a wall hides them.
            target.GetComponentsInChildren(false, hitShapes);
            Bounds bounds = new Bounds(aimPoint, Vector3.zero);
            for (int i = hitShapes.Count - 1; i >= 0; i--)
            {
                Collider shape = hitShapes[i];
                if (shape.enabled && !shape.isTrigger && (mask & (1 << shape.gameObject.layer)) != 0 &&
                    shape.GetComponentInParent<ShotActor>() == target)
                    bounds.Encapsulate(shape.bounds);
                else
                    hitShapes.RemoveAt(i);
            }
            if (hitShapes.Count == 0) return false;

            Vector3 forward = toAim / distance;
            Vector3 reference = Mathf.Abs(forward.y) < 0.9f ? Vector3.up : Vector3.right;
            Vector3 right = Vector3.Cross(reference, forward).normalized;
            Vector3 up = Vector3.Cross(forward, right);
            float radius = (bounds.center - aimPoint).magnitude + bounds.extents.magnitude;
            float tangentAngle = Mathf.Asin(Mathf.Clamp01(radius / distance));
            for (int attempt = 0; attempt < MissAttempts; attempt++)
            {
                float azimuth = (float)random.NextDouble() * 2f * Mathf.PI;
                Vector3 side = Mathf.Cos(azimuth) * right + Mathf.Sin(azimuth) * up;
                float angle = Mathf.Min(89f * Mathf.Deg2Rad, tangentAngle * (1.05f + 0.06f * attempt));
                Vector3 candidate = (Mathf.Cos(angle) * forward + Mathf.Sin(angle) * side).normalized;
                var ray = new Ray(origin, candidate);
                bool intersects = false;
                foreach (Collider shape in hitShapes)
                {
                    if (!shape.Raycast(ray, out _, range)) continue;
                    intersects = true;
                    break;
                }
                if (intersects) continue;
                direction = candidate;
                hitShapes.Clear();
                return true;
            }
            hitShapes.Clear();
            return false;
        }

        private static int FindOverlaps(PhysicsScene scene, Vector3 point, int mask)
        {
            int count;
            while ((count = scene.OverlapSphere(point, OriginRadius, overlaps, mask, QueryTriggerInteraction.Ignore))
                == overlaps.Length)
                Array.Resize(ref overlaps, overlaps.Length * 2);
            return count;
        }

        private static bool ContactAtOrigin(PhysicsScene scene, ShotActor owner, Vector3 point, int mask,
            out ShotContact nearest)
        {
            nearest = default;
            int count = FindOverlaps(scene, point, mask);
            for (int i = 0; i < count; i++)
            {
                Collider shape = overlaps[i];
                ShotActor actor = shape.GetComponentInParent<ShotActor>();
                if (actor == owner) continue;
                var candidate = MakeContact(shape, actor, point, 0f);
                if (Prefer(candidate, nearest)) nearest = candidate;
            }
            return nearest.Kind != ContactKind.None;
        }

        private static ShotContact MakeContact(Collider shape, ShotActor actor, Vector3 point, float distance)
        {
            string obstacleId = null;
            if (!actor)
            {
                var obstacle = shape.GetComponentInParent<ShotObstacle>();
                if (obstacle)
                {
                    obstacleId = obstacle.ObstacleId;
                }
                else if (shape.GetComponentInParent<IShotObstacleResolver>() is { } resolver)
                {
                    if (resolver.Classify(shape, out obstacleId) == ShotObstacleResponse.Ignore)
                        return default;
                }
            }

            return new ShotContact
            {
                Kind = actor ? ContactKind.Actor : ContactKind.Obstacle, Actor = actor, Collider = shape,
                Point = point, Distance = distance, ObstacleId = obstacleId
            };
        }

        // Co-located surfaces favour cover. Registered cover and actors have stable IDs.
        private static bool Prefer(ShotContact candidate, ShotContact nearest)
        {
            if (candidate.Kind == ContactKind.None) return false;
            if (nearest.Kind == ContactKind.None || candidate.Distance < nearest.Distance) return true;
            if (candidate.Distance > nearest.Distance) return false;
            if (candidate.Kind != nearest.Kind) return candidate.Kind == ContactKind.Obstacle;
            if (candidate.Actor) return candidate.Actor.ActorId < nearest.Actor.ActorId;
            return string.CompareOrdinal(candidate.ObstacleId, nearest.ObstacleId) < 0;
        }
    }
}
