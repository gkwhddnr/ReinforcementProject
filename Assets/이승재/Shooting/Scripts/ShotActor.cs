using UnityEngine;

namespace Reinforcement.Shooting
{
    public enum HitRegion { None, Body, Head }

    // A local adapter boundary. The health owner supplies the actual damage receipt.
    public abstract class ShotActor : MonoBehaviour
    {
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

        public HitRegion RegionAt(Vector3 hitPoint)
        {
            var shape = Capsule;
            if (!shape) return HitRegion.None;

            int axisIndex = shape.direction;
            Vector3 axis = Vector3.zero;
            axis[axisIndex] = 1f;
            Vector3 worldAxis = shape.transform.TransformVector(axis).normalized;
            Vector3 scale = shape.transform.lossyScale;
            float radiusScale = Mathf.Max(Mathf.Abs(scale[(axisIndex + 1) % 3]),
                Mathf.Abs(scale[(axisIndex + 2) % 3]));
            // A scaled Unity capsule cannot be shorter than its world-space diameter.
            float height = Mathf.Max(shape.height * Mathf.Abs(scale[axisIndex]), 2f * shape.radius * radiusScale);
            if (height <= 0f) return HitRegion.None;
            float aboveCenter = Vector3.Dot(hitPoint - AimPoint, worldAxis);
            return aboveCenter >= height * (0.5f - 1f / 7f) ? HitRegion.Head : HitRegion.Body;
        }
    }
}
