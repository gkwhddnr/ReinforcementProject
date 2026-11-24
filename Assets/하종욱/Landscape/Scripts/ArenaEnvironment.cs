using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace ReinProject.Landscape
{
    public enum ArenaSpawnSlot
    {
        A,
        B
    }

    public enum ArenaCollisionKind
    {
        Unclassified,
        Terrain,
        Cover,
        Boundary,
        Decoration
    }

    [Serializable]
    public sealed class ArenaCoverEntry
    {
        [SerializeField] private string _id;
        [SerializeField] private Collider[] _colliders = Array.Empty<Collider>();

        private Collider FirstCollider => _colliders.Length > 0 ? _colliders[0] : null;

        public string Id => _id;
        public IReadOnlyList<Collider> Colliders => _colliders;
        public int Layer => FirstCollider ? FirstCollider.gameObject.layer : -1;
        public string LayerName => FirstCollider ? LayerMask.LayerToName(FirstCollider.gameObject.layer) : string.Empty;
        public string Tag => FirstCollider ? FirstCollider.tag : string.Empty;
    }

    [DisallowMultipleComponent]
    public sealed class ArenaEnvironment : MonoBehaviour
    {
        private const float NavAnchorDistance = .35f;

        [SerializeField] private string _mapId = "outdoor-arena";
        [SerializeField] private int _mapVersion = 1;
        [SerializeField] private string _spawnLayoutId = "default";
        [SerializeField] private TerrainCollider _terrain;
        [SerializeField] private Transform _spawnA;
        [SerializeField] private Transform _spawnB;
        [SerializeField] private NavMeshSurface _surface;
        [SerializeField] private ArenaCoverEntry[] _covers = Array.Empty<ArenaCoverEntry>();

        private readonly Dictionary<Collider, ArenaCoverEntry> _coverByCollider = new();
        private readonly Dictionary<string, ArenaCoverEntry> _coverById = new(StringComparer.Ordinal);
        private Collider[] _overlaps = new Collider[16];
        private NavMeshPath _spawnPath;
        private Pose _preparedA;
        private Pose _preparedB;

        public bool IsReady { get; private set; }
        public string MapId => _mapId;
        public int MapVersion => _mapVersion;
        public string SpawnLayoutId => _spawnLayoutId;
        public TerrainCollider TerrainCollider => _terrain;
        public IReadOnlyList<ArenaCoverEntry> Covers => _covers;

        /// <summary>Validates the loaded map and the supplied standing capsule before a round starts.</summary>
        public bool TryPrepare(float standingHeight, float radius, out string failure)
        {
            IsReady = false;
            _coverByCollider.Clear();
            _coverById.Clear();

            if (!IsPositiveFinite(standingHeight) || !IsPositiveFinite(radius) || standingHeight < 2f * radius)
            {
                failure = "Standing capsule dimensions must be finite and positive, with height at least twice the radius.";
                return false;
            }

            if (!isActiveAndEnabled || string.IsNullOrWhiteSpace(_mapId) ||
                _mapVersion < 1 || string.IsNullOrWhiteSpace(_spawnLayoutId) ||
                !_terrain || !_terrain.enabled || !_terrain.gameObject.activeInHierarchy || !_terrain.terrainData ||
                !_spawnA || !_spawnB || _spawnA == _spawnB ||
                !_surface || !_surface.isActiveAndEnabled || !_surface.navMeshData ||
                !BelongsToArena(_terrain.transform) || !BelongsToArena(_spawnA) ||
                !BelongsToArena(_spawnB) || !BelongsToArena(_surface.transform))
            {
                failure = "Arena identifiers, active terrain, two spawn markers, or baked navigation data are missing.";
                return false;
            }

            if (!TryRegisterCovers(out failure))
                return false;

            Physics.SyncTransforms();
            var filter = new NavMeshQueryFilter
            {
                agentTypeID = _surface.agentTypeID,
                areaMask = NavMesh.AllAreas
            };

            if (!TryResolveSpawn(_spawnA, standingHeight, radius, filter, out _preparedA, out var navA, out failure) ||
                !TryResolveSpawn(_spawnB, standingHeight, radius, filter, out _preparedB, out var navB, out failure))
                return false;

            _spawnPath ??= new NavMeshPath();
            if (!NavMesh.CalculatePath(navA, navB, filter, _spawnPath) ||
                _spawnPath.status != NavMeshPathStatus.PathComplete)
            {
                failure = "The two spawn positions do not share a complete navigation path.";
                return false;
            }

            IsReady = true;
            failure = null;
            return true;
        }

        /// <summary>Returns a prepared world-space foot pose for the requested spawn slot.</summary>
        public bool TryGetSpawn(ArenaSpawnSlot slot, out Pose pose)
        {
            pose = default;
            if (!IsReady)
                return false;

            switch (slot)
            {
                case ArenaSpawnSlot.A:
                    pose = _preparedA;
                    return true;
                case ArenaSpawnSlot.B:
                    pose = _preparedB;
                    return true;
                default:
                    return false;
            }
        }

        public bool TryGetCover(Collider collider, out ArenaCoverEntry cover)
        {
            cover = null;
            return IsReady && collider && _coverByCollider.TryGetValue(collider, out cover);
        }

        /// <summary>Looks up a registered cover by its stable ID after the arena is ready.</summary>
        public bool TryGetCoverById(string id, out ArenaCoverEntry cover)
        {
            cover = null;
            return IsReady && !string.IsNullOrEmpty(id) && _coverById.TryGetValue(id, out cover);
        }

        public ArenaCollisionKind GetCollisionKind(Collider collider)
        {
            if (!collider || !BelongsToArena(collider.transform))
                return ArenaCollisionKind.Unclassified;

            string tag = collider.tag;
            if (collider == _terrain || tag == ArenaCoverQuery.TerrainTag)
                return ArenaCollisionKind.Terrain;
            if (ArenaCoverQuery.IsCoverTag(tag))
                return ArenaCollisionKind.Cover;

            switch (tag)
            {
                case "ArenaBoundary":
                    return ArenaCollisionKind.Boundary;
                case "ArenaCenter":
                case "ArenaChunk":
                case "ArenaHelper":
                case "ArenaLadder":
                case "ArenaLight":
                case "ArenaRoute":
                case "ArenaSpawn":
                    return ArenaCollisionKind.Decoration;
                default:
                    return ArenaCollisionKind.Unclassified;
            }
        }

        private bool TryRegisterCovers(out string failure)
        {
            if (_covers == null || _covers.Length == 0)
            {
                failure = "No designated covers are registered.";
                return false;
            }

            foreach (ArenaCoverEntry cover in _covers)
            {
                if (cover == null || string.IsNullOrWhiteSpace(cover.Id) || !_coverById.TryAdd(cover.Id, cover) ||
                    cover.Colliders == null || cover.Colliders.Count == 0)
                {
                    failure = "Each designated cover must have a unique ID and at least one collider.";
                    return false;
                }

                int layer = cover.Layer;
                string tag = cover.Tag;
                foreach (Collider collider in cover.Colliders)
                {
                    if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger ||
                        GetCollisionKind(collider) != ArenaCollisionKind.Cover || !_coverByCollider.TryAdd(collider, cover))
                    {
                        failure = $"Cover '{cover.Id}' contains an invalid or duplicate collider.";
                        return false;
                    }

                    if (collider.gameObject.layer != layer || !collider.CompareTag(tag))
                    {
                        failure = $"Cover '{cover.Id}' has inconsistent Layer or Tag on collider '{collider.name}'.";
                        return false;
                    }
                }
            }

            failure = null;
            return true;
        }

        private bool TryResolveSpawn(Transform marker, float height, float radius, NavMeshQueryFilter filter,
            out Pose pose, out Vector3 navAnchor, out string failure)
        {
            pose = default;
            navAnchor = default;
            Bounds bounds = _terrain.bounds;
            Vector3 position = marker.position;
            var ray = new Ray(new Vector3(position.x, bounds.max.y + .1f, position.z), Vector3.down);
            if (!_terrain.Raycast(ray, out RaycastHit ground, bounds.size.y + .2f))
            {
                failure = $"{marker.name} has no terrain surface below its horizontal position.";
                return false;
            }

            Vector3 feet = ground.point;
            Vector3 lower = feet + Vector3.up * radius;
            Vector3 upper = feet + Vector3.up * (height - radius);
            var physicsScene = gameObject.scene.GetPhysicsScene();
            int count = physicsScene.OverlapCapsule(lower, upper, radius, _overlaps, ~0, QueryTriggerInteraction.Ignore);
            while (count == _overlaps.Length)
            {
                Array.Resize(ref _overlaps, _overlaps.Length * 2);
                count = physicsScene.OverlapCapsule(lower, upper, radius, _overlaps, ~0, QueryTriggerInteraction.Ignore);
            }

            for (int i = 0; i < count; i++)
            {
                Collider collider = _overlaps[i];
                var kind = GetCollisionKind(collider);
                if (kind == ArenaCollisionKind.Cover || kind == ArenaCollisionKind.Boundary)
                {
                    failure = $"{marker.name} overlaps '{collider.name}' with the standing capsule.";
                    return false;
                }
            }

            if (!NavMesh.SamplePosition(feet, out NavMeshHit navHit, NavAnchorDistance, filter))
            {
                failure = $"{marker.name} is outside the baked NavMesh.";
                return false;
            }

            pose = new Pose(feet, marker.rotation);
            navAnchor = navHit.position;
            failure = null;
            return true;
        }

        private bool BelongsToArena(Transform candidate)
        {
            return candidate.gameObject.scene == gameObject.scene && candidate.IsChildOf(transform);
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f && !float.IsInfinity(value);
        }

        private void OnDisable()
        {
            IsReady = false;
        }
    }
}
