using ReinProject.Landscape;
using UnityEditor;
using UnityEngine;

namespace Reinforce.LearningIntegration.Editor
{
    public static class OutdoorArenaTelemetryMenu
    {
        [MenuItem("Tools/Reinforcement/Attach OutdoorArena Telemetry")]
        public static void Attach()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("Enter Play Mode in OutdoorArena before attaching telemetry.");
                return;
            }
            var arena = Object.FindFirstObjectByType<ArenaSpawnManager>();
            if (!arena)
            {
                Debug.LogWarning("No active ArenaSpawnManager was found.");
                return;
            }

            var telemetry = arena.GetComponent<OutdoorArenaTelemetry>();
            if (!telemetry)
                telemetry = arena.gameObject.AddComponent<OutdoorArenaTelemetry>();
            telemetry.Bind(arena);
            Selection.activeGameObject = arena.gameObject;
        }
    }
}
