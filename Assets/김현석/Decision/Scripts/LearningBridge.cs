using UnityEngine;

namespace Reinforce.Decision
{
    // Implement in the learning integration layer after the owners agree on their public APIs.
    public abstract class LearningBridge : MonoBehaviour
    {
        public abstract bool IsReady { get; }
        public abstract bool TryReadObservation(out ObservationFrame frame);
        public abstract void ApplyAction(DecisionAction action);
        public abstract void ResetForEpisode(int episodeId);
    }
}
