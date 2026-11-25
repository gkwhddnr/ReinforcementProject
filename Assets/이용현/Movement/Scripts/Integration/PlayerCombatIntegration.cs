using UnityEngine;
using Reinforce.Movement;
using Reinforcement.Shooting;
using Reinforcement.Shooting.Demo;

namespace Reinforce.Integration
{
    /// <summary>
    /// PlayerMovementController와 ShootingWeapon을 결합하여,
    /// 하나의 캐릭터에서 이동(HFSM), 수평 회전, 오토에임 사격, 체력 및 리셋을 통합 제어하는 코어 컴포넌트
    /// </summary>
    [RequireComponent(typeof(PlayerMovementController))]
    [RequireComponent(typeof(PlayerShotActor))]
    public class PlayerCombatIntegration : MonoBehaviour
    {
        [Header("Weapon & Shooting Setup")]
        [SerializeField] private ShootingWeapon weapon;
        [SerializeField] private TrialVision vision;
        [SerializeField] private ShootingSettings shootingSettings = new ShootingSettings();
        [SerializeField] private Transform eye;
        [SerializeField] private Transform mount;
        [SerializeField] private Transform muzzle;

        [Header("Tracer Visuals")]
        [SerializeField] private bool showTracers = true;
        [SerializeField] private Color tracerColor = new Color(0.1f, 0.8f, 1.0f, 1f);

        [Header("Opponents for Vision")]
        [SerializeField] private ShotActor[] opponents;

        [Header("Simulation Timing")]
        [SerializeField] private int episodeId = 1;
        [SerializeField] private int rngSeed = 731;

        [Header("Debug HUD")]
        [SerializeField] private bool showCombatHUD = true;
        [SerializeField] private bool enableDemoReset = true;
        [SerializeField] private bool stationaryAimOnly;

        private PlayerMovementController movementController;
        private PlayerShotActor shotActor;
        private LineRenderer lineRenderer;
        private float lineUntilTime = 0f;
        private int tickCount = 0;
        private double elapsedSimTime = 0d;
        private string lastShotLog = "No shots yet";
        private Vector3 initialSpawnPos;
        private Quaternion initialSpawnRot;

        public PlayerMovementController Movement => movementController;
        public PlayerShotActor Actor => shotActor;
        public ShootingWeapon Weapon => weapon;
        public string LastShotLog => lastShotLog;

        #region ML-Agents Observations & Telemetry (김현석 / 조원별 전달 요청서 규격)

        // [이용현 담당 - 이동·회전·자세]
        public Vector3 Position => movementController != null ? movementController.Position : transform.position;
        public Quaternion Rotation => movementController != null ? movementController.Rotation : transform.rotation;
        public Vector3 Velocity => movementController != null ? movementController.Velocity : Vector3.zero;
        public float MaximumSpeed => movementController != null ? movementController.MaximumSpeed : 6.5f;
        public PostureState Posture => movementController != null ? movementController.Posture : PostureState.Standing;
        public bool IsMovementReady => movementController != null && movementController.IsReady;

        // [이승재 담당 - 사격·오토에임]
        public bool HasTarget => weapon != null && weapon.Target != null;
        public int TargetId => weapon != null && weapon.Target != null ? weapon.Target.ActorId : -1;
        public Vector3 TargetAimPoint => (weapon != null && weapon.Target != null) ? weapon.Target.AimPoint : Vector3.zero;
        public float MaxDetectionRange => shootingSettings != null ? shootingSettings.range : 20f;
        public int TargetUpdateTick => tickCount;
        public bool CanFire => weapon != null && weapon.CanFire(elapsedSimTime);
        public bool IsWeaponReady => weapon != null && weapon.isActiveAndEnabled;
        public Vector3 MuzzlePosition => muzzle != null ? muzzle.position : transform.position;
        public Transform MuzzleTransform => muzzle;
        public float EffectiveRange => shootingSettings != null ? shootingSettings.range : 20f;
        public float CooldownRemaining => weapon != null ? (float)weapon.RemainingCooldown(elapsedSimTime) : 0f;
        public float CurrentAccuracy => weapon != null ? weapon.CurrentAccuracy(elapsedSimTime) : (shootingSettings != null ? shootingSettings.accuracy : 0f);
        public float TargetLockDuration => weapon != null ? (float)weapon.TargetLockDuration(elapsedSimTime) : 0f;
        public float NormalizedHealth => shotActor != null ? shotActor.Health / shotActor.MaxHealth : 0f;
        public ShootingSettings ShootingSettings => shootingSettings;

        #endregion

        private void Awake()
        {
            movementController = GetComponent<PlayerMovementController>();
            shotActor = GetComponent<PlayerShotActor>();

            initialSpawnPos = transform.position;
            initialSpawnRot = transform.rotation;

            EnsureRigTransforms();
            EnsureWeaponComponents();
            EnsureTracer();
        }

        private void Start()
        {
            InitializeRig();
            if (stationaryAimOnly)
                weapon.ResetEpisode(episodeId, rngSeed);
            else
                ResetCombatEpisode(episodeId, rngSeed);
        }

        private void EnsureRigTransforms()
        {
            if (eye == null)
            {
                Transform found = transform.Find("Eye");
                if (found != null) eye = found;
                else
                {
                    GameObject go = new GameObject("Eye");
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = new Vector3(0f, 1.82f, 0f);
                    eye = go.transform;
                }
            }

            if (mount == null)
            {
                Transform found = transform.Find("Mount");
                if (found != null) mount = found;
                else
                {
                    GameObject go = new GameObject("Mount");
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = new Vector3(0.48f, 1.24f, 0f);
                    mount = go.transform;
                }
            }

            if (muzzle == null)
            {
                Transform found = mount != null ? mount.Find("Muzzle") : transform.Find("Muzzle");
                if (found != null) muzzle = found;
                else
                {
                    GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "Muzzle";
                    go.transform.SetParent(mount != null ? mount : transform, false);
                    go.transform.localPosition = new Vector3(0f, 0f, 0.46f);
                    go.transform.localScale = new Vector3(0.12f, 0.12f, 0.35f);
                    Destroy(go.GetComponent<Collider>()); // 총구 자체 콜라이더 제거
                    muzzle = go.transform;
                }
            }
        }

        private void EnsureWeaponComponents()
        {
            if (weapon == null)
            {
                weapon = GetComponent<ShootingWeapon>();
                if (weapon == null) weapon = gameObject.AddComponent<ShootingWeapon>();
            }

            if (vision == null)
            {
                vision = GetComponent<TrialVision>();
                if (vision == null) vision = gameObject.AddComponent<TrialVision>();
            }
        }

        private void EnsureTracer()
        {
            GameObject tracerObj = new GameObject(name + "_Tracer");
            tracerObj.transform.SetParent(transform, false);
            lineRenderer = tracerObj.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = true;
            lineRenderer.positionCount = 2;
            lineRenderer.widthMultiplier = 0.05f;

            Shader urpShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (urpShader == null) urpShader = Shader.Find("Sprites/Default");
            Material mat = new Material(urpShader);
            mat.color = tracerColor;
            lineRenderer.material = mat;
            lineRenderer.enabled = false;
        }

        public void InitializeRig()
        {
            weapon.Configure(shotActor, eye, mount, muzzle, shootingSettings);
            weapon.ShotResolved -= OnShotResolved;
            weapon.ShotResolved += OnShotResolved;

            if (opponents != null && opponents.Length > 0)
            {
                vision.Configure(shotActor, eye, opponents);
            }
        }

        public void SetOpponents(params ShotActor[] newOpponents)
        {
            opponents = newOpponents;
            if (vision != null && eye != null && shotActor != null)
            {
                vision.Configure(shotActor, eye, opponents);
            }
        }

        public void SetSpawnPose(Pose spawn)
        {
            initialSpawnPos = spawn.position;
            initialSpawnRot = spawn.rotation;
        }

        public void ResetCombatEpisode(int newEpisodeId, int seed)
        {
            episodeId = newEpisodeId;
            rngSeed = seed;
            tickCount = 0;
            elapsedSimTime = 0d;
            lastShotLog = "Episode started";

            shotActor.ResetHealth();
            movementController.ResetForEpisode(initialSpawnPos, initialSpawnRot);
            weapon.ResetEpisode(episodeId, seed);
            if (lineRenderer != null) lineRenderer.enabled = false;
        }

        private void Update()
        {
            if (Time.time >= lineUntilTime && lineRenderer != null)
            {
                lineRenderer.enabled = false;
            }

            // T 키 입력 시 동적 명중률 시스템 실시간 토글
            if (Input.GetKeyDown(KeyCode.T) && shootingSettings != null)
            {
                shootingSettings.useDynamicAccuracy = !shootingSettings.useDynamicAccuracy;
            }

            // R 키 입력 시 통합 리셋 수행
            if (!stationaryAimOnly && enableDemoReset && Input.GetKeyDown(KeyCode.R))
            {
                ResetCombatEpisode(episodeId + 1, rngSeed + 1);
            }
        }

        private void FixedUpdate()
        {
            if (!shotActor.Alive || movementController.IsDead) return;

            // 1. 발사 입력 주입 (키보드 또는 ML-Agents 브릿지의 IsFire 전달)
            bool fireCommand = !stationaryAimOnly && movementController.CurrentInput.IsFire;
            weapon.SetCommand(episodeId, fireCommand);

            // 2. 물리 동기화 및 시야 샘플링
            Physics.SyncTransforms();
            VisibilityFrame frame = vision.Sample(episodeId, tickCount, shootingSettings.mask);
            if (stationaryAimOnly && weapon.Target)
            {
                foreach (ShotActor visible in frame.Candidates)
                {
                    if (visible != weapon.Target)
                        continue;

                    movementController.RotateTowards(visible.AimPoint - transform.position);
                    break;
                }
            }

            // 3. 무기 틱 처리 (오토에임 조준 정렬, 쿨다운 검사, 발사 및 데미지 전달)
            weapon.Tick(episodeId, tickCount, elapsedSimTime, frame);

            tickCount++;
            elapsedSimTime += Time.fixedDeltaTime;
        }

        private void OnShotResolved(ShotResult shot)
        {
            string hitDesc = shot.Contact.Kind == ContactKind.Obstacle ? $"Obstacle ({shot.Contact.ObstacleId})" :
                shot.Contact.Kind == ContactKind.Actor ? $"{shot.Region} (Damage: {shot.Damage.Applied:0})" : "Miss";

            float accPercent = CurrentAccuracy * 100f;
            lastShotLog = $"[Shot #{shot.Sequence}] -> {hitDesc} | 당시 명중률: {accPercent:F1}% | Target: #{shot.TargetId}";

            if (showTracers && lineRenderer != null && shootingSettings.tracerSeconds > 0f)
            {
                lineRenderer.SetPosition(0, shot.Origin);
                lineRenderer.SetPosition(1, shot.Contact.Point);
                lineRenderer.enabled = true;
                lineUntilTime = Time.time + shootingSettings.tracerSeconds;
            }
        }

        private void OnGUI()
        {
            if (!showCombatHUD) return;

            int width = 480;
            int height = 210;
            int x = Screen.width - width - 10;
            int y = 10;

            GUI.Box(new Rect(x, y, width, height), $"[통합 사격 HUD - {name}]");
            GUILayout.BeginArea(new Rect(x + 10, y + 25, width - 20, height - 30));

            string hpColor = shotActor.Health > 50f ? "lime" : (shotActor.Health > 20f ? "yellow" : "red");
            GUILayout.Label($"체력 (HP): <color={hpColor}><b>{shotActor.Health:F0} / {shotActor.MaxHealth:F0}</b></color> (생존: {shotActor.Alive})", new GUIStyle(GUI.skin.label) { richText = true });

            string targetInfo = HasTarget ? $"<color=cyan>Target #{TargetId}</color>" : "<color=gray>No Target (Searching FOV)</color>";
            GUILayout.Label($"오토에임 표적: {targetInfo} | 발사상태: {weapon.Status}", new GUIStyle(GUI.skin.label) { richText = true });

            // [동적 명중률 실시간 정보 표시]
            if (shootingSettings != null && shootingSettings.useDynamicAccuracy)
            {
                float lockTime = TargetLockDuration;
                float currentAcc = CurrentAccuracy * 100f;
                string lockStatus = lockTime < shootingSettings.reactionDelay
                    ? $"<color=red><b>식별 지연 중 ({lockTime:F2}s / {shootingSettings.reactionDelay:F2}s - 사격 불가)</b></color>"
                    : $"<color=lime><b>조준 집중 중 ({lockTime:F2}s 유지)</b></color>";

                GUILayout.Label($"동적 명중률([T]토글): <color=cyan><b>ON</b></color> | {lockStatus}", new GUIStyle(GUI.skin.label) { richText = true });
                GUILayout.Label($"실시간 명중률: <color=yellow><b>{currentAcc:F1}%</b></color> (0.22s 25% → 1.0s 80% 포화 곡선)", new GUIStyle(GUI.skin.label) { richText = true });
            }
            else
            {
                float fixedAcc = (shootingSettings != null ? shootingSettings.accuracy : 0.25f) * 100f;
                GUILayout.Label($"동적 명중률([T]토글): <color=gray>OFF</color> | 고정 명중률: <color=yellow><b>{fixedAcc:F1}%</b></color>", new GUIStyle(GUI.skin.label) { richText = true });
            }

            GUILayout.Label($"발사 입력 (Fire): {(movementController.CurrentInput.IsFire ? "<color=red><b>FIRE HELD</b></color>" : "HOLD")} | 쿨다운: {CooldownRemaining:F2}s", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.Label($"최근 사격: <color=yellow>{lastShotLog}</color>", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.Label($"조작: [Space/좌클릭: 사격] | [WASD: 이동] | [Ctrl/C: 앉기] | [T: 명중률토글] | [R: 리셋]");
            GUILayout.EndArea();
        }
    }
}