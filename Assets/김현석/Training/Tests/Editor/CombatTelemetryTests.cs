using System;
using NUnit.Framework;
using Reinforcement.Shooting;
using UnityEngine;

namespace Reinforce.Training.Tests
{
    public sealed class CombatTelemetryTests
    {
        [Test]
        public void SameSequenceFromDifferentShootersIsRetainedAndDuplicatesAreRejected()
        {
            var buffer = new CombatTelemetryBuffer();
            buffer.BeginEpisode(4);
            var shot = new ShotResult { EpisodeId = 4, ShooterId = 1, Sequence = 1 };
            Assert.IsTrue(buffer.TryRecord(shot));
            Assert.IsFalse(buffer.TryRecord(shot));
            shot.ShooterId = 2;
            Assert.IsTrue(buffer.TryRecord(shot));
            Assert.AreEqual(2, buffer.Shots.Count);
        }

        [Test]
        public void NewEpisodeClearsPriorSamplesAndRejectsDelayedShots()
        {
            var buffer = new CombatTelemetryBuffer();
            buffer.BeginEpisode(4);
            var shot = new ShotResult { EpisodeId = 4, ShooterId = 1, Sequence = 1 };
            buffer.TryRecord(shot);
            buffer.BeginEpisode(4);
            Assert.AreEqual(1, buffer.Shots.Count);
            buffer.BeginEpisode(5);
            Assert.AreEqual(0, buffer.Shots.Count);
            Assert.IsFalse(buffer.TryRecord(shot));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.BeginEpisode(0));
        }

        [Test]
        public void SampleCopiesActualContactAndDamageInsteadOfTheAimRoll()
        {
            var buffer = new CombatTelemetryBuffer();
            buffer.BeginEpisode(4);
            var shot = new ShotResult
            {
                EpisodeId = 4, ShooterId = 1, Sequence = 1, TargetId = 2,
                AimedAtTarget = true, MountBlocked = true,
                Contact = new ShotContact { Kind = ContactKind.Obstacle, ObstacleId = "wall-1", Point = Vector3.right },
                Damage = new DamageReceipt(0f, false)
            };
            Assert.IsTrue(buffer.TryRecord(shot));
            shot.Contact.ObstacleId = "changed";
            ShotSample sample = buffer.Shots[0];
            Assert.IsTrue(sample.AimedAtTarget);
            Assert.AreEqual(ContactKind.Obstacle, sample.ContactKind);
            Assert.AreEqual("wall-1", sample.ObstacleId);
            Assert.AreEqual(-1, sample.HitActorId);
            Assert.AreEqual(0f, sample.AppliedDamage);

            shot.Sequence = 2;
            shot.HitActorId = 3;
            shot.Contact.Kind = ContactKind.Actor;
            shot.Damage = new DamageReceipt(5f, true);
            buffer.TryRecord(shot);
            Assert.AreEqual(2, buffer.Shots[1].TargetId);
            Assert.AreEqual(3, buffer.Shots[1].HitActorId);
            Assert.AreEqual(5f, buffer.Shots[1].AppliedDamage);
            Assert.IsTrue(buffer.Shots[1].Killed);
        }

        [Test]
        public void InvalidSampleIsRejectedBeforeItsKeyIsConsumed()
        {
            var buffer = new CombatTelemetryBuffer();
            buffer.BeginEpisode(4);
            var shot = new ShotResult { EpisodeId = 4, ShooterId = 1, Sequence = 1, Time = double.NaN };
            Assert.Throws<ArgumentException>(() => buffer.TryRecord(shot));
            shot.Time = 0d;
            Assert.IsTrue(buffer.TryRecord(shot));
        }
    }
}
