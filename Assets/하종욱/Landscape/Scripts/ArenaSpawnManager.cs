using UnityEngine;
using Reinforce.Integration;
using Reinforce.Movement;
using Reinforcement.Shooting;

namespace ReinProject.Landscape
{
    /// <summary>
    /// OutdoorArena 전용 에피소드 스폰 매니저.
    /// 에피소드 시작 시 ML-Agents 조작 가능한 Player_Combat_Integrated 프리팹을
    /// 지정된 스폰 전략(현재: 고정 청크)에 따라 소환/배치하고 팀 ID 및 상호 상대 목록을 연결합니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ArenaEnvironment))]
    public class ArenaSpawnManager : MonoBehaviour
    {
        [Header("전투원 프리팹 (통합 이동+사격)")]
        [SerializeField] private GameObject _combatantPrefab;

        [Header("스폰 전략 설정")]
        [SerializeField] private SpawnStrategyType _strategyType = SpawnStrategyType.FixedChunk;
        [SerializeField] private bool _autoSpawnOnStart = true;
        [SerializeField] private bool _enableDuelBots = true; // 테스트 자동 교전 봇 부착 여부

        [Header("에피소드 정보")]
        [SerializeField] private int _currentEpisode = 1;
        [SerializeField] private int _initialSeed = 1301;

        private ArenaEnvironment _arena;
        private PlayerCombatIntegration _instanceA;
        private PlayerCombatIntegration _instanceB;
        private ISpawnStrategy _spawnStrategy;

        public PlayerCombatIntegration CombatantA => _instanceA;
        public PlayerCombatIntegration CombatantB => _instanceB;
        public int CurrentEpisode => _currentEpisode;

        private void Awake()
        {
            _arena = GetComponent<ArenaEnvironment>();
            UpdateStrategyInstance();
        }

        private void Start()
        {
            if (_autoSpawnOnStart)
            {
                SpawnOrResetEpisode();
            }
        }

        private void Update()
        {
            // [R] 키 입력 시 에피소드 즉시 리셋 및 재스폰
            if (Input.GetKeyDown(KeyCode.R))
            {
                _currentEpisode++;
                _initialSeed += 7;
                SpawnOrResetEpisode();
            }
        }

        public void SetStrategy(SpawnStrategyType strategyType)
        {
            _strategyType = strategyType;
            UpdateStrategyInstance();
        }

        private void UpdateStrategyInstance()
        {
            switch (_strategyType)
            {
                case SpawnStrategyType.FixedChunk:
                    _spawnStrategy = new FixedChunkSpawnStrategy();
                    break;
                case SpawnStrategyType.RuleBasedRandomChunk:
                    _spawnStrategy = new RuleBasedRandomChunkSpawnStrategy(2, _initialSeed);
                    break;
            }
        }

        /// <summary>
        /// 새로운 에피소드를 위해 전투원들을 스폰하거나 기존 인스턴스를 재설정합니다.
        /// </summary>
        public bool SpawnOrResetEpisode()
        {
            if (_combatantPrefab == null)
            {
                Debug.LogError("ArenaSpawnManager: CombatantPrefab is not assigned!", this);
                return false;
            }

            var prefabCombat = _combatantPrefab.GetComponent<PlayerCombatIntegration>();
            MovementSettingsSO settings = (prefabCombat != null && prefabCombat.Movement != null) ? prefabCombat.Movement.Settings : null;
            float standingHeight = settings != null ? settings.standingHeight : 2.0f;
            float radius = settings != null ? settings.colliderRadius : 0.5f;

            if (_spawnStrategy == null) UpdateStrategyInstance();

            if (!_spawnStrategy.TryGetSpawns(_arena, standingHeight, radius, out Pose spawnA, out Pose spawnB, out string failure))
            {
                Debug.LogError($"ArenaSpawnManager: Failed to get spawn poses: {failure}", this);
                return false;
            }

            // 인스턴스가 없으면 새로 생성, 있으면 위치 리셋
            if (_instanceA == null)
            {
                GameObject objA = Instantiate(_combatantPrefab, spawnA.position, spawnA.rotation, transform);
                _instanceA = objA.GetComponent<PlayerCombatIntegration>();
                _instanceA.name = "Team A Combatant (Dynamic)";
            }
            else
            {
                _instanceA.SetSpawnPose(spawnA);
                _instanceA.ResetCombatEpisode(_currentEpisode, _initialSeed);
            }

            if (_instanceB == null)
            {
                GameObject objB = Instantiate(_combatantPrefab, spawnB.position, spawnB.rotation, transform);
                _instanceB = objB.GetComponent<PlayerCombatIntegration>();
                _instanceB.name = "Team B Combatant (Dynamic)";
            }
            else
            {
                _instanceB.SetSpawnPose(spawnB);
                _instanceB.ResetCombatEpisode(_currentEpisode, _initialSeed + 1);
            }

            // 팀 ID 및 Actor ID 구성 (Team A: 1, Team B: 2)
            ConfigureCombatant(_instanceA, spawnA, 1, 1);
            ConfigureCombatant(_instanceB, spawnB, 2, 2);

            // 상호 상대 등록
            _instanceA.SetOpponents(new ShotActor[] { _instanceB.Actor });
            _instanceB.SetOpponents(new ShotActor[] { _instanceA.Actor });

            // 테스트용 간이 교전 봇 연결 (선택)
            if (_enableDuelBots)
            {
                SetupDuelBot(_instanceA, _instanceB);
                SetupDuelBot(_instanceB, _instanceA);
            }

            // 관전 카메라 갱신
            var spectator = FindAnyObjectByType<ArenaSpectatorCamera>();
            if (spectator != null)
            {
                spectator.RefreshCombatants();
            }

            Debug.Log($"ArenaSpawnManager: Episode #{_currentEpisode} started. Team A at {spawnA.position}, Team B at {spawnB.position}");
            return true;
        }

        private static void ConfigureCombatant(PlayerCombatIntegration combatant, Pose spawn, int actorId, int teamId)
        {
            combatant.SetSpawnPose(spawn);
            combatant.Actor.SetIdentity(actorId, teamId);

            if (combatant.ShootingSettings != null)
            {
                combatant.ShootingSettings.useDynamicAccuracy = true;
            }

            if (combatant.TryGetComponent(out PlayerAgentInputBridge bridge))
            {
                combatant.Movement.SetInputProvider(bridge);
            }
        }

        private static void SetupDuelBot(PlayerCombatIntegration attacker, PlayerCombatIntegration target)
        {
            if (!attacker.TryGetComponent(out ArenaDuelBot bot))
            {
                bot = attacker.gameObject.AddComponent<ArenaDuelBot>();
            }
            bot.SetOpponent(target);
        }
    }
}
