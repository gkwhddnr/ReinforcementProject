using System;
using UnityEngine;

namespace Reinforce.Training
{
    [DisallowMultipleComponent]
    public sealed class MatchEpisodeController : MonoBehaviour
    {
        [SerializeField] private DecisionAgent firstAgent;
        [SerializeField] private DecisionAgent secondAgent;

        private readonly DuelEpisode episode = new DuelEpisode();

        public int EpisodeId => episode.Id;
        public bool IsRunning => episode.IsRunning;
        public event Action<EpisodeResult> EpisodeFinished;

        public void Bind(DecisionAgent first, DecisionAgent second)
        {
            if (episode.IsRunning)
                throw new InvalidOperationException("Stop the match before changing participants.");
            firstAgent = first;
            secondAgent = second;
        }

        public bool StartMatch()
        {
            if (episode.IsRunning)
                return false;
            if (firstAgent == null || secondAgent == null || firstAgent == secondAgent)
            {
                Debug.LogError("A duel requires two different DecisionAgents.", this);
                return false;
            }

            episode.Begin();
            try
            {
                if (!firstAgent.PrepareEpisode(episode.Id) || !secondAgent.PrepareEpisode(episode.Id))
                {
                    AbortMatch();
                    return false;
                }

                firstAgent.BeginActions();
                secondAgent.BeginActions();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                AbortMatch();
                return false;
            }
        }

        public bool ReportDefeat(int episodeId, int participant)
        {
            return episode.ReportDefeat(episodeId, participant);
        }

        public bool QueueTerminalBonus(int episodeId, int participant, string eventId, float amount)
        {
            return episode.QueueTerminalBonus(episodeId, participant, eventId, amount);
        }

        // The future simulation owner calls this once after movement, shooting, and damage resolution.
        public void CompleteSimulationStep(float simulationDeltaTime)
        {
            if (!episode.IsRunning)
                return;
            if (firstAgent == null || secondAgent == null ||
                !firstAgent.isActiveAndEnabled || !secondAgent.isActiveAndEnabled ||
                !firstAgent.IsRunning || !secondAgent.IsRunning)
            {
                AbortMatch();
                return;
            }

            if (!episode.Advance(simulationDeltaTime))
                return;

            firstAgent.StopActions();
            secondAgent.StopActions();
            firstAgent.AddReward(episode.GetTerminalReward(0));
            secondAgent.AddReward(episode.GetTerminalReward(1));
            firstAgent.EndEpisode();
            secondAgent.EndEpisode();
            EpisodeFinished?.Invoke(new EpisodeResult(episode));
        }

        public void AbortMatch()
        {
            if (!episode.IsRunning)
                return;
            episode.Abort();
            if (firstAgent != null)
            {
                firstAgent.InterruptEpisode();
            }
            if (secondAgent != null && secondAgent != firstAgent)
            {
                secondAgent.InterruptEpisode();
            }
            EpisodeFinished?.Invoke(new EpisodeResult(episode));
        }

        private void OnDisable()
        {
            AbortMatch();
        }
    }
}
