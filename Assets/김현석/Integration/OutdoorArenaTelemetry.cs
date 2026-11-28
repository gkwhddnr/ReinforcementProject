using System;
using Reinforce.Integration;
using Reinforce.Movement;
using Reinforce.Training;
using Reinforcement.Shooting;
using ReinProject.Landscape;
using UnityEngine;

namespace Reinforce.LearningIntegration
{
    // Assembly-CSharp can reference both the owners' integration code and the named training assembly.
    [DisallowMultipleComponent]
    public sealed class OutdoorArenaTelemetry : MonoBehaviour
    {
        [SerializeField] private ArenaSpawnManager arena;
        [SerializeField] private bool hasFirstState, hasSecondState;
        [SerializeField] private PlayerState firstState, secondState;
        [SerializeField] private int recordedShots;
        [SerializeField] private ShotSample lastShot;

        private readonly CombatTelemetryBuffer buffer = new CombatTelemetryBuffer();
        private ShootingWeapon firstWeapon, secondWeapon;

        public CombatTelemetryBuffer Buffer => buffer;
        public bool HasFirstState => hasFirstState;
        public bool HasSecondState => hasSecondState;
        public PlayerState FirstState => firstState;
        public PlayerState SecondState => secondState;

        public void Bind(ArenaSpawnManager source)
        {
            if (arena != source && buffer.EpisodeId != 0)
                throw new InvalidOperationException("Use a new telemetry component when changing arenas.");
            Unsubscribe();
            arena = source;
            CaptureLatest();
        }

        private void OnEnable() => CaptureLatest();
        private void LateUpdate() => CaptureLatest();

        public void CaptureLatest()
        {
            if (!isActiveAndEnabled || !arena || !arena.isActiveAndEnabled || arena.CurrentEpisode < 1)
            {
                Unsubscribe();
                hasFirstState = hasSecondState = false;
                return;
            }

            buffer.BeginEpisode(arena.CurrentEpisode);
            Subscribe(ref firstWeapon, arena.CombatantA ? arena.CombatantA.Weapon : null);
            Subscribe(ref secondWeapon, arena.CombatantB ? arena.CombatantB.Weapon : null);
            hasFirstState = TryRead(arena.CombatantA, arena.CurrentEpisode, out firstState);
            hasSecondState = TryRead(arena.CombatantB, arena.CurrentEpisode, out secondState);
            UpdateShotPreview();
        }

        private static bool TryRead(PlayerCombatIntegration combatant, int episodeId, out PlayerState state)
        {
            state = default;
            if (!combatant || !combatant.isActiveAndEnabled || !combatant.Actor ||
                !combatant.Actor.isActiveAndEnabled || !combatant.Movement ||
                !combatant.Movement.isActiveAndEnabled || !combatant.IsMovementReady || !combatant.IsWeaponReady)
                return false;

            bool hasTarget = combatant.HasTarget;
            state = new PlayerState
            {
                ArenaEpisodeId = episodeId, ActorId = combatant.Actor.ActorId, TeamId = combatant.Actor.TeamId,
                CaptureFrame = Time.frameCount, CaptureFixedTime = Time.fixedTimeAsDouble,
                SourceTargetUpdateTick = combatant.TargetUpdateTick,
                Position = combatant.Position, Rotation = combatant.Rotation, Velocity = combatant.Velocity,
                MaximumSpeed = combatant.MaximumSpeed,
                IsCrouching = combatant.Posture == PostureState.Crouching,
                Health = combatant.Actor.Health, MaximumHealth = combatant.Actor.MaxHealth,
                Alive = combatant.Actor.Alive, HasTarget = hasTarget,
                TargetId = hasTarget ? combatant.TargetId : -1,
                TargetAimPoint = hasTarget ? combatant.TargetAimPoint : Vector3.zero,
                DetectionRange = combatant.MaxDetectionRange, EffectiveRange = combatant.EffectiveRange,
                MuzzlePosition = combatant.MuzzlePosition, CanFire = combatant.CanFire,
                CooldownRemaining = combatant.CooldownRemaining,
                CurrentAccuracy = combatant.CurrentAccuracy, TargetLockDuration = combatant.TargetLockDuration
            };
            return true;
        }

        private void Subscribe(ref ShootingWeapon previous, ShootingWeapon next)
        {
            if (previous == next)
                return;
            if (previous)
                previous.ShotResolved -= RecordShot;
            previous = next;
            if (previous)
                previous.ShotResolved += RecordShot;
        }

        private void RecordShot(ShotResult shot)
        {
            if (!isActiveAndEnabled || !arena || !arena.isActiveAndEnabled || arena.CurrentEpisode < 1)
                return;
            buffer.BeginEpisode(arena.CurrentEpisode);
            buffer.TryRecord(shot);
            UpdateShotPreview();
        }

        private void UpdateShotPreview()
        {
            recordedShots = buffer.Shots.Count;
            lastShot = recordedShots == 0 ? default : buffer.Shots[recordedShots - 1];
        }

        private void Unsubscribe()
        {
            Subscribe(ref firstWeapon, null);
            Subscribe(ref secondWeapon, null);
        }

        private void OnDisable()
        {
            Unsubscribe();
            hasFirstState = hasSecondState = false;
        }
    }
}
