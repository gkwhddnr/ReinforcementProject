using System;
using System.Collections.Generic;
using UnityEngine;

namespace Reinforcement.Shooting
{
    // Trial tuning from the design brief. Map/vision and health owners supply their own settings.
    [Serializable]
    public sealed class ShootingSettings
    {
        [Min(0.01f)] public float range = 20f;
        [Min(0f)] public float interval = 0.2f;
        [Min(0f)] public float damage = 20f;
        [Range(0f, 1f)] public float accuracy = 0.7f;
        public LayerMask mask = Physics.DefaultRaycastLayers;
        [Min(0f)] public float tracerSeconds = 0.05f;

        public bool IsHitRoll(double roll) => roll < accuracy;

        internal bool Valid => range > 0f && !float.IsInfinity(range)
            && interval >= 0f && !float.IsInfinity(interval)
            && damage >= 0f && !float.IsInfinity(damage)
            && accuracy >= 0f && accuracy <= 1f && mask.value != 0;
    }

    // The provider owns the candidate list until Tick returns. The observed aim points
    // belong to this frame, so moving a target cannot reveal a new aim point through an old frame.
    public readonly struct VisibilityFrame
    {
        public readonly int EpisodeId;
        public readonly int Tick;
        public readonly IReadOnlyList<ShotActor> Candidates;
        private readonly Vector3 firstPoint;
        private readonly Vector3[] otherPoints;

        public VisibilityFrame(int episodeId, int tick, IReadOnlyList<ShotActor> candidates)
        {
            EpisodeId = episodeId;
            Tick = tick;
            Candidates = candidates;
            int count = candidates?.Count ?? 0;
            firstPoint = count > 0 && candidates[0] ? candidates[0].AimPoint : default;
            otherPoints = count > 1 ? new Vector3[count - 1] : null;
            for (int i = 1; i < count; i++)
                otherPoints[i - 1] = candidates[i] ? candidates[i].AimPoint : default;
        }

        public Vector3 ObservedPoint(int index) => index == 0 ? firstPoint : otherPoints[index - 1];
    }

    public struct DamageRequest
    {
        public int EpisodeId, Sequence, ShooterId, TeamId, TargetId;
        public double Time;
        public Vector3 Point;
        public HitRegion Region;
        public float Amount;
    }

    public readonly struct DamageReceipt
    {
        public readonly float Applied;
        public readonly bool Killed;

        public DamageReceipt(float applied, bool killed)
        {
            Applied = applied;
            Killed = killed;
        }
    }

    public enum ShotStatus
    {
        Inactive, InvalidSetup, ShooterDead, StaleVision, NoTarget, Aiming, Cooldown,
        OverlappingActor, MissDirectionUnavailable, Fired
    }

    // Evaluation event. Policy observations and rewards are owned by the learning system.
    public struct ShotResult
    {
        public int EpisodeId, Seed, ShooterId, TeamId, Sequence, TargetId;
        public double Time;
        public Vector3 Origin, Direction;
        public bool AimedAtTarget, MountBlocked;
        public ShotContact Contact;
        public int? HitActorId;
        public HitRegion Region;
        public DamageReceipt Damage;
    }

    public struct ShotLineQuery
    {
        public bool Valid, OutOfRange, MountBlocked;
        public double Time;
        public ShotContact Contact;
    }
}
