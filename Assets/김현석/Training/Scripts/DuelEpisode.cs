using System;
using System.Collections.Generic;

namespace Reinforce.Training
{
    public enum DuelOutcome
    {
        None,
        FirstWins,
        SecondWins,
        Draw,
        Aborted
    }

    public readonly struct EpisodeResult
    {
        public readonly int EpisodeId;
        public readonly DuelOutcome Outcome;
        public readonly float ElapsedSeconds;
        public readonly float FirstReward;
        public readonly float SecondReward;

        public EpisodeResult(DuelEpisode episode)
        {
            EpisodeId = episode.Id;
            Outcome = episode.Outcome;
            ElapsedSeconds = episode.ElapsedSeconds;
            FirstReward = Outcome == DuelOutcome.Aborted ? 0f : episode.GetTerminalReward(0);
            SecondReward = Outcome == DuelOutcome.Aborted ? 0f : episode.GetTerminalReward(1);
        }
    }

    public sealed class DuelEpisode
    {
        public const float TimeLimitSeconds = 300f;

        private readonly bool[] defeated = new bool[2];
        private readonly float[] bonuses = new float[2];
        private readonly HashSet<(int participant, string eventId)> bonusEvents = new HashSet<(int, string)>();

        public int Id { get; private set; }
        public bool IsRunning { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public DuelOutcome Outcome { get; private set; }

        public void Begin()
        {
            if (IsRunning)
                throw new InvalidOperationException("The current episode is still running.");

            Id = checked(Id + 1);
            ElapsedSeconds = 0f;
            Outcome = DuelOutcome.None;
            Array.Clear(defeated, 0, defeated.Length);
            Array.Clear(bonuses, 0, bonuses.Length);
            bonusEvents.Clear();
            IsRunning = true;
        }

        public bool ReportDefeat(int episodeId, int participant)
        {
            ValidateParticipant(participant);
            if (!IsRunning || episodeId != Id)
                return false;

            defeated[participant] = true;
            return true;
        }

        public bool QueueTerminalBonus(int episodeId, int participant, string eventId, float amount)
        {
            ValidateParticipant(participant);
            if (string.IsNullOrWhiteSpace(eventId))
                throw new ArgumentException("A stable bonus event ID is required.", nameof(eventId));
            ValidateFinite(amount);
            if (!IsRunning || episodeId != Id || bonusEvents.Contains((participant, eventId)))
                return false;

            float total = bonuses[participant] + amount;
            ValidateFinite(total);
            bonusEvents.Add((participant, eventId));
            bonuses[participant] = total;
            return true;
        }

        // Call once after both participants' results for the simulation step have been reported.
        public bool Advance(float simulationDeltaTime)
        {
            ValidateFinite(simulationDeltaTime);
            if (simulationDeltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(simulationDeltaTime));
            if (!IsRunning)
                return false;

            ElapsedSeconds = Math.Min(TimeLimitSeconds, ElapsedSeconds + simulationDeltaTime);
            if (defeated[0] && defeated[1])
                Outcome = DuelOutcome.Draw;
            else if (defeated[0])
                Outcome = DuelOutcome.SecondWins;
            else if (defeated[1])
                Outcome = DuelOutcome.FirstWins;
            else if (ElapsedSeconds >= TimeLimitSeconds)
                Outcome = DuelOutcome.Draw;

            if (Outcome == DuelOutcome.None)
                return false;

            IsRunning = false;
            return true;
        }

        public void Abort()
        {
            if (!IsRunning)
                return;
            Outcome = DuelOutcome.Aborted;
            IsRunning = false;
        }

        public float GetTerminalReward(int participant)
        {
            ValidateParticipant(participant);
            if (IsRunning || Outcome == DuelOutcome.None || Outcome == DuelOutcome.Aborted)
                throw new InvalidOperationException("Rewards require a completed match.");

            float result = 0f;
            if (Outcome == DuelOutcome.FirstWins)
                result = participant == 0 ? 1f : -1f;
            else if (Outcome == DuelOutcome.SecondWins)
                result = participant == 1 ? 1f : -1f;
            return result + bonuses[participant];
        }

        private static void ValidateParticipant(int participant)
        {
            if (participant < 0 || participant > 1)
                throw new ArgumentOutOfRangeException(nameof(participant));
        }

        private static void ValidateFinite(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentException("Expected a finite value.");
        }
    }
}
