using UnityEngine;

namespace Reinforcement.Shooting
{
    public enum HitRegion { None, Body, Head }

    // A local adapter boundary. The health owner supplies the actual damage receipt.
    public abstract class ShotActor : MonoBehaviour
    {
        private const float HeadHeightFraction = 1f / 7f;

        [SerializeField] private CapsuleCollider capsule;

        public abstract int ActorId { get; }
        public abstract int TeamId { get; }
        public abstract bool Alive { get; }
        public abstract DamageReceipt ReceiveDamage(DamageRequest request);

        public CapsuleCollider Capsule
        {
            get
            {
                if (!capsule) capsule = GetComponent<CapsuleCollider>();
                return capsule;
            }
            set => capsule = value;
        }

        public Vector3 AimPoint
        {
            get
            {
                var shape = Capsule;
                return shape ? shape.transform.TransformPoint(shape.center) : transform.position;
            }
        }

        internal Vector3 HeadAimPoint
        {
            get
            {
                return TryGetCapsuleAxis(out Vector3 axis, out float height)
                    ? AimPoint + axis * (height * (0.5f - HeadHeightFraction * 0.5f))
                    : AimPoint;
            }
        }

        public HitRegion RegionAt(Vector3 hitPoint)
        {
            if (!TryGetCapsuleAxis(out Vector3 axis, out float height))
                return HitRegion.None;

            float aboveCenter = Vector3.Dot(hitPoint - AimPoint, axis);
            return aboveCenter >= height * (0.5f - HeadHeightFraction) ? HitRegion.Head : HitRegion.Body;
        }

        private bool TryGetCapsuleAxis(out Vector3 worldAxis, out float height)
        {
            worldAxis = Vector3.zero;
            height = 0f;
            var shape = Capsule;
            if (!shape)
                return false;

            int axisIndex = shape.direction;
            Vector3 axis = Vector3.zero;
            axis[axisIndex] = 1f;
            worldAxis = shape.transform.TransformVector(axis).normalized;
            Vector3 scale = shape.transform.lossyScale;
            float radiusScale = Mathf.Max(Mathf.Abs(scale[(axisIndex + 1) % 3]),
                Mathf.Abs(scale[(axisIndex + 2) % 3]));
            // A scaled Unity capsule cannot be shorter than its world-space diameter.
            height = Mathf.Max(shape.height * Mathf.Abs(scale[axisIndex]), 2f * shape.radius * radiusScale);
            return height > 0f;
        }
    }
}
