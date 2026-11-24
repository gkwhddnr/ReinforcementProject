using UnityEngine;

namespace ReinProject.Landscape
{
    internal readonly struct SandbagArc
    {
        internal const float MaxAngle = 150f;
        internal const float ColliderDepthPadding = .025f;

        private readonly Vector3 _center;
        private readonly float _halfSpan;
        private readonly float _halfAngle;
        private readonly float _direction;
        private readonly float _radius;

        internal SandbagArc(BoxCollider shape, float angle) : this(shape.center, shape.size.x, angle)
        {
        }

        internal SandbagArc(Vector3 center, float span, float angle)
        {
            _center = center;
            _halfSpan = span * 0.5f;
            _halfAngle = Mathf.Abs(angle) * 0.5f * Mathf.Deg2Rad;
            _direction = Mathf.Sign(angle);
            _radius = _halfAngle > 0f ? _halfSpan / Mathf.Sin(_halfAngle) : 0f;
        }

        internal static bool TryReadFootprint(Transform target, out Vector3 center, out Vector3 size, out float angle)
        {
            center = default;
            size = default;
            angle = 0f;
            if (!SandbagSplineBake.IsBakedArc(target))
                return false;

            BoxCollider first = null;
            BoxCollider last = null;
            int count = 0;
            foreach (Transform child in target)
            {
                if (!child.TryGetComponent(out BoxCollider section) || !section.enabled || section.isTrigger)
                    continue;

                first ??= section;
                last = section;
                count++;
            }

            if (count == 0)
                return false;

            Vector3 start = first.transform.localPosition;
            center = start;
            size = first.size;
            size.z = Mathf.Max(.01f, size.z - ColliderDepthPadding);
            if (count == 1)
                return true;

            // Baked segments are equally spaced by angle. The outermost centers sit half a segment
            // inside each end, so recover the full chord from their span and tangent directions.
            Vector3 end = last.transform.localPosition;
            float sweep = Mathf.DeltaAngle(first.transform.localEulerAngles.y, last.transform.localEulerAngles.y);
            angle = sweep * count / (count - 1f);
            if (Mathf.Abs(sweep) < .01f || Mathf.Abs(angle) > MaxAngle + .1f || end.x <= start.x)
                return false;

            float halfSweep = Mathf.Abs(sweep) * .5f * Mathf.Deg2Rad;
            float halfAngle = Mathf.Abs(angle) * .5f * Mathf.Deg2Rad;
            float radius = (end.x - start.x) / (2f * Mathf.Sin(halfSweep));
            center = (start + end) * .5f;
            center.z -= Mathf.Sign(angle) * radius * (Mathf.Cos(halfSweep) - Mathf.Cos(halfAngle));
            size.x = 2f * radius * Mathf.Sin(halfAngle);
            return true;
        }

        internal Vector3 Map(Vector3 point) => Map(point, out _, out _);

        internal Vector3 Map(Vector3 point, out Quaternion rotation, out float lengthScale)
        {
            rotation = Quaternion.identity;
            lengthScale = 1f;
            if (_halfAngle == 0f)
                return point;

            float angle = (point.x - _center.x) / _halfSpan * _halfAngle;
            float depth = point.z - _center.z;
            float offsetRadius = _radius + _direction * depth;
            rotation = Quaternion.Euler(0f, _direction * angle * Mathf.Rad2Deg, 0f);
            lengthScale = offsetRadius * _halfAngle / _halfSpan;
            return new Vector3(
                _center.x + offsetRadius * Mathf.Sin(angle),
                point.y,
                _center.z + _direction * (_radius * (Mathf.Cos(angle) - Mathf.Cos(_halfAngle))) +
                depth * Mathf.Cos(angle));
        }

        internal float LengthMultiplier => _halfAngle > 0f ? _halfAngle / Mathf.Sin(_halfAngle) : 1f;
    }
}
