using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Reinforce.Decision;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.TestTools;

namespace Reinforce.Training.Tests
{
    public sealed class LearningShellTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject item in objects)
                UnityEngine.Object.DestroyImmediate(item);
            objects.Clear();
            if (Academy.IsInitialized)
                Academy.Instance.Dispose();
        }

        [Test]
        public void ObservationSchemaNormalizesAndHidesUnseenTarget()
        {
            ObservationFrame frame = ValidFrame();
            var values = new float[ObservationFrame.Size];
            frame.Health = 60f;
            frame.PlanarSpeed = 2f;
            frame.CoverDistances = new Vector4(0f, 5f, 10f, 20f);
            frame.HasTarget = true;
            frame.TargetLocalPosition = new Vector3(5f, -10f, 30f);
            frame.WriteTo(values);
            CollectionAssert.AreEqual(new[] { 0.6f, 0.5f, 0f, 0.5f, 1f, 1f, 1f, 0.25f, -0.5f, 1f }, values);

            frame.HasTarget = false;
            frame.TargetLocalPosition = new Vector3(float.NaN, float.PositiveInfinity, 1000f);
            frame.WriteTo(values);
            CollectionAssert.AreEqual(new[] { 0f, 0f, 0f, 0f }, new[] { values[6], values[7], values[8], values[9] });
        }

        [Test]
        public void InvalidObservationDoesNotPartiallyReplacePreviousFrame()
        {
            ObservationFrame frame = ValidFrame();
            var values = new float[ObservationFrame.Size];
            frame.WriteTo(values);
            float[] previous = (float[])values.Clone();
            frame.CoverDistances.w = float.NaN;
            Assert.Throws<ArgumentException>(() => frame.WriteTo(values));
            CollectionAssert.AreEqual(previous, values);
            frame = ValidFrame();
            frame.MaximumHealth = 0f;
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.WriteTo(values));
        }

        [TestCase(0, 0, 0)]
        [TestCase(1, 0, 1)]
        [TestCase(2, 0, -1)]
        [TestCase(3, -1, 0)]
        [TestCase(4, 1, 0)]
        public void MovementActionsHaveDocumentedLocalDirections(int movement, float x, float z)
        {
            Assert.AreEqual(new Vector2(x, z), new DecisionAction(movement, 0, 0).Move);
        }

        [Test]
        public void RotationAndFireAreIndependentOfMovement()
        {
            var action = new DecisionAction(0, 1, 1);
            Assert.AreEqual(Vector2.zero, action.Move);
            Assert.AreEqual(-1, action.Turn);
            Assert.IsTrue(action.Fire);
            Assert.AreEqual(1, new DecisionAction(0, 2, 0).Turn);
            Assert.Throws<ArgumentOutOfRangeException>(() => new DecisionAction(5, 0, 0));
        }

        [Test]
        public void TimeoutProducesDrawAtExactlyThreeHundredSeconds()
        {
            var episode = new DuelEpisode();
            episode.Begin();
            Assert.IsFalse(episode.Advance(299.9f));
            Assert.IsTrue(episode.Advance(0.2f));
            Assert.AreEqual(300f, episode.ElapsedSeconds);
            Assert.AreEqual(DuelOutcome.Draw, episode.Outcome);
            Assert.AreEqual(0f, episode.GetTerminalReward(0));
            Assert.IsFalse(episode.Advance(1f));
        }

        [Test]
        public void SameStepDefeatsProduceOneDrawAndNoEarlyWinner()
        {
            var episode = new DuelEpisode();
            episode.Begin();
            Assert.IsTrue(episode.ReportDefeat(episode.Id, 0));
            Assert.AreEqual(DuelOutcome.None, episode.Outcome);
            Assert.IsTrue(episode.ReportDefeat(episode.Id, 1));
            Assert.IsTrue(episode.Advance(0.02f));
            Assert.AreEqual(DuelOutcome.Draw, episode.Outcome);
        }

        [Test]
        public void BonusIsSettledAtEndAndDuplicateEventsAreIgnored()
        {
            var episode = new DuelEpisode();
            episode.Begin();
            Assert.IsTrue(episode.QueueTerminalBonus(episode.Id, 0, "approved-bonus", 0.1f));
            Assert.IsFalse(episode.QueueTerminalBonus(episode.Id, 0, "approved-bonus", 0.1f));
            Assert.Throws<InvalidOperationException>(() => episode.GetTerminalReward(0));
            episode.ReportDefeat(episode.Id, 1);
            episode.Advance(0.02f);
            Assert.AreEqual(1.1f, episode.GetTerminalReward(0), 0.00001f);
            Assert.AreEqual(-1f, episode.GetTerminalReward(1));

            int oldId = episode.Id;
            episode.Begin();
            Assert.IsFalse(episode.ReportDefeat(oldId, 0));
            Assert.IsFalse(episode.QueueTerminalBonus(oldId, 0, "late", 0.4f));
            episode.Advance(300f);
            Assert.AreEqual(0f, episode.GetTerminalReward(0));
        }

        [Test]
        public void AbortHasNoTerminalRewardAndRejectsInvalidInputs()
        {
            var episode = new DuelEpisode();
            episode.Begin();
            Assert.Throws<InvalidOperationException>(() => episode.Begin());
            Assert.Throws<ArgumentException>(() => episode.Advance(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => episode.Advance(-1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => episode.ReportDefeat(episode.Id, 2));
            episode.Abort();
            Assert.AreEqual(DuelOutcome.Aborted, episode.Outcome);
            Assert.Throws<InvalidOperationException>(() => episode.GetTerminalReward(0));
        }

        [Test]
        public void RealAgentEntryPointsRelayActionsAndSettleOnlyOnce()
        {
            DecisionAgent first = CreateAgent(out var firstBridge);
            DecisionAgent second = CreateAgent(out var secondBridge);
            Academy.Instance.AutomaticSteppingEnabled = false;
            Academy.Instance.EnvironmentStep();
            var controller = CreateObject("Match").AddComponent<MatchEpisodeController>();
            controller.Bind(first, second);
            int finishes = 0;
            EpisodeResult result = default;
            controller.EpisodeFinished += value => { finishes++; result = value; };

            Assert.IsTrue(controller.StartMatch());
            first.SetHeuristicAction(0, 1, 1);
            Academy.Instance.EnvironmentStep();
            Assert.AreEqual(-1, firstBridge.LastAction.Turn);
            Assert.IsTrue(firstBridge.LastAction.Fire);
            Assert.AreEqual(Vector2.zero, firstBridge.LastAction.Move);

            var sensor = new VectorSensor(10);
            first.CollectObservations(sensor);
            Assert.IsTrue(controller.QueueTerminalBonus(controller.EpisodeId, 0, "bonus", 0.1f));
            controller.ReportDefeat(controller.EpisodeId, 1);
            controller.CompleteSimulationStep(0.02f);
            controller.CompleteSimulationStep(0.02f);
            Assert.AreEqual(1, finishes);
            Assert.AreEqual(1.1f, result.FirstReward, 0.00001f);
            Assert.AreEqual(-1f, result.SecondReward);
            Assert.IsFalse(first.IsRunning);
            Assert.IsFalse(firstBridge.LastAction.Fire);

            Assert.IsTrue(controller.StartMatch());
            Assert.AreEqual(2, firstBridge.ResetCount);
            Assert.AreEqual(2, secondBridge.ResetCount);
            Assert.IsFalse(firstBridge.LastAction.Fire);
            controller.CompleteSimulationStep(300f);
            Assert.AreEqual(2, finishes);
            Assert.AreEqual(DuelOutcome.Draw, result.Outcome);
            Assert.AreEqual(0f, result.FirstReward);
        }

        [Test]
        public void MissingBridgeBlocksMatchAndStopsPreparedParticipant()
        {
            DecisionAgent first = CreateAgent(out var firstBridge);
            DecisionAgent second = CreateAgent(out _);
            Academy.Instance.AutomaticSteppingEnabled = false;
            Academy.Instance.EnvironmentStep();
            second.Bind(null);
            var controller = CreateObject("Match").AddComponent<MatchEpisodeController>();
            controller.Bind(first, second);
            LogAssert.Expect(LogType.Error, "Connect a ready LearningBridge before starting a match.");
            Assert.IsFalse(controller.StartMatch());
            Assert.IsFalse(controller.IsRunning);
            Assert.IsFalse(first.IsRunning);
            Assert.IsFalse(firstBridge.LastAction.Fire);
        }

        [UnityTest]
        public IEnumerator PlayModeLifecycleUsesTheSameHeuristicAndResetPath()
        {
            yield return new EnterPlayMode();
            DecisionAgent first = CreateAgent(out var firstBridge);
            DecisionAgent second = CreateAgent(out _);
            Academy.Instance.AutomaticSteppingEnabled = false;
            Academy.Instance.EnvironmentStep();
            var controller = CreateObject("Match").AddComponent<MatchEpisodeController>();
            controller.Bind(first, second);
            Assert.IsTrue(controller.StartMatch());
            first.SetHeuristicAction(1, 2, 1);
            Academy.Instance.EnvironmentStep();
            Assert.AreEqual(Vector2.up, firstBridge.LastAction.Move);
            Assert.AreEqual(1, firstBridge.LastAction.Turn);
            Assert.IsTrue(firstBridge.LastAction.Fire);
            controller.CompleteSimulationStep(300f);
            Assert.IsFalse(first.IsRunning);
            Assert.IsFalse(firstBridge.LastAction.Fire);
            Assert.IsTrue(controller.StartMatch());
            Assert.AreEqual(2, firstBridge.ResetCount);
            controller.AbortMatch();
            Assert.IsTrue(controller.StartMatch());
            first.gameObject.SetActive(false);
            controller.CompleteSimulationStep(0.02f);
            Assert.IsFalse(controller.IsRunning);
            Assert.IsFalse(second.IsRunning);
            yield return new ExitPlayMode();
        }

        private DecisionAgent CreateAgent(out StubLearningBridge bridge)
        {
            GameObject item = CreateObject("Agent");
            item.SetActive(false);
            var behavior = item.AddComponent<BehaviorParameters>();
            behavior.BehaviorName = "DuelPPO";
            behavior.BehaviorType = BehaviorType.HeuristicOnly;
            behavior.BrainParameters.VectorObservationSize = 10;
            behavior.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(5, 3, 2);
            bridge = item.AddComponent<StubLearningBridge>();
            var agent = item.AddComponent<DecisionAgent>();
            agent.Bind(bridge);
            item.SetActive(true);
            agent.LazyInitialize();
            return agent;
        }

        private GameObject CreateObject(string name)
        {
            var item = new GameObject(name);
            objects.Add(item);
            return item;
        }

        internal static ObservationFrame ValidFrame()
        {
            return new ObservationFrame
            {
                Health = 100f,
                MaximumHealth = 100f,
                MaximumSpeed = 4f,
                CoverDistances = Vector4.one * 10f,
                CoverSensorRange = 10f,
                DetectionRange = 20f
            };
        }
    }

    public sealed class StubLearningBridge : LearningBridge
    {
        public DecisionAction LastAction;
        public int ResetCount;
        public override bool IsReady => true;
        public override bool TryReadObservation(out ObservationFrame frame)
        {
            frame = LearningShellTests.ValidFrame();
            return true;
        }
        public override void ApplyAction(DecisionAction action) => LastAction = action;
        public override void ResetForEpisode(int episodeId) => ResetCount++;
    }
}
