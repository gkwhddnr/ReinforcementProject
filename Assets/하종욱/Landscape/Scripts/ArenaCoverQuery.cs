using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ReinProject.Landscape
{
    public enum ArenaCoverKind
    {
        None,
        Terrain,
        Prop
    }

    public static class ArenaCoverQuery
    {
        public const string SceneLayer = "ArenaCover";
        public const string TerrainTag = "ArenaTerrain";

        private static RaycastHit[] _hits = new RaycastHit[16];

        public static ArenaCoverKind GetOccluder(Scene arena, Vector3 observerEye, Vector3 targetPoint)
        {
            if (!TryGetOccluder(arena, observerEye, targetPoint, out string tag, out _))
                return ArenaCoverKind.None;

            return tag == TerrainTag ? ArenaCoverKind.Terrain : ArenaCoverKind.Prop;
        }

        /// <summary>Finds the closest tagged cover hit, skipping actors, markers and other scenes.</summary>
        public static bool TryGetOccluder(Scene arena, Vector3 observerEye, Vector3 targetPoint,
            out string coverTag, out RaycastHit coverHit)
        {
            coverTag = null;
            coverHit = default;
            if (!arena.IsValid() || !arena.isLoaded)
                return false;

            Vector3 delta = targetPoint - observerEye;
            float distance = delta.magnitude;
            int layer = LayerMask.NameToLayer(SceneLayer);
            if (distance < 0.001f || layer < 0)
                return false;

            var physicsScene = arena.GetPhysicsScene();
            Vector3 direction = delta / distance;
            int count = physicsScene.Raycast(observerEye, direction, _hits,
                distance, 1 << layer, QueryTriggerInteraction.Ignore);
            while (count == _hits.Length)
            {
                // A full buffer can omit nearby hits. Retry with more capacity before selecting a hit.
                Array.Resize(ref _hits, _hits.Length * 2);
                count = physicsScene.Raycast(observerEye, direction, _hits,
                    distance, 1 << layer, QueryTriggerInteraction.Ignore);
            }

            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hits[i];
                var collider = hit.collider;
                if (collider == null || collider.gameObject.scene != arena || hit.distance >= nearest)
                    continue;

                string tag = collider.tag;
                if (!IsCoverTag(tag))
                    continue;

                nearest = hit.distance;
                coverTag = tag;
                coverHit = hit;
            }
            return coverTag != null;
        }

        public static bool IsCoverTag(string tag)
        {
            switch (tag)
            {
                case TerrainTag:
                case "ArenaWall":
                case "ArenaTree":
                case "ArenaRock":
                case "ArenaBuilding":
                case "ArenaBox":
                case "ArenaSandbag":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Returns both sample tags so evaluation can attribute cover without guessing its type.</summary>
        public static bool TryGetConcealment(Scene arena, Vector3 observerEye, Vector3 candidateFeet,
            out string torsoCoverTag, out string eyeCoverTag, float torsoHeight = 0.9f, float eyeHeight = 1.55f)
        {
            bool torsoHidden = TryGetOccluder(arena, observerEye, candidateFeet + Vector3.up * torsoHeight,
                out torsoCoverTag, out _);
            bool eyesHidden = TryGetOccluder(arena, observerEye, candidateFeet + Vector3.up * eyeHeight,
                out eyeCoverTag, out _);
            return torsoHidden && eyesHidden;
        }

        public static bool IsHidden(Scene arena, Vector3 observerEye, Vector3 candidateFeet,
            float torsoHeight = 0.9f, float eyeHeight = 1.55f)
        {
            return TryGetConcealment(arena, observerEye, candidateFeet, out _, out _, torsoHeight, eyeHeight);
        }
    }
}
