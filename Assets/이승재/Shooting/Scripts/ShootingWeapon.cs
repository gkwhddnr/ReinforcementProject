using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Reinforcement.Shooting
{
    [DisallowMultipleComponent]
    public sealed class ShootingWeapon : MonoBehaviour
    {
        [SerializeField] private ShotActor owner;
        [SerializeField] private Transform eye, mount, muzzle;
        [SerializeField] private ShootingSettings settings = new ShootingSettings();

        private int episode, shotSeed, sequence;
        // Shared by weapons in this simulation, including replacements spawned mid-episode.
        private static long lastIssuedShotId;
        private int targetUpdateTick = -1, observedTargetId = -1;
        private Vector3 observedAimPoint;
        private float maxDetectionDistance = VisibilityFrame.TrialDetectionDistance;
        private bool hasCurrentVision;
        private readonly ShotClock clock = new ShotClock();
        private bool active, held;
        private System.Random random;
        private Quaternion restRotation;

        public ShotActor Target { get; private set; }
        public ShotStatus Status { get; private set; }
        public ShotResult LastShot { get; private set; }
        public int MissDirectionFailures { get; private set; }
        public event Action<ShotResult> ShotResolved;
        public event Action<ShotFiredEvent> ShotFired;
        public event Action<ShotHitEvent> ShotHit;

        private void Awake()
        {
            if (muzzle) restRotation = muzzle.localRotation;
        }

        public void Configure(ShotActor owner, Transform eye, Transform mount, Transform muzzle,
            ShootingSettings settings)
        {
            EndEpisode();
            this.owner = owner;
            this.eye = eye;
            this.mount = mount;
            this.muzzle = muzzle;
            this.settings = settings;
            if (muzzle) restRotation = muzzle.localRotation;
        }

        public void ResetEpisode(int episodeId) =>
            ResetEpisode(episodeId, unchecked(episodeId * 397 ^ (owner ? owner.ActorId : 0)));

        public void ResetEpisode(int episodeId, int seed)
        {
            EndEpisode();
            episode = episodeId;
            shotSeed = seed;
            random = new System.Random(seed);
            active = true;
            sequence = 0;
            LastShot = default;
            MissDirectionFailures = 0;
            Status = ShotStatus.NoTarget;
        }

        private void OnDisable() => EndEpisode();

        public void EndEpisode()
        {
            active = false;
            held = false;
            hasCurrentVision = false;
            targetUpdateTick = -1;
            maxDetectionDistance = VisibilityFrame.TrialDetectionDistance;
            clock.Reset();
            ClearTarget();
            Status = ShotStatus.Inactive;
        }

        public void SetCommand(bool fireHeld) => SetCommand(episode, fireHeld);

        public void SetCommand(int episodeId, bool fireHeld)
        {
            if (active && episodeId == episode)
                held = fireHeld && owner && owner.isActiveAndEnabled && owner.Alive;
        }

        public double RemainingCooldown(double time) => active ? clock.Remaining(time) : 0d;

        private bool Ready => active && isActiveAndEnabled && ValidRig()
            && owner.isActiveAndEnabled && owner.Alive && hasCurrentVision;

        private bool HasObservedTarget() => observedTargetId != -1 && Target && Target.isActiveAndEnabled
            && Target.Alive && Target.ActorId == observedTargetId && Target.TeamId != owner.TeamId;

        public ShootingState GetState()
        {
            bool ready = Ready;
            // Report the selection from the last consumed sight frame. A later move must
            // neither replace that point nor disclose movement through a read between ticks.
            bool hasTarget = ready && HasObservedTarget();
            return new ShootingState
            {
                IsReady = ready, HasTarget = hasTarget,
                TargetId = hasTarget ? observedTargetId : -1,
                AimPoint = hasTarget ? observedAimPoint : Vector3.zero,
                TargetUpdateTick = targetUpdateTick, MaxDetectionDistance = maxDetectionDistance,
                CanFire = hasTarget && CanFireAt(clock.Time),
                Origin = muzzle ? muzzle.position : Vector3.zero,
                EffectiveRange = settings?.range ?? 0f
            };
        }

        public bool CanFire(double time) => Ready && HasObservedTarget() && CanFireAt(time);

        private bool CanFireAt(double time)
        {
            Vector3 offset = observedAimPoint - muzzle.position;
            if (!(time >= clock.Time) || double.IsInfinity(time) || RemainingCooldown(time) > 0
                || offset == Vector3.zero || offset.sqrMagnitude > settings.range * settings.range)
                return false;
            if (time == clock.Time && (Status == ShotStatus.Fired || Status == ShotStatus.MissDirectionUnavailable))
                return false;
            bool blocked = ShotPhysics.CheckMount(owner, mount.position, muzzle.position, settings.mask,
                out var contact);
            return !(blocked && contact.Kind == ContactKind.Actor);
        }

        public void Tick(int episodeId, int tick, double time, VisibilityFrame vision)
        {
            if (!active || !isActiveAndEnabled || episodeId != episode || !clock.BeginTick(tick, time)) return;
            targetUpdateTick = tick;
            hasCurrentVision = false;
            if (!ValidRig())
            {
                held = false;
                ClearTarget();
                Status = ShotStatus.InvalidSetup;
                return;
            }
            if (!owner.isActiveAndEnabled || !owner.Alive)
            {
                EndEpisode();
                Status = ShotStatus.ShooterDead;
                return;
            }
            if (vision.EpisodeId != episode || vision.Tick != tick || vision.Candidates == null
                || !(vision.MaxDetectionDistance > 0f) || float.IsInfinity(vision.MaxDetectionDistance))
            {
                ClearTarget();
                Status = ShotStatus.StaleVision;
                return;
            }

            hasCurrentVision = true;
            maxDetectionDistance = vision.MaxDetectionDistance;
            Target = SelectTarget(vision, out observedAimPoint);
            if (!Target)
            {
                ClearTarget();
                Status = ShotStatus.NoTarget;
                return;
            }
            observedTargetId = Target.ActorId;
            Vector3 origin = muzzle.position;
            Vector3 direction = (observedAimPoint - origin).normalized;
            if (direction == Vector3.zero)
            {
                Status = ShotStatus.OverlappingActor;
                return;
            }
            AlignMuzzle(direction);
            if (RemainingCooldown(time) > 0)
            {
                Status = ShotStatus.Cooldown;
                return;
            }
            Status = ShotStatus.Aiming;
            if (!held) return;

            bool blocked = ShotPhysics.CheckMount(owner, mount.position, origin, settings.mask, out var contact);
            if (blocked && contact.Kind == ContactKind.Actor)
            {
                Status = ShotStatus.OverlappingActor;
                return;
            }
            bool aimed = settings.IsHitRoll(random.NextDouble());
            if (!aimed && !ShotPhysics.ClearMissDirection(Target, origin, observedAimPoint, settings.range,
                settings.mask, random, out direction))
            {
                MissDirectionFailures++;
                Status = ShotStatus.MissDirectionUnavailable;
                return;
            }
            AlignMuzzle(direction);
            if (!blocked) contact = ShotPhysics.Trace(owner, origin, muzzle.forward, settings.range, settings.mask);

            var firingEpisodeRng = random;
            var shot = new ShotResult
            {
                ShotId = System.Threading.Interlocked.Increment(ref lastIssuedShotId),
                EpisodeId = episode, Seed = shotSeed, Sequence = ++sequence, Time = time,
                ShooterId = owner.ActorId, TeamId = owner.TeamId, TargetId = observedTargetId,
                Origin = origin, Direction = muzzle.forward, Contact = contact,
                MountBlocked = blocked, AimedAtTarget = aimed
            };
            clock.Commit(settings.interval);
            Status = ShotStatus.Fired;
            if (contact.Actor)
            {
                ShotActor hit = contact.Actor;
                shot.HitActorId = hit.ActorId;
                shot.Region = hit.RegionAt(contact.Point);
                // Trial rule: allies obstruct the line. Only living opposing actors accept damage.
                if (hit.TeamId != shot.TeamId && hit.isActiveAndEnabled && hit.Alive && shot.Region != HitRegion.None)
                {
                    shot.Damage = hit.ReceiveDamage(new DamageRequest
                    {
                        ShotId = shot.ShotId, EpisodeId = shot.EpisodeId, Sequence = shot.Sequence, Time = time,
                        ShooterId = shot.ShooterId, TeamId = shot.TeamId, TargetId = hit.ActorId,
                        Region = shot.Region, Point = contact.Point, Amount = settings.damage
                    });
                }
            }
            // A health callback can synchronously start a new episode.
            if (random == firingEpisodeRng) LastShot = shot;
            shot.Notify(ShotFired, ShotHit);
            ShotResolved?.Invoke(shot);
        }

        // Evaluation query. The caller supplies its permitted point; this does not acquire a target.
        public ShotLineQuery QueryShotLine(Vector3 targetPoint, double time)
        {
            var query = new ShotLineQuery { Time = time };
            if (!ValidRig()) return query;
            Vector3 origin = muzzle.position;
            Vector3 delta = targetPoint - origin;
            float distance = delta.magnitude;
            if (!(distance > 0f) || float.IsInfinity(distance)) return query;
            query.Valid = true;
            query.OutOfRange = distance > settings.range;
            query.MountBlocked = ShotPhysics.CheckMount(owner, mount.position, origin, settings.mask,
                out query.Contact);
            if (!query.MountBlocked)
                query.Contact = ShotPhysics.Trace(owner, origin, delta, Mathf.Min(distance, settings.range),
                    settings.mask);
            return query;
        }

        private bool ValidRig() => owner && eye && mount && muzzle && muzzle != owner.transform
            && muzzle != mount && muzzle != eye && muzzle.IsChildOf(owner.transform)
            && settings != null && settings.Valid;

        private bool Eligible(ShotActor actor)
        {
            if (!actor || actor == owner || actor.ActorId == -1 || !actor.isActiveAndEnabled || !actor.Alive
                || actor.TeamId == owner.TeamId
                || actor.gameObject.scene.GetPhysicsScene() != owner.gameObject.scene.GetPhysicsScene())
                return false;
            var capsule = actor.Capsule;
            return capsule && capsule.enabled && capsule.gameObject.activeInHierarchy && !capsule.isTrigger
                && (settings.mask.value & (1 << capsule.gameObject.layer)) != 0
                && (actor.AimPoint - muzzle.position).sqrMagnitude <= settings.range * settings.range;
        }

        private ShotActor SelectTarget(VisibilityFrame vision, out Vector3 aimPoint)
        {
            aimPoint = default;
            ShotActor nearest = null;
            float nearestSq = float.PositiveInfinity;
            for (int i = 0; i < vision.Candidates.Count; i++)
            {
                var candidate = vision.Candidates[i];
                if (!Eligible(candidate)) continue;
                Vector3 observed = vision.ObservedPoint(i);
                if (candidate.AimPoint != observed) continue;
                Vector3 offset = observed - eye.position;
                if (offset.x * offset.x + offset.z * offset.z > vision.MaxDetectionDistance * vision.MaxDetectionDistance)
                    continue;
                float distanceSq = offset.sqrMagnitude;
                if (distanceSq < nearestSq || (distanceSq == nearestSq && nearest && candidate.ActorId < nearest.ActorId))
                {
                    nearest = candidate;
                    nearestSq = distanceSq;
                    aimPoint = observed;
                }
            }
            return nearest;
        }

        private void AlignMuzzle(Vector3 direction)
        {
            Vector3 up = owner.transform.up;
            if (Mathf.Abs(Vector3.Dot(direction, up)) > 0.999f) up = owner.transform.right;
            muzzle.rotation = Quaternion.LookRotation(direction, up);
        }

        private void ClearTarget()
        {
            Target = null;
            observedTargetId = -1;
            observedAimPoint = Vector3.zero;
            if (muzzle && owner && muzzle != mount && muzzle != eye && muzzle != owner.transform
                && muzzle.IsChildOf(owner.transform))
                muzzle.localRotation = restRotation;
        }
    }
}
