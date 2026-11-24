using UnityEngine;

#if UNITY_EDITOR
using Unity.AI.Navigation;
using UnityEngine.AI;
using UnityEngine.InputSystem;
#endif

namespace ReinProject.Landscape
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ArenaLadderTraversal))]
    public sealed class ArenaPlayerTestControls : MonoBehaviour
    {
#if UNITY_EDITOR
        [SerializeField] private InputActionAsset _inputActions;
        [SerializeField] private Camera _overviewCamera;
        [SerializeField] private NavMeshLink[] _ladders;

        private const float LadderReach = 2.25f;
        private const float CameraDistance = 5.5f;
        private const float LookSensitivity = 0.2f;

        private NavMeshAgent _agent;
        private ArenaLadderTraversal _traversal;
        private InputAction _move;
        private bool _manual;
        private bool _followingLadder;
        private bool _previousUpdateRotation;
        private bool _previousStopped;
        private bool _previousOrthographic;
        private Vector3 _previousCameraPosition;
        private Quaternion _previousCameraRotation;
        private float _yaw;
        private float _pitch;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _traversal = GetComponent<ArenaLadderTraversal>();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f8Key.wasPressedThisFrame && !IsTraversingLadder())
                SetManual(!_manual);

            if (!_manual || !_agent.isOnNavMesh)
                return;

            UpdateLook();
            if (IsTraversingLadder())
                return;

            if (keyboard != null && keyboard.eKey.wasPressedThisFrame && TryRouteThroughLadder())
                return;

            if (_followingLadder)
            {
                if (_agent.pathPending || (_agent.hasPath &&
                    _agent.pathStatus == NavMeshPathStatus.PathComplete &&
                    _agent.remainingDistance > Mathf.Max(_agent.stoppingDistance, 0.2f)))
                    return;

                StopLadderRoute();
            }

            Vector2 input = Vector2.ClampMagnitude(_move.ReadValue<Vector2>(), 1f);
            if (input.sqrMagnitude < 0.0001f)
                return;

            Vector3 direction = Quaternion.Euler(0f, _yaw, 0f) * new Vector3(input.x, 0f, input.y);
            _agent.Move(direction * (_agent.speed * Time.deltaTime));
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(direction), _agent.angularSpeed * Time.deltaTime);
        }

        private void LateUpdate()
        {
            if (!_manual)
                return;

            Vector3 focus = transform.position + Vector3.up * 0.55f;
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 cameraPosition = focus - rotation * Vector3.forward * CameraDistance;
            if (ArenaCoverQuery.TryGetOccluder(gameObject.scene, focus, cameraPosition, out _, out RaycastHit hit))
                cameraPosition = focus + (cameraPosition - focus).normalized * Mathf.Max(0.1f, hit.distance - 0.18f);

            _overviewCamera.transform.SetPositionAndRotation(cameraPosition, rotation);
        }

        private void UpdateLook()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.rightButton.isPressed)
                return;

            Vector2 delta = mouse.delta.ReadValue() * LookSensitivity;
            _yaw += delta.x;
            _pitch = Mathf.Clamp(_pitch - delta.y, 8f, 70f);
        }

        private void SetManual(bool enable)
        {
            if (_manual == enable)
                return;

            if (enable)
            {
                var action = _inputActions != null ? _inputActions.FindAction("Player/Move") : null;
                if (action == null || _overviewCamera == null || !_agent.isOnNavMesh)
                {
                    Debug.LogWarning("Arena test controls need Player/Move, the arena camera, and a Player on the NavMesh.", this);
                    return;
                }

                _move = action.Clone();
                _move.Enable();
                _previousUpdateRotation = _agent.updateRotation;
                _previousStopped = _agent.isStopped;
                _agent.ResetPath();
                _agent.updateRotation = false;
                _agent.isStopped = false;
                _previousCameraPosition = _overviewCamera.transform.position;
                _previousCameraRotation = _overviewCamera.transform.rotation;
                _previousOrthographic = _overviewCamera.orthographic;
                _overviewCamera.orthographic = false;
                _yaw = transform.eulerAngles.y;
                _pitch = 24f;
            }
            else
            {
                _move.Disable();
                _move.Dispose();
                _move = null;
                StopLadderRoute();
                if (_agent != null)
                {
                    _agent.updateRotation = _previousUpdateRotation;
                    if (_agent.isOnNavMesh)
                        _agent.isStopped = _previousStopped;
                }

                if (_overviewCamera != null)
                {
                    _overviewCamera.orthographic = _previousOrthographic;
                    _overviewCamera.transform.SetPositionAndRotation(_previousCameraPosition, _previousCameraRotation);
                }
            }

            _manual = enable;
        }

        private bool IsTraversingLadder()
        {
            return _traversal.IsClimbing || (_agent.isOnNavMesh && _agent.isOnOffMeshLink);
        }

        private bool TryRouteThroughLadder()
        {
            Vector3 feet = transform.position - Vector3.up * _agent.baseOffset;
            float nearest = LadderReach * LadderReach;
            Vector3 destination = default;
            bool found = false;
            foreach (NavMeshLink link in _ladders)
            {
                if (link == null || !link.isActiveAndEnabled || !link.activated || link.agentTypeID != _agent.agentTypeID)
                    continue;

                Transform origin = link.transform;
                Vector3 start = link.startTransform != null ? link.startTransform.position :
                    origin.position + origin.rotation * link.startPoint;
                Vector3 end = link.endTransform != null ? link.endTransform.position :
                    origin.position + origin.rotation * link.endPoint;

                float fromStart = (feet - start).sqrMagnitude;
                float fromEnd = (feet - end).sqrMagnitude;
                if (fromStart < nearest)
                {
                    nearest = fromStart;
                    destination = end;
                    found = true;
                }
                if (link.bidirectional && fromEnd < nearest)
                {
                    nearest = fromEnd;
                    destination = start;
                    found = true;
                }
            }

            if (!found)
                return false;

            var filter = new NavMeshQueryFilter { agentTypeID = _agent.agentTypeID, areaMask = _agent.areaMask };
            if (!NavMesh.SamplePosition(destination, out NavMeshHit landing, 0.6f, filter) ||
                !_agent.SetDestination(landing.position))
                return false;

            _followingLadder = true;
            return true;
        }

        private void StopLadderRoute()
        {
            if (_followingLadder && _agent != null && _agent.isOnNavMesh && !_agent.isOnOffMeshLink)
                _agent.ResetPath();
            _followingLadder = false;
        }

        private void OnDisable()
        {
            SetManual(false);
        }
#endif
    }
}
