using System.Collections;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace ReinProject.Landscape
{
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class ArenaLadderTraversal : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float _climbSpeed = 2.4f;

        private NavMeshAgent _agent;
        private Coroutine _traversal;
        private bool _previousAutoTraverse;
        private bool _previousUpdatePosition;
        private bool _usingDefaultTraversal;

        public bool IsClimbing => _traversal != null;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
        }

        private void OnEnable()
        {
            _previousAutoTraverse = _agent.autoTraverseOffMeshLink;
            _agent.autoTraverseOffMeshLink = false;
        }

        private void OnDisable()
        {
            if (_traversal != null)
            {
                StopCoroutine(_traversal);
                _traversal = null;
                _agent.updatePosition = _previousUpdatePosition;
            }
            _usingDefaultTraversal = false;
            _agent.autoTraverseOffMeshLink = _previousAutoTraverse;
        }

        private void Update()
        {
            if (_traversal != null || !_agent.isActiveAndEnabled)
                return;

            if (!_agent.isOnOffMeshLink)
            {
                if (_usingDefaultTraversal)
                {
                    _agent.autoTraverseOffMeshLink = false;
                    _usingDefaultTraversal = false;
                }
                return;
            }

            if (_usingDefaultTraversal)
                return;

            OffMeshLinkData data = _agent.currentOffMeshLinkData;
            if (data.owner is NavMeshLink link && link.TryGetComponent(out ArenaLadder ladder))
            {
                _traversal = StartCoroutine(Climb(data, ladder));
            }
            else if (_previousAutoTraverse)
            {
                _usingDefaultTraversal = true;
                _agent.autoTraverseOffMeshLink = true;
            }
        }

        private IEnumerator Climb(OffMeshLinkData data, ArenaLadder ladder)
        {
            _previousUpdatePosition = _agent.updatePosition;
            _agent.updatePosition = false;
            float offset = _agent.baseOffset;
            Vector3 start = data.startPos + Vector3.up * offset;
            Vector3 finish = data.endPos + Vector3.up * offset;
            if ((transform.position - finish).sqrMagnitude < (transform.position - start).sqrMagnitude)
                (start, finish) = (finish, start);

            bool climbingUp = start.y <= finish.y;
            Vector3 entry = climbingUp ? ladder.ClimbBottom : ladder.ClimbTop;
            Vector3 exit = climbingUp ? ladder.ClimbTop : ladder.ClimbBottom;
            entry.y = start.y;
            exit.y = finish.y;
            yield return MoveTo(entry);
            yield return MoveTo(exit);
            yield return MoveTo(finish);

            if (_agent.isActiveAndEnabled && _agent.isOnOffMeshLink)
                _agent.CompleteOffMeshLink();
            _agent.updatePosition = _previousUpdatePosition;
            _traversal = null;
        }

        private IEnumerator MoveTo(Vector3 destination)
        {
            while (_agent.isActiveAndEnabled && _agent.isOnOffMeshLink &&
                (transform.position - destination).sqrMagnitude > 0.0001f)
            {
                transform.position = Vector3.MoveTowards(transform.position, destination, _climbSpeed * Time.deltaTime);
                yield return null;
            }
        }
    }
}
