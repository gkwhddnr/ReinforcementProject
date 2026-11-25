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
        [Tooltip("Use -1 to apply the body damage to head hits.")] public float headDamage = -1f;
        [Range(0f, 1f)] public float accuracy = 0.7f;
        [Tooltip("Fraction of accurate shots that try to aim at the head.")]
        [Range(0f, 1f)] public float headAimChance;
        public LayerMask mask = Physics.DefaultRaycastLayers;
        [Min(0f)] public float tracerSeconds = 0.05f;

        [Header("Dynamic Human Accuracy")]
        [Tooltip("인간 유사 동적 명중률 시스템 활성화 여부 (반응 지연 및 조준 유지 시간에 따른 명중률 곡선 적용)")]
        public bool useDynamicAccuracy = true;
        [Tooltip("적 식별 후 최초 사격까지의 반응 지연 시간 (초 단위, 0.2~0.25초)")]
        [Range(0.1f, 0.5f)] public float reactionDelay = 0.22f;
        [Tooltip("반응 지연 직후 기본 명중률 (0.25 = 25%)")]
        [Range(0f, 1f)] public float baseAccuracy = 0.25f;
        [Tooltip("조준 지속 시 최대 수렴 명중률 (0.80 = 80%)")]
        [Range(0f, 1f)] public float maxAccuracy = 0.80f;
        [Tooltip("조준 집중 수렴 속도 (클수록 초반 상승폭이 큼)")]
        [Min(0.1f)] public float aimConvergence = 3.8f;

        public float CalculateDynamicAccuracy(double lockDuration)
        {
            if (lockDuration < reactionDelay) return 0f;
            float dt = (float)(lockDuration - reactionDelay);
            // 위로 볼록한 지수 포화 곡선: base + (max - base) * (1 - e^(-k * dt))
            return Mathf.Clamp01(baseAccuracy + (maxAccuracy - baseAccuracy) * (1f - Mathf.Exp(-aimConvergence * dt)));
        }

        public bool IsHitRoll(double roll) => roll < accuracy;

        public bool IsHitRoll(double roll, double lockDuration)
        {
            if (!useDynamicAccuracy) return roll < accuracy;
            if (lockDuration < reactionDelay) return false;
            return roll < CalculateDynamicAccuracy(lockDuration);
        }

        internal float DamageFor(HitRegion region) => region == HitRegion.Head && headDamage >= 0f ? headDamage : damage;

        internal bool Valid => range > 0f && !float.IsInfinity(range)
            && interval >= 0f && !float.IsInfinity(interval)
            && damage >= 0f && !float.IsInfinity(damage)
            && (headDamage == -1f || (headDamage >= 0f && !float.IsInfinity(headDamage)))
            && accuracy >= 0f && accuracy <= 1f
            && headAimChance >= 0f && headAimChance <= 1f && mask.value != 0
            && (!useDynamicAccuracy || (reactionDelay >= 0f && baseAccuracy >= 0f && maxAccuracy >= baseAccuracy && aimConvergence > 0f));
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
