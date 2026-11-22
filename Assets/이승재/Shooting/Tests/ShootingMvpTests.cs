using System.Collections.Generic;
using NUnit.Framework;
using Reinforcement.Shooting.Demo;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Reinforcement.Shooting.Tests
{
    public sealed class ShootingMvpTests
    {
        private const int Episode = 17, Seed = 20261120, Layer = 30, Mask = 1 << Layer;
        private Scene previous, scene;
        private Transform root, eye, mount, muzzle;
        private RecordingShotActor owner, target;
        private ShootingSettings settings;
        private ShootingWeapon weapon;
        private TrialVision vision;
        private readonly List<ShotResult> shots = new List<ShotResult>();

        [SetUp]
        public void SetUp()
        {
            previous = SceneManager.GetActiveScene();
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                root = new GameObject("Shooting test fixture").transform;
                root.position = new Vector3(1024f, 512f, -512f);
                owner = Actor(1, 1, Vector3.zero);
                target = Actor(2, 2, Vector3.forward * 8);
                eye = Child(owner.transform, "Eye", new Vector3(0f, 1.65f, 0f));
                mount = Child(owner.transform, "Mount", new Vector3(0.45f, 0.9f, 0f));
                muzzle = Child(mount, "Muzzle", new Vector3(0.2f, 0f, 0.55f));
                settings = new ShootingSettings { accuracy = 1f, mask = Mask, tracerSeconds = 0f };
                weapon = owner.gameObject.AddComponent<ShootingWeapon>();
                weapon.Configure(owner, eye, mount, muzzle, settings);
                weapon.ResetEpisode(Episode, Seed);
                shots.Clear();
                weapon.ShotResolved += shots.Add;
                vision = owner.gameObject.AddComponent<TrialVision>();
                vision.Configure(owner, eye, target);
                Physics.SyncTransforms();
            }
            catch
            {
                TearDown();
                throw;
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (weapon) weapon.ShotResolved -= shots.Add;
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            shots.Clear();
        }

        private static Transform Child(Transform parent, string name, Vector3 localPosition)
        {
            var child = new GameObject(name).transform;
            child.gameObject.layer = Layer;
            child.SetParent(parent, false);
            child.localPosition = localPosition;
            return child;
        }

        private RecordingShotActor Actor(int id, int team, Vector3 localPosition)
        {
            var entity = Child(root, $"Actor {id}", localPosition).gameObject;
            var capsule = entity.AddComponent<CapsuleCollider>();
            capsule.center = Vector3.up;
            capsule.height = 2f;
            capsule.radius = 0.5f;
            var actor = entity.AddComponent<RecordingShotActor>();
            actor.id = id;
            actor.team = team;
            actor.Capsule = capsule;
            return actor;
        }

        private static BoxCollider Box(Transform parent, Vector3 localPosition, Vector3 size)
        {
            var shape = Child(parent, "Box", localPosition).gameObject.AddComponent<BoxCollider>();
            shape.size = size;
            return shape;
        }

        private void Tick(int tick, double time, params ShotActor[] candidates) =>
            weapon.Tick(Episode, tick, time, new VisibilityFrame(Episode, tick, candidates));

        private void Hold() => weapon.SetCommand(Episode, true);

        private static void SamePoint(Vector3 a, Vector3 b) =>
            Assert.That(Vector3.Distance(a, b), Is.LessThan(0.001f));

        [Test]
        public void AutoAimNeedsCommandAndAppliesOnlyTheFirstDamageReceipt()
        {
            // A second hit shape belongs to the same receiver. Owner shapes and sensors precede it.
            Box(target.transform, Vector3.up, Vector3.one);
            Box(owner.transform, new Vector3(0.6f, 1f, 2f), Vector3.one);
            Box(root, new Vector3(0.5f, 1f, 3f), Vector3.one).isTrigger = true;
            Physics.SyncTransforms();
            Tick(0, 0, target);
            Assert.That(weapon.Target, Is.SameAs(target));
            Assert.That(shots, Is.Empty);
            Assert.That(target.Requests, Is.Zero);
            SamePoint(muzzle.forward, (target.AimPoint - muzzle.position).normalized);

            target.health = 5f;
            Hold();
            Tick(1, 0.02, target);
            Assert.That(shots.Count, Is.EqualTo(1));
            ShotResult hit = shots[0];
            Assert.That(hit.AimedAtTarget, Is.True);
            Assert.That(hit.Contact.Actor, Is.SameAs(target));
            Assert.That(hit.Region, Is.EqualTo(HitRegion.Body));
            Assert.That(hit.ShooterId, Is.EqualTo(owner.ActorId));
            Assert.That(hit.TargetId, Is.EqualTo(target.ActorId));
            Assert.That(hit.Sequence, Is.EqualTo(1));
            Assert.That(hit.Damage.Applied, Is.EqualTo(5f));
            Assert.That(hit.Damage.Killed, Is.True);
            Assert.That(target.LastRequest.Amount, Is.EqualTo(settings.damage));
            Assert.That(target.Requests, Is.EqualTo(1));
            Assert.That(owner.Requests, Is.Zero);
            SamePoint(hit.Origin, muzzle.position);
            SamePoint(hit.Direction, muzzle.forward);
        }

        [Test]
        public void ForcedMissPhysicallyClearsCompoundTargetAndHitsABackstop()
        {
            settings.accuracy = 0f;
            Box(target.transform, new Vector3(0.6f, 1.4f, 0f), Vector3.one);
            Box(root, new Vector3(0f, 1f, 13f), new Vector3(16f, 16f, 0.2f))
                .gameObject.AddComponent<ShotObstacle>().ObstacleId = "backstop";
            Physics.SyncTransforms();
            Hold();
            Tick(0, 0, target);
            Assert.That(shots.Count, Is.EqualTo(1));
            Assert.That(shots[0].AimedAtTarget, Is.False);
            Assert.That(shots[0].Contact.ObstacleId, Is.EqualTo("backstop"));
            Assert.That(target.Requests, Is.Zero);
            SamePoint(shots[0].Direction, muzzle.forward);
        }

        [Test]
        public void SeededExposedCapsuleHasExpectedActualContactRate()
        {
            const int attempts = 10000;
            settings.accuracy = 0.7f;
            Assert.That(settings.IsHitRoll(0d), Is.True);
            Assert.That(settings.IsHitRoll(0.6999d), Is.True);
            Assert.That(settings.IsHitRoll(0.7d), Is.False);
            var visible = new ShotActor[] { target };
            Hold();
            for (int i = 0; i < attempts; i++)
                weapon.Tick(Episode, i, i * 0.25d, new VisibilityFrame(Episode, i, visible));
            int hits = shots.FindAll(shot => shot.Contact.Kind == ContactKind.Actor &&
                shot.Contact.Actor == target).Count;
            Debug.Log($"Shooting controlled trial: seed={Seed}, attempts={attempts}, fired={shots.Count}, " +
                $"targetHits={hits}, rejected={attempts - shots.Count}, missDirectionFailures={weapon.MissDirectionFailures}");
            Assert.That(shots.Count, Is.EqualTo(attempts));
            Assert.That(weapon.MissDirectionFailures, Is.Zero);
            Assert.That(hits, Is.InRange(6800, 7200));
            Assert.That(target.Requests, Is.EqualTo(hits));
        }

        [Test]
        public void NarrowMuzzleWallBlocksDespiteClearSight()
        {
            Box(root, new Vector3(0.56f, 0.92f, 1.6f), new Vector3(0.55f, 0.55f, 0.12f))
                .gameObject.AddComponent<ShotObstacle>().ObstacleId = "right-side";
            Physics.SyncTransforms();
            var visible = vision.Sample(Episode, 0, Mask);
            CollectionAssert.Contains(visible.Candidates, target);
            ShotLineQuery query = weapon.QueryShotLine(target.AimPoint, 0);
            Assert.That(query.MountBlocked, Is.False);
            Assert.That(query.Contact.ObstacleId, Is.EqualTo("right-side"));
            Hold();
            weapon.Tick(Episode, 0, 0, visible);
            Assert.That(shots.Count, Is.EqualTo(1));
            Assert.That(shots[0].Contact.ObstacleId, Is.EqualTo(query.Contact.ObstacleId));
            Assert.That(target.Requests, Is.Zero);
        }

        [Test]
        public void MuzzleInsideWallAndWallBetweenMountAndMuzzleBothStopShot()
        {
            var cover = Box(root, root.InverseTransformPoint(muzzle.position), Vector3.one * 0.08f);
            Hold();
            for (int i = 0; i < 2; i++)
            {
                if (i == 1) cover.transform.position = (mount.position + muzzle.position) * 0.5f;
                Physics.SyncTransforms();
                var query = weapon.QueryShotLine(target.AimPoint, i);
                Assert.That(query.MountBlocked, Is.True);
                Assert.That(query.Contact.Collider, Is.SameAs(cover));
                Tick(i, i, target);
                Assert.That(shots.Count, Is.EqualTo(i + 1));
                Assert.That(shots[i].MountBlocked, Is.True);
                Assert.That(shots[i].Contact.Collider, Is.SameAs(cover));
                Assert.That(target.Requests, Is.Zero);
            }
        }

        [Test]
        public void FirstFriendlyStopsDamageAndFirstEnemyReceivesIt()
        {
            var blocker = Actor(3, owner.TeamId, new Vector3(0.35f, 0f, 4f));
            Physics.SyncTransforms();
            Hold();
            Tick(0, 0, target);
            Assert.That(shots[0].Contact.Actor, Is.SameAs(blocker));
            Assert.That(shots[0].TargetId, Is.EqualTo(target.ActorId));
            Assert.That(shots[0].HitActorId, Is.EqualTo(blocker.ActorId));
            Assert.That(blocker.Requests, Is.Zero);
            blocker.team = target.TeamId;
            Tick(1, 1, target);
            Assert.That(shots[1].Contact.Actor, Is.SameAs(blocker));
            Assert.That(blocker.LastRequest.TargetId, Is.EqualTo(blocker.ActorId));
            Assert.That(blocker.Requests, Is.EqualTo(1));
            Assert.That(target.Requests, Is.Zero);
        }

        [Test]
        public void StaleFramesLostTargetsAndOldCommandsDoNotFire()
        {
            Tick(0, 0, target);
            weapon.SetCommand(Episode - 1, true);
            Tick(1, 1, target);
            Assert.That(shots, Is.Empty);
            Hold();
            weapon.Tick(Episode, 2, 2, new VisibilityFrame(Episode, 1, new[] { target }));
            Assert.That(weapon.Target, Is.Null);
            Assert.That(weapon.Status, Is.EqualTo(ShotStatus.StaleVision));
            weapon.Tick(Episode, 3, 3, new VisibilityFrame(Episode - 1, 3, new[] { target }));
            Assert.That(weapon.Status, Is.EqualTo(ShotStatus.StaleVision));
            Tick(4, 4);
            Assert.That(weapon.Target, Is.Null);
            SamePoint(muzzle.forward, owner.transform.forward);
            var observed = vision.Sample(Episode, 5, Mask);
            target.health = 0f;
            weapon.Tick(Episode, 5, 5, observed);
            Assert.That(weapon.Target, Is.Null);
            Assert.That(shots, Is.Empty);
            target.health = 100f;
            observed = vision.Sample(Episode, 6, Mask);
            CollectionAssert.Contains(observed.Candidates, target);
            target.transform.localPosition += Vector3.right * 2f;
            Physics.SyncTransforms();
            weapon.Tick(Episode, 6, 6, observed);
            Assert.That(weapon.Target, Is.Null);
            Assert.That(shots, Is.Empty);
        }

        [Test]
        public void DeathAndEpisodeResetClearHeldFireTargetAndTimers()
        {
            Hold();
            Tick(0, 8, target);
            Assert.That(weapon.RemainingCooldown(8), Is.GreaterThan(0d));
            owner.health = 0f;
            Tick(1, 9, target);
            Assert.That(weapon.Status, Is.EqualTo(ShotStatus.ShooterDead));
            Assert.That(weapon.Target, Is.Null);
            owner.health = 5f;
            Tick(2, 10, target);
            Assert.That(shots.Count, Is.EqualTo(1));
            const int next = Episode + 1;
            weapon.ResetEpisode(next, Seed);
            Assert.That(weapon.RemainingCooldown(0), Is.Zero);
            Assert.That(weapon.LastShot.Sequence, Is.Zero);
            weapon.SetCommand(Episode, true);
            weapon.Tick(next, 0, 0, new VisibilityFrame(next, 0, new[] { target }));
            Assert.That(shots.Count, Is.EqualTo(1));
            weapon.SetCommand(next, true);
            weapon.Tick(next, 1, 0.02, new VisibilityFrame(next, 1, new[] { target }));
            Assert.That(shots[1].Sequence, Is.EqualTo(1));
            Assert.That(shots[1].EpisodeId, Is.EqualTo(next));
            weapon.EndEpisode();
            Assert.That(weapon.Target, Is.Null);
            weapon.Tick(next, 2, 1, new VisibilityFrame(next, 2, new[] { target }));
            Assert.That(shots.Count, Is.EqualTo(2));
        }

        [Test]
        public void HeldFireUsesSimulationCadenceAndRejectsDuplicateTicks()
        {
            Hold();
            for (int i = 0; i < 50; i++)
            {
                Tick(i, i * 0.02d, target);
                Tick(i, i * 0.02d, target);
            }
            Assert.That(shots.Count, Is.EqualTo(5));
            for (int i = 0; i < shots.Count; i++)
                Assert.That(shots[i].Time, Is.EqualTo(i * 0.2d).Within(0.000001d));
            weapon.SetCommand(Episode, false);
            Tick(50, 1, target);
            Assert.That(shots.Count, Is.EqualTo(5));
        }

        [Test]
        public void CapsuleRegionsFollowStandingCrouchingAndRotatedScaledAxis()
        {
            for (int posture = 0; posture < 3; posture++)
            {
                var capsule = target.Capsule;
                capsule.height = posture == 1 ? 1.2f : 2f;
                capsule.center = Vector3.up * (capsule.height / 2f);
                if (posture == 2)
                {
                    target.transform.rotation = Quaternion.Euler(20f, 30f, 75f);
                    target.transform.localScale = new Vector3(1.1f, 1.3f, 0.8f);
                }
                Physics.SyncTransforms();
                foreach (HitRegion expected in new[] { HitRegion.Head, HitRegion.Body })
                {
                    float height = expected == HitRegion.Head ? 0.43f * capsule.height : 0f;
                    Vector3 point = target.transform.TransformPoint(capsule.center + Vector3.up * height);
                    Vector3 direction = target.transform.forward;
                    var hit = ShotPhysics.Trace(owner, point - 3f * direction, direction, 5f, Mask);
                    Assert.That(hit.Actor, Is.SameAs(target));
                    Assert.That(target.RegionAt(hit.Point), Is.EqualTo(expected), $"Posture {posture}");
                }
            }
        }

        [Test]
        public void CameraAndReadOnlyQueriesDoNotAffectReplay()
        {
            settings.accuracy = 0.7f;
            var camera = Child(root, "Spectator", Vector3.back * 5).gameObject.AddComponent<Camera>();
            Hold();
            for (int i = 0; i < 24; i++) Tick(i, i, target);
            ShotResult[] baseline = shots.ToArray();
            Assert.That(baseline.Length, Is.EqualTo(24));
            weapon.ResetEpisode(Episode, Seed);
            shots.Clear();
            int startRequests = target.Requests;
            camera.transform.localPosition = Vector3.right * 5;
            camera.transform.localRotation = Quaternion.Euler(30f, -70f, 0f);
            Hold();
            for (int i = 0; i < baseline.Length; i++)
            {
                double cooldown = weapon.RemainingCooldown(i);
                int before = target.Requests;
                for (int j = 0; j < 4; j++)
                {
                    var line = weapon.QueryShotLine(target.AimPoint, i);
                    Assert.That(line.Contact.Actor, Is.SameAs(target));
                    Assert.That(line.Time, Is.EqualTo((double)i));
                    Assert.That(line.Valid && !line.OutOfRange, Is.True);
                }
                Assert.That(target.Requests, Is.EqualTo(before));
                Assert.That(shots.Count, Is.EqualTo(i));
                Assert.That(weapon.RemainingCooldown(i), Is.EqualTo(cooldown));
                Tick(i, i, target);
                Assert.That(shots[i].AimedAtTarget, Is.EqualTo(baseline[i].AimedAtTarget));
                Assert.That(shots[i].Contact.Kind, Is.EqualTo(baseline[i].Contact.Kind));
                SamePoint(shots[i].Origin, baseline[i].Origin);
                SamePoint(shots[i].Direction, baseline[i].Direction);
                SamePoint(shots[i].Contact.Point, baseline[i].Contact.Point);
            }
            Assert.That(target.Requests - startRequests, Is.EqualTo(startRequests));
        }

        [Test]
        public void TrialSightUsesHorizontalDistanceAndWeaponHasIndependentRange()
        {
            target.transform.localPosition = new Vector3(0f, 4f, 100f);
            Physics.SyncTransforms();
            var boundary = vision.Sample(Episode, 0, Mask);
            CollectionAssert.Contains(boundary.Candidates, target);
            Hold();
            weapon.Tick(Episode, 0, 0, boundary);
            Assert.That(weapon.Target, Is.Null);
            Assert.That(weapon.QueryShotLine(target.AimPoint, 0).OutOfRange, Is.True);
            Assert.That(shots, Is.Empty);
            target.transform.localPosition += Vector3.forward * 0.01f;
            Physics.SyncTransforms();
            Assert.That(vision.Sample(Episode, 1, Mask).Candidates, Is.Empty);
            CollectionAssert.Contains(boundary.Candidates, target);
            target.transform.localPosition -= Vector3.forward;
            root.position += new Vector3(51f, 0f, 51f);
            Physics.SyncTransforms();
            CollectionAssert.Contains(vision.Sample(Episode, 2, Mask).Candidates, target);
        }

        [Test]
        public void TrialSightRejectsOutsideFovOccludedDeadAndFriendly()
        {
            target.transform.localPosition = new Vector3(7.9f, 0f, 8f);
            Physics.SyncTransforms();
            CollectionAssert.Contains(vision.Sample(Episode, 0, Mask).Candidates, target);
            target.transform.localPosition = new Vector3(8.1f, 0f, 8f);
            Physics.SyncTransforms();
            Assert.That(vision.Sample(Episode, 1, Mask).Candidates, Is.Empty);
            target.transform.localPosition = Vector3.forward * 8f;
            var wall = Box(root, new Vector3(0f, 1.5f, 4f), new Vector3(3f, 3f, 0.2f));
            Physics.SyncTransforms();
            Assert.That(vision.Sample(Episode, 2, Mask).Candidates, Is.Empty);
            wall.enabled = false;
            target.team = owner.TeamId;
            Assert.That(vision.Sample(Episode, 3, Mask).Candidates, Is.Empty);
            target.team++;
            target.health = 0f;
            Assert.That(vision.Sample(Episode, 4, Mask).Candidates, Is.Empty);
        }

        [Test]
        public void OverlapWithActorRejectsFireAndNoMissDirectionIsFabricated()
        {
            target.transform.position = muzzle.position - Vector3.up + Vector3.forward * 0.1f;
            Physics.SyncTransforms();
            bool clear = ShotPhysics.ClearMissDirection(target, muzzle.position, target.AimPoint,
                settings.range, Mask, new System.Random(Seed), out _);
            Assert.That(clear, Is.False);
            Hold();
            Tick(0, 0, target);
            Assert.That(weapon.Status, Is.EqualTo(ShotStatus.OverlappingActor));
            Assert.That(shots, Is.Empty);
            Assert.That(target.Requests, Is.Zero);
        }

        [Test]
        public void UnavailableMissDirectionRejectsWithoutChargingCooldown()
        {
            settings.accuracy = 0f;
            // A compound target owns a wide plate just beyond the muzzle, perpendicular to
            // the aiming axis. Every candidate in the forward miss cone intersects the plate.
            Vector3 forward = (target.AimPoint - muzzle.position).normalized;
            var plate = Box(target.transform, Vector3.zero, new Vector3(40f, 40f, 0.02f));
            plate.transform.SetPositionAndRotation(muzzle.position + 0.15f * forward,
                Quaternion.LookRotation(forward));
            Physics.SyncTransforms();
            Hold();
            Tick(0, 0, target);
            Assert.That(weapon.Status, Is.EqualTo(ShotStatus.MissDirectionUnavailable));
            Assert.That(weapon.MissDirectionFailures, Is.EqualTo(1));
            Assert.That(weapon.RemainingCooldown(0), Is.Zero);
            Assert.That(shots, Is.Empty);
            Assert.That(target.Requests, Is.Zero);

            plate.enabled = false;
            Physics.SyncTransforms();
            Tick(1, 0.02, target);
            Assert.That(weapon.Status, Is.EqualTo(ShotStatus.Fired));
            Assert.That(shots[0].Sequence, Is.EqualTo(1));
        }

        [Test]
        public void NearestThenStableIdWinsAndAimLeavesBodyPoseToMovement()
        {
            target.transform.localPosition = new Vector3(-2f, 0f, 8f);
            var tied = Actor(3, 2, new Vector3(2f, 0f, 8f));
            var near = Actor(4, 2, new Vector3(-1f, 0f, 6f));
            Physics.SyncTransforms();
            Tick(0, 0, tied, target, near);
            Assert.That(weapon.Target, Is.SameAs(near));
            Tick(1, 1, tied, target);
            Assert.That(weapon.Target, Is.SameAs(target));

            owner.transform.localPosition = Vector3.left * 2f;
            owner.transform.localRotation = Quaternion.Euler(0f, 30f, 0f);
            Vector3 bodyPosition = owner.transform.position;
            Quaternion bodyRotation = owner.transform.rotation;
            Vector3 movingMuzzle = muzzle.position;
            Physics.SyncTransforms();
            Hold();
            Tick(2, 2, target);
            Assert.That(shots.Count, Is.EqualTo(1));
            SamePoint(owner.transform.position, bodyPosition);
            Assert.That(Quaternion.Angle(owner.transform.rotation, bodyRotation), Is.LessThan(0.001f));
            SamePoint(shots[0].Origin, movingMuzzle);
            Assert.That(owner.transform.InverseTransformPoint(shots[0].Origin).x, Is.GreaterThan(0f));
        }
    }
}
