using System;
using System.Collections.Generic;
using Reinforcement.Shooting;
using UnityEngine;

namespace Reinforce.Training
{
    [Serializable]
    public struct PlayerState
    {
        public int ArenaEpisodeId, ActorId, TeamId, CaptureFrame, SourceTargetUpdateTick;
        public double CaptureFixedTime;
        public Vector3 Position, Velocity, TargetAimPoint, MuzzlePosition;
        public Quaternion Rotation;
        public float MaximumSpeed, Health, MaximumHealth, DetectionRange, EffectiveRange;
        public float CooldownRemaining, CurrentAccuracy, TargetLockDuration;
        public bool Alive, IsCrouching, HasTarget, CanFire;
        public int TargetId;
    }

    [Serializable]
    public struct ShotSample
    {
        public int EpisodeId, Seed, ShooterId, TeamId, Sequence, TargetId, HitActorId;
        public double Time;
        public Vector3 Origin, Direction, ContactPoint;
        public float ContactDistance, AppliedDamage;
        public ContactKind ContactKind;
        public HitRegion Region;
        public string ObstacleId;
        public bool Killed, MountBlocked, AimedAtTarget;

        public static ShotSample From(ShotResult shot)
        {
            return new ShotSample
            {
                EpisodeId = shot.EpisodeId, Seed = shot.Seed, ShooterId = shot.ShooterId,
                TeamId = shot.TeamId, Sequence = shot.Sequence, TargetId = shot.TargetId,
                HitActorId = shot.HitActorId ?? -1, Time = shot.Time,
                Origin = shot.Origin, Direction = shot.Direction,
                ContactPoint = shot.Contact.Point, ContactDistance = shot.Contact.Distance,
                ContactKind = shot.Contact.Kind, ObstacleId = shot.Contact.ObstacleId,
                Region = shot.Region, AppliedDamage = shot.Damage.Applied,
                Killed = shot.Damage.Killed, MountBlocked = shot.MountBlocked,
                AimedAtTarget = shot.AimedAtTarget
            };
        }
    }

    public sealed class CombatTelemetryBuffer
    {
        private readonly List<ShotSample> shots = new List<ShotSample>();
        private readonly HashSet<(int shooter, int sequence)> keys = new HashSet<(int, int)>();

        public int EpisodeId { get; private set; }
        public IReadOnlyList<ShotSample> Shots { get; }

        public CombatTelemetryBuffer()
        {
            Shots = shots.AsReadOnly();
        }

        public void BeginEpisode(int episodeId)
        {
            if (episodeId < 1)
                throw new ArgumentOutOfRangeException(nameof(episodeId));
            if (EpisodeId == episodeId)
                return;
            EpisodeId = episodeId;
            shots.Clear();
            keys.Clear();
        }

        public bool TryRecord(ShotResult shot)
        {
            if (shot.EpisodeId != EpisodeId || EpisodeId == 0 || shot.Sequence < 1)
                return false;
            if (double.IsNaN(shot.Time) || double.IsInfinity(shot.Time) || shot.Time < 0d ||
                float.IsNaN(shot.Damage.Applied) || float.IsInfinity(shot.Damage.Applied) || shot.Damage.Applied < 0f)
                throw new ArgumentException("Shot time and applied damage must be finite and nonnegative.", nameof(shot));
            if (!keys.Add((shot.ShooterId, shot.Sequence)))
                return false;
            shots.Add(ShotSample.From(shot));
            return true;
        }
    }
}
