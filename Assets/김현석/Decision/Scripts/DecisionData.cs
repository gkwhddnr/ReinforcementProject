using System;
using UnityEngine;

namespace Reinforce.Decision
{
    public readonly struct DecisionAction
    {
        public readonly Vector2 Move;
        public readonly int Turn;
        public readonly bool Fire;

        public DecisionAction(int movement, int rotation, int fire)
        {
            if (movement < 0 || movement > 4 || rotation < 0 || rotation > 2 || fire < 0 || fire > 1)
                throw new ArgumentOutOfRangeException(nameof(movement), "Expected branches [5, 3, 2].");

            switch (movement)
            {
                case 1: Move = Vector2.up; break;
                case 2: Move = Vector2.down; break;
                case 3: Move = Vector2.left; break;
                case 4: Move = Vector2.right; break;
                default: Move = Vector2.zero; break;
            }

            Turn = rotation == 1 ? -1 : rotation == 2 ? 1 : 0;
            Fire = fire == 1;
        }
    }

    [Serializable]
    public struct ObservationFrame
    {
        public const int Size = 10;

        public float Health;
        public float MaximumHealth;
        public float PlanarSpeed;
        public float MaximumSpeed;
        public Vector4 CoverDistances;
        public float CoverSensorRange;
        public bool HasTarget;
        public Vector3 TargetLocalPosition;
        public float DetectionRange;

        public void WriteTo(float[] observations)
        {
            if (observations == null || observations.Length != Size)
                throw new ArgumentException("Expected exactly 10 observations.", nameof(observations));

            RequirePositive(MaximumHealth);
            RequirePositive(MaximumSpeed);
            RequirePositive(CoverSensorRange);
            RequirePositive(DetectionRange);
            RequireFinite(Health);
            RequireFinite(PlanarSpeed);
            for (int i = 0; i < 4; i++)
                RequireFinite(CoverDistances[i]);

            // Hidden target coordinates are ignored, even if the provider retained an old value.
            if (HasTarget)
            {
                RequireFinite(TargetLocalPosition.x);
                RequireFinite(TargetLocalPosition.y);
                RequireFinite(TargetLocalPosition.z);
            }

            observations[0] = Mathf.Clamp01(Health / MaximumHealth);
            observations[1] = Mathf.Clamp01(PlanarSpeed / MaximumSpeed);
            for (int i = 0; i < 4; i++)
                observations[2 + i] = Mathf.Clamp01(CoverDistances[i] / CoverSensorRange);

            observations[6] = HasTarget ? 1f : 0f;
            observations[7] = HasTarget ? Mathf.Clamp(TargetLocalPosition.x / DetectionRange, -1f, 1f) : 0f;
            observations[8] = HasTarget ? Mathf.Clamp(TargetLocalPosition.y / DetectionRange, -1f, 1f) : 0f;
            observations[9] = HasTarget ? Mathf.Clamp(TargetLocalPosition.z / DetectionRange, -1f, 1f) : 0f;
        }

        private static void RequirePositive(float value)
        {
            RequireFinite(value);
            if (value <= 0f)
                throw new ArgumentOutOfRangeException(nameof(value), "Observation scale must be positive.");
        }

        private static void RequireFinite(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentException("Observation values must be finite.");
        }
    }
}
