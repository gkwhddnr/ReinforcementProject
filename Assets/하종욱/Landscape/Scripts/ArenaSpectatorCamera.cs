using System.Collections.Generic;
using UnityEngine;
using Reinforce.Integration;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ReinProject.Landscape
{
    /// <summary>
    /// OutdoorArena 전용 관전 카메라 매니저.
    /// Play 모드 진입 시 전체 전경(Overview)으로 시작하며,
    /// 단축키(0:전경, 1:A팀, 2:B팀, Tab/방향키:순환)를 통해 다인전(3vs3, 4vs4)에서도
    /// 원하는 전투원의 시점을 3인칭 숄더뷰로 부드럽게 전환 및 추적합니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class ArenaSpectatorCamera : MonoBehaviour
    {
        public enum SpectatorMode
        {
            Overview,
            FollowCombatant
        }

        [Header("카메라 추적 오프셋")]
        [SerializeField] private float _followDistance = 4.5f;
        [SerializeField] private float _followHeight = 2.0f;
        [SerializeField] private float _shoulderOffset = 0.6f;
        [SerializeField] private float _positionSmoothTime = 0.15f;
        [SerializeField] private float _rotationSmoothSpeed = 10f;
        [SerializeField] private LayerMask _collisionMask = ~0;

        [Header("HUD 설정")]
        [SerializeField] private bool _showHUD = false;

        private Camera _camera;
        private SpectatorMode _currentMode = SpectatorMode.Overview;
        private PlayerCombatIntegration _currentTarget;
        private bool _originalOrthographic;

        // 원본 Overview 카메라 위치/회전
        private Vector3 _overviewPosition;
        private Quaternion _overviewRotation;

        // 전투원 목록 (다인전 대응)
        private readonly List<PlayerCombatIntegration> _teamA = new List<PlayerCombatIntegration>();
        private readonly List<PlayerCombatIntegration> _teamB = new List<PlayerCombatIntegration>();
        private readonly List<PlayerCombatIntegration> _allCombatants = new List<PlayerCombatIntegration>();
        private int _teamAIndex = 0;
        private int _teamBIndex = 0;
        private int _allIndex = 0;

        // 보간 속도용 변수
        private Vector3 _currentVelocity;

        public SpectatorMode CurrentMode => _currentMode;
        public PlayerCombatIntegration CurrentTarget => _currentTarget;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _overviewPosition = transform.position;
            _overviewRotation = transform.rotation;
            if (_camera != null)
            {
                _originalOrthographic = _camera.orthographic;
            }
        }

        private void Start()
        {
            RefreshCombatants();
            SetOverviewMode();
        }

        /// <summary>
        /// 씬 내의 모든 Combatant를 검색하여 팀별 목록을 갱신합니다.
        /// </summary>
        public void RefreshCombatants()
        {
            _teamA.Clear();
            _teamB.Clear();
            _allCombatants.Clear();

            PlayerCombatIntegration[] combatants = FindObjectsByType<PlayerCombatIntegration>(FindObjectsSortMode.None);
            foreach (PlayerCombatIntegration c in combatants)
            {
                if (c == null || c.Actor == null) continue;
                _allCombatants.Add(c);
                if (c.Actor.TeamId == 1) _teamA.Add(c);
                else if (c.Actor.TeamId == 2) _teamB.Add(c);
            }

            // ActorId 기준 정렬
            _teamA.Sort((a, b) => a.Actor.ActorId.CompareTo(b.Actor.ActorId));
            _teamB.Sort((a, b) => a.Actor.ActorId.CompareTo(b.Actor.ActorId));
            _allCombatants.Sort((a, b) => a.Actor.ActorId.CompareTo(b.Actor.ActorId));
        }

        private void Update()
        {
            HandleInput();
        }

        private void HandleInput()
        {
            // [0] 또는 [Esc]: Overview 전경 복귀
            if (IsKeyPressed(KeyCode.Alpha0, KeyCode.Escape))
            {
                SetOverviewMode();
            }
            // [1]: Team A 전투원 선택 및 순환
            else if (IsKeyPressed(KeyCode.Alpha1, KeyCode.Keypad1))
            {
                CycleTeamCombatant(1);
            }
            // [2]: Team B 전투원 선택 및 순환
            else if (IsKeyPressed(KeyCode.Alpha2, KeyCode.Keypad2))
            {
                CycleTeamCombatant(2);
            }
            // [Tab] 또는 [RightArrow]: 전체 전투원 대상 다음 순환
            else if (IsKeyPressed(KeyCode.Tab, KeyCode.RightArrow))
            {
                CycleAllCombatants(1);
            }
            // [LeftArrow]: 전체 전투원 대상 이전 순환
            else if (IsKeyPressed(KeyCode.LeftArrow))
            {
                CycleAllCombatants(-1);
            }
        }

        private void LateUpdate()
        {
            if (_currentMode == SpectatorMode.Overview)
            {
                // Overview 모드: 부드럽게 기본 위치 복귀
                transform.position = Vector3.Lerp(transform.position, _overviewPosition, Time.deltaTime * 5f);
                transform.rotation = Quaternion.Slerp(transform.rotation, _overviewRotation, Time.deltaTime * 5f);
                return;
            }

            if (_currentTarget == null || _currentTarget.Actor == null)
            {
                SetOverviewMode();
                return;
            }

            // 대상이 사망한 경우: 같은 팀의 다른 생존자 찾기, 없으면 Overview 복귀
            if (!_currentTarget.Actor.Alive)
            {
                PlayerCombatIntegration survivor = FindLivingCombatantInTeam(_currentTarget.Actor.TeamId);
                if (survivor != null && survivor != _currentTarget)
                {
                    _currentTarget = survivor;
                }
                else
                {
                    // 생존자가 없으면 전경 뷰 복귀
                    SetOverviewMode();
                    return;
                }
            }

            // 3인칭 숄더뷰 추적 계산
            Transform targetT = _currentTarget.transform;
            Vector3 focusPoint = targetT.position + Vector3.up * _followHeight;
            Vector3 desiredPos = focusPoint - targetT.forward * _followDistance + targetT.right * _shoulderOffset;

            // 카메라 클리핑/가림 방지 (벽이나 지형에 파고들지 않도록 레이캐스트 보정)
            Vector3 rayDir = desiredPos - focusPoint;
            float targetDistance = rayDir.magnitude;
            if (Physics.Raycast(focusPoint, rayDir.normalized, out RaycastHit hit, targetDistance, _collisionMask, QueryTriggerInteraction.Ignore))
            {
                desiredPos = focusPoint + rayDir.normalized * Mathf.Max(0.5f, hit.distance - 0.2f);
            }

            // 부드러운 위치 및 회전 추적
            transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref _currentVelocity, _positionSmoothTime);

            Quaternion targetRotation = Quaternion.LookRotation((focusPoint - transform.position).normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * _rotationSmoothSpeed);
        }

        public void SetOverviewMode()
        {
            _currentMode = SpectatorMode.Overview;
            _currentTarget = null;
            if (_camera != null)
            {
                _camera.orthographic = _originalOrthographic;
            }
        }

        public void SetTarget(PlayerCombatIntegration combatant)
        {
            if (combatant == null)
            {
                SetOverviewMode();
                return;
            }

            _currentTarget = combatant;
            _currentMode = SpectatorMode.FollowCombatant;
            if (_camera != null)
            {
                _camera.orthographic = false; // 3인칭 추적 시 원근감(Perspective) 제공
                _camera.fieldOfView = 60f;
            }
        }

        private void CycleTeamCombatant(int teamId)
        {
            List<PlayerCombatIntegration> list = teamId == 1 ? _teamA : _teamB;
            if (list.Count == 0) RefreshCombatants();
            if (list.Count == 0) return;

            ref int index = ref (teamId == 1 ? ref _teamAIndex : ref _teamBIndex);

            // 현재 이미 같은 팀 액터를 보고 있으면 다음 인덱스로
            if (_currentMode == SpectatorMode.FollowCombatant && _currentTarget != null && _currentTarget.Actor.TeamId == teamId)
            {
                index = (index + 1) % list.Count;
            }

            // 생존자 우선 검색
            for (int i = 0; i < list.Count; i++)
            {
                int checkIndex = (index + i) % list.Count;
                if (list[checkIndex] != null && list[checkIndex].Actor.Alive)
                {
                    index = checkIndex;
                    SetTarget(list[checkIndex]);
                    return;
                }
            }

            // 생존자가 없어도 해당 팀 액터 지정
            SetTarget(list[index % list.Count]);
        }

        private void CycleAllCombatants(int direction)
        {
            if (_allCombatants.Count == 0) RefreshCombatants();
            if (_allCombatants.Count == 0) return;

            _allIndex = (_allIndex + direction + _allCombatants.Count) % _allCombatants.Count;

            // 생존자 우선 검색
            for (int i = 0; i < _allCombatants.Count; i++)
            {
                int checkIndex = (_allIndex + i * direction + _allCombatants.Count * 2) % _allCombatants.Count;
                if (_allCombatants[checkIndex] != null && _allCombatants[checkIndex].Actor.Alive)
                {
                    _allIndex = checkIndex;
                    SetTarget(_allCombatants[checkIndex]);
                    return;
                }
            }

            SetTarget(_allCombatants[_allIndex]);
        }

        private PlayerCombatIntegration FindLivingCombatantInTeam(int teamId)
        {
            List<PlayerCombatIntegration> list = teamId == 1 ? _teamA : _teamB;
            foreach (PlayerCombatIntegration c in list)
            {
                if (c != null && c.Actor != null && c.Actor.Alive)
                    return c;
            }
            return null;
        }

        private bool IsKeyPressed(params KeyCode[] legacyKeys)
        {
            foreach (KeyCode k in legacyKeys)
            {
                if (Input.GetKeyDown(k)) return true;
            }

#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                foreach (KeyCode k in legacyKeys)
                {
                    switch (k)
                    {
                        case KeyCode.Alpha0: if (kb.digit0Key.wasPressedThisFrame) return true; break;
                        case KeyCode.Alpha1: if (kb.digit1Key.wasPressedThisFrame) return true; break;
                        case KeyCode.Alpha2: if (kb.digit2Key.wasPressedThisFrame) return true; break;
                        case KeyCode.Escape: if (kb.escapeKey.wasPressedThisFrame) return true; break;
                        case KeyCode.Tab: if (kb.tabKey.wasPressedThisFrame) return true; break;
                        case KeyCode.LeftArrow: if (kb.leftArrowKey.wasPressedThisFrame) return true; break;
                        case KeyCode.RightArrow: if (kb.rightArrowKey.wasPressedThisFrame) return true; break;
                    }
                }
            }
#endif
            return false;
        }

        private void OnGUI()
        {
            if (!_showHUD) return;

            const int width = 360;
            const int height = 135;
            const int x = 10;
            const int y = 10;

            GUI.Box(new Rect(x, y, width, height), "[Arena 다인전 관전 카메라]");
            GUILayout.BeginArea(new Rect(x + 10, y + 25, width - 20, height - 30));

            if (_currentMode == SpectatorMode.Overview)
            {
                GUILayout.Label("<b>현재 시점: <color=yellow>전체 전경 뷰 (Overview)</color></b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 13 });
                GUILayout.Label($"등록된 전투원: Team A ({_teamA.Count}명) | Team B ({_teamB.Count}명)");
            }
            else if (_currentTarget != null && _currentTarget.Actor != null)
            {
                string teamColor = _currentTarget.Actor.TeamId == 1 ? "cyan" : "orange";
                string teamName = _currentTarget.Actor.TeamId == 1 ? "Team A" : "Team B";
                string hpColor = _currentTarget.Actor.Health > 50f ? "lime" : (_currentTarget.Actor.Health > 20f ? "yellow" : "red");

                GUILayout.Label($"<b>현재 관전: <color={teamColor}>{teamName} - Actor #{_currentTarget.Actor.ActorId}</color></b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 13 });
                GUILayout.Label($"체력: <color={hpColor}><b>{_currentTarget.Actor.Health:F0} / {_currentTarget.Actor.MaxHealth:F0}</b></color> | 생존: {_currentTarget.Actor.Alive}");
                
                string targetStr = _currentTarget.HasTarget ? $"<color=red>적 포착 (#{_currentTarget.TargetId})</color>" : "<color=gray>탐색 중</color>";
                float acc = _currentTarget.CurrentAccuracy * 100f;
                GUILayout.Label($"전투 상태: {targetStr} | 실시간 명중률: <b>{acc:F1}%</b>", new GUIStyle(GUI.skin.label) { richText = true });
            }

            GUILayout.Space(4);
            GUILayout.Label("단축키: <b>[0: 전경] [1: A팀] [2: B팀] [Tab: 순환]</b>", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.EndArea();
        }
    }
}
