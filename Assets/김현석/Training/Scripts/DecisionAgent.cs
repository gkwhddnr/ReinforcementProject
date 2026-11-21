using System;
using Reinforce.Decision;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace Reinforce.Training
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BehaviorParameters))]
    public sealed class DecisionAgent : Agent
    {
        [SerializeField] private LearningBridge bridge;
        [SerializeField, Range(1, 20)] private int decisionPeriod = 5;

        private readonly float[] observations = new float[ObservationFrame.Size];
        private int heuristicMovement;
        private int heuristicRotation;
        private int heuristicFire;
        private int decisionTicks;
        private bool episodeBegun;

        public bool IsRunning { get; private set; }
        public int EpisodeId { get; private set; }

        protected override void OnDisable()
        {
            if (Academy.IsInitialized)
                Academy.Instance.AgentPreStep -= RequestStep;
            StopActions();
            episodeBegun = false;
            base.OnDisable();
        }

        public override void Initialize()
        {
            MaxStep = 0;
            Academy.Instance.AgentPreStep += RequestStep;
        }

        public void Bind(LearningBridge value)
        {
            if (IsRunning)
                throw new InvalidOperationException("Stop the match before changing its bridge.");
            bridge = value;
        }

        public bool PrepareEpisode(int episodeId)
        {
            if (!episodeBegun)
            {
                Debug.LogError("Wait for the Academy's initial episode reset before starting a match.", this);
                return false;
            }
            if (!isActiveAndEnabled || bridge == null || !bridge.isActiveAndEnabled || !bridge.IsReady)
            {
                Debug.LogError("Connect a ready LearningBridge before starting a match.", this);
                return false;
            }

            var brain = GetComponent<BehaviorParameters>().BrainParameters;
            var spec = brain.ActionSpec;
            if (brain.VectorObservationSize != ObservationFrame.Size || brain.NumStackedVectorObservations != 1 ||
                spec.NumContinuousActions != 0 || spec.NumDiscreteActions != 3 ||
                spec.BranchSizes[0] != 5 || spec.BranchSizes[1] != 3 || spec.BranchSizes[2] != 2 ||
                GetComponent<DecisionRequester>() != null)
            {
                Debug.LogError("Expected 10 observations, one stack, discrete branches [5,3,2], and no DecisionRequester.", this);
                return false;
            }

            StopActions();
            EpisodeId = episodeId;
            bridge.ResetForEpisode(episodeId);
            decisionTicks = 0;
            SetHeuristicAction(0, 0, 0);
            return RefreshObservations();
        }

        internal void BeginActions()
        {
            IsRunning = true;
        }

        public void StopActions()
        {
            IsRunning = false;
            if (bridge != null)
                bridge.ApplyAction(default);
        }

        internal void InterruptEpisode()
        {
            StopActions();
            if (episodeBegun && isActiveAndEnabled)
                EpisodeInterrupted();
        }

        public override void OnEpisodeBegin()
        {
            StopActions();
            episodeBegun = true;
            Array.Clear(observations, 0, observations.Length);
            SetHeuristicAction(0, 0, 0);
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            if (IsRunning && !RefreshObservations())
                StopActions();
            for (int i = 0; i < observations.Length; i++)
                sensor.AddObservation(observations[i]);
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            if (!IsRunning)
                return;
            if (bridge == null || !bridge.isActiveAndEnabled || !bridge.IsReady)
            {
                StopActions();
                return;
            }

            var discrete = actions.DiscreteActions;
            bridge.ApplyAction(new DecisionAction(discrete[0], discrete[1], discrete[2]));
        }

        public void SetHeuristicAction(int movement, int rotation, int fire)
        {
            _ = new DecisionAction(movement, rotation, fire);
            heuristicMovement = movement;
            heuristicRotation = rotation;
            heuristicFire = fire;
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var discrete = actionsOut.DiscreteActions;
            discrete[0] = heuristicMovement;
            discrete[1] = heuristicRotation;
            discrete[2] = heuristicFire;
        }

        private void RequestStep(int academyStep)
        {
            if (!IsRunning)
                return;
            if (decisionTicks % Mathf.Max(1, decisionPeriod) == 0)
                RequestDecision();
            else
                RequestAction();
            decisionTicks++;
        }

        private bool RefreshObservations()
        {
            if (bridge == null || !bridge.isActiveAndEnabled || !bridge.IsReady || !bridge.TryReadObservation(out var frame))
            {
                Debug.LogError("Learning observations are unavailable; stop and reconnect the environment.", this);
                return false;
            }

            try
            {
                frame.WriteTo(observations);
                return true;
            }
            catch (ArgumentException exception)
            {
                Debug.LogException(exception, this);
                return false;
            }
        }
    }
}
