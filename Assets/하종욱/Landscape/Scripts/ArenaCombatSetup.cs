using System.Collections.Generic;
using Reinforce.Integration;
using Reinforce.Movement;
using Reinforcement.Shooting;
using UnityEngine;

namespace ReinProject.Landscape
{
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ArenaEnvironment))]
    internal sealed class ArenaCombatSetup : MonoBehaviour, IShotObstacleResolver
    {
        [SerializeField] private PlayerCombatIntegration _teamA;
        [SerializeField] private PlayerCombatIntegration _teamB;

        private ArenaEnvironment _arena;
        private string _terrainId;
        private string _boundaryId;

        private void Awake()
        {
            _arena = GetComponent<ArenaEnvironment>();
            _terrainId = _arena.MapId + "/terrain";
            _boundaryId = _arena.MapId + "/boundary";
        }

        private void Start()
        {
            if (_teamA == _teamB || !TryGetInput(_teamA, out _) || !TryGetInput(_teamB, out _))
            {
                StopSetup("Two distinct active combatants with movement and agent input are required.");
                return;
            }

            MovementSettingsSO settingsA = _teamA.Movement.Settings;
            MovementSettingsSO settingsB = _teamB.Movement.Settings;
            float standingHeight = Mathf.Max(settingsA.standingHeight, settingsB.standingHeight);
            float radius = Mathf.Max(settingsA.colliderRadius, settingsB.colliderRadius);
            if (!_arena.TryPrepare(standingHeight, radius, out string failure))
            {
                StopSetup(failure);
                return;
            }

            if (!_arena.TryGetSpawn(ArenaSpawnSlot.A, out Pose spawnA) ||
                !_arena.TryGetSpawn(ArenaSpawnSlot.B, out Pose spawnB))
            {
                StopSetup("Both prepared arena spawn poses are required.");
                return;
            }

            ConfigurePrimary(_teamA, spawnA, 1);
            ConfigurePrimary(_teamB, spawnB, 2);
            RegisterCombatants();
        }

        private static void ConfigurePrimary(PlayerCombatIntegration combatant, Pose spawn, int id)
        {
            combatant.SetSpawnPose(spawn);
            combatant.Actor.SetIdentity(id, id);
        }

        private static bool TryGetInput(PlayerCombatIntegration combatant, out PlayerAgentInputBridge input)
        {
            input = null;
            return combatant && combatant.isActiveAndEnabled && combatant.Actor &&
                combatant.Actor.isActiveAndEnabled && combatant.IsMovementReady &&
                combatant.TryGetComponent(out input);
        }

        private void RegisterCombatants()
        {
            var combatants = new List<PlayerCombatIntegration>();
            foreach (PlayerCombatIntegration combatant in GetComponentsInChildren<PlayerCombatIntegration>())
            {
                if (!TryGetInput(combatant, out PlayerAgentInputBridge input))
                    continue;

                combatant.Movement.SetInputProvider(input);
                combatants.Add(combatant);
            }

            var opponentsByTeam = new Dictionary<int, ShotActor[]>();
            foreach (PlayerCombatIntegration combatant in combatants)
            {
                int teamId = combatant.Actor.TeamId;
                if (!opponentsByTeam.TryGetValue(teamId, out ShotActor[] opponents))
                {
                    var otherActors = new List<ShotActor>();
                    foreach (PlayerCombatIntegration candidate in combatants)
                    {
                        if (candidate.Actor.TeamId != teamId)
                            otherActors.Add(candidate.Actor);
                    }

                    opponents = otherActors.ToArray();
                    opponentsByTeam.Add(teamId, opponents);
                }

                combatant.SetOpponents(opponents);
            }
        }


        public ShotObstacleResponse Classify(Collider collider, out string obstacleId)
        {
            obstacleId = null;
            switch (_arena.GetCollisionKind(collider))
            {
                case ArenaCollisionKind.Cover:
                    if (_arena.TryGetCover(collider, out ArenaCoverEntry cover))
                        obstacleId = cover.Id;
                    return ShotObstacleResponse.Block;
                case ArenaCollisionKind.Terrain:
                    obstacleId = _terrainId;
                    return ShotObstacleResponse.Block;
                case ArenaCollisionKind.Boundary:
                    obstacleId = _boundaryId;
                    return ShotObstacleResponse.Block;
                case ArenaCollisionKind.Decoration:
                    return ShotObstacleResponse.Ignore;
                default:
                    return ShotObstacleResponse.Default;
            }
        }

        private void StopSetup(string failure)
        {
            Debug.LogError($"OutdoorArena combat setup failed: {failure}", this);
            if (_teamA) _teamA.gameObject.SetActive(false);
            if (_teamB) _teamB.gameObject.SetActive(false);
        }
    }
}
