using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Reinforce.Movement;
using Reinforcement.Shooting;
using Reinforcement.Shooting.Demo;

namespace Reinforce.Integration
{
    /// <summary>
    /// 이동(Movement)과 사격(Shooting) 통합 검증 씬의 시나리오 및 더미 대전 매니저
    /// 1. 이동 + 사격 플레이어 vs 시험용 더미 적의 실시간 1대1 교전 검증
    /// 2. 엄폐물(개방/낮은엄폐/완전엄폐/경사면/낮은천장) 모드 전환 지원
    /// 3. 단축키:
    ///    - [WASD]: 이동, [Shift]: 질주, [Ctrl/C]: 앉기
    ///    - [Space / 마우스 좌클릭]: 사격
    ///    - [1 / 2 / 3]: 개방 / 낮은 엄폐 / 완전 엄폐 전환
    ///    - [B]: 적의 반격(반사 사격) 켜기/끄기
    ///    - [R]: 에피소드 및 위치/체력/사격 쿨다운 전체 초기화
    /// </summary>
    public class CombatIntegrationManager : MonoBehaviour
    {
        [Header("Combatants")]
        [SerializeField] private PlayerCombatIntegration player;
        [SerializeField] private TrialActor rival;

        [Header("Covers & Obstacles")]
        [SerializeField] private GameObject lowCover;
        [SerializeField] private GameObject fullCover;

        [Header("Settings")]
        [SerializeField] private ShootingSettings rivalSettings = new ShootingSettings();
        [SerializeField] private int seed = 731;

        private ShootingWeapon rivalWeapon;
        private TrialVision rivalVision;
        private LineRenderer rivalLine;
        private float rivalLineUntil = 0f;
        private Vector3 playerStartPos;
        private Quaternion playerStartRot;
        private Vector3 rivalStartPos;
        private Quaternion rivalStartRot;

        private int episode = 1;
        private int tick = 0;
        private double elapsed = 0d;
        private bool returnFire = false;
        private bool roundEnded = false;
        private int coverMode = 1; // 1: Open, 2: Low Cover, 3: Full Cover

        private void Start()
        {
            EnsureCombatants();
            SetupRivalWeapon();
            SetupCovers();

            if (player != null)
            {
                playerStartPos = player.transform.position;
                playerStartRot = player.transform.rotation;
            }

            if (rival != null)
            {
                rivalStartPos = rival.transform.position;
                rivalStartRot = rival.transform.rotation;
            }

            RestartCombat();
        }

        private void EnsureCombatants()
        {
            if (player == null)
            {
                player = FindFirstObjectByType<PlayerCombatIntegration>();
            }

            if (player == null)
            {
                // PlayerMovementController 오브젝트가 있으면 자동으로 통합 컴포넌트 부착
                var movement = FindFirstObjectByType<PlayerMovementController>();
                if (movement != null)
                {
                    if (movement.GetComponent<PlayerShotActor>() == null)
                    {
                        movement.gameObject.AddComponent<PlayerShotActor>();
                    }
                    player = movement.gameObject.AddComponent<PlayerCombatIntegration>();
                }
            }

            if (rival == null)
            {
                rival = FindFirstObjectByType<TrialActor>();
            }

            if (rival == null)
            {
                // 씬에 시험용 더미 적이 없으면 자동 생성 (Z = +10m, 지면 Y = 0)
                GameObject rivalObj = new GameObject("Rival_Dummy");
                rivalObj.transform.position = new Vector3(0f, 0f, 10f);
                rivalObj.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

                // CapsuleCollider 부착 (높이 2m, 반경 0.5m, 중심 Y=1m, isTrigger = false)
                var cap = rivalObj.AddComponent<CapsuleCollider>();
                cap.height = 2f;
                cap.radius = 0.5f;
                cap.center = new Vector3(0f, 1f, 0f);
                cap.isTrigger = false;

                // Body Mesh (시각용 캡슐)
                GameObject bodyObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                bodyObj.name = "Body";
                bodyObj.transform.SetParent(rivalObj.transform, false);
                bodyObj.transform.localPosition = new Vector3(0f, 1f, 0f);
                Destroy(bodyObj.GetComponent<Collider>()); // 충돌체는 루트 캡슐콜라이더가 담당
                Renderer r = bodyObj.GetComponent<Renderer>();
                if (r != null) r.material.color = new Color(0.95f, 0.33f, 0.17f);

                // Eye (시야 기준점) - 높이 1.82m
                GameObject eyeObj = new GameObject("Eye");
                eyeObj.transform.SetParent(rivalObj.transform, false);
                eyeObj.transform.localPosition = new Vector3(0f, 1.82f, 0f);

                // Mount (총기 장착점) - 높이 1.24m, 우측 0.48m
                GameObject mountObj = new GameObject("Mount");
                mountObj.transform.SetParent(rivalObj.transform, false);
                mountObj.transform.localPosition = new Vector3(0.48f, 1.24f, 0f);

                // Muzzle (총구) - Mount 자식, 전방 0.46m
                GameObject muzzleObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                muzzleObj.name = "Muzzle";
                muzzleObj.transform.SetParent(mountObj.transform, false);
                muzzleObj.transform.localPosition = new Vector3(0f, 0f, 0.46f);
                muzzleObj.transform.localScale = new Vector3(0.12f, 0.12f, 0.35f);
                Destroy(muzzleObj.GetComponent<Collider>());
                Renderer mr = muzzleObj.GetComponent<Renderer>();
                if (mr != null) mr.material.color = Color.gray;

                // TrialActor 컴포넌트 부착 및 구성 (actorId: 2, teamId: 2)
                rival = rivalObj.AddComponent<TrialActor>();
                rival.Capsule = cap;
                rival.Configure(2, 2, bodyObj.transform, eyeObj.transform, mountObj.transform, muzzleObj.transform);
            }
            else if (rival.Eye == null)
            {
                Transform bodyT = rival.transform.Find("Body");
                Transform eyeT = rival.transform.Find("Eye");
                Transform mountT = rival.transform.Find("Mount");
                Transform muzzleT = mountT != null ? mountT.Find("Muzzle") : null;
                rival.Configure(2, 2, bodyT, eyeT, mountT, muzzleT);
            }

            if (player != null && rival != null)
            {
                player.SetOpponents(rival);
            }
        }

        private void SetupRivalWeapon()
        {
            if (rival == null) return;

            rivalWeapon = rival.gameObject.GetComponent<ShootingWeapon>();
            if (rivalWeapon == null) rivalWeapon = rival.gameObject.AddComponent<ShootingWeapon>();
            rivalWeapon.Configure(rival, rival.Eye, rival.Mount, rival.Muzzle, rivalSettings);
            rivalWeapon.ShotResolved -= OnRivalShot;
            rivalWeapon.ShotResolved += OnRivalShot;

            rivalVision = rival.gameObject.GetComponent<TrialVision>();
            if (rivalVision == null) rivalVision = rival.gameObject.AddComponent<TrialVision>();
            if (player != null && player.Actor != null)
            {
                rivalVision.Configure(rival, rival.Eye, player.Actor);
            }

            // 라이벌용 주황색 Tracer 라인
            GameObject traceObj = new GameObject("Rival_Tracer");
            traceObj.transform.SetParent(transform, false);
            rivalLine = traceObj.AddComponent<LineRenderer>();
            rivalLine.useWorldSpace = true;
            rivalLine.positionCount = 2;
            rivalLine.widthMultiplier = 0.05f;

            Shader urpShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (urpShader == null) urpShader = Shader.Find("Sprites/Default");
            Material mat = new Material(urpShader);
            mat.color = new Color(1f, 0.3f, 0.1f, 1f);
            rivalLine.material = mat;
            rivalLine.enabled = false;
        }

        private void SetupCovers()
        {
            // 엄폐물이 씬에 없으면 동적 생성
            if (lowCover == null)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "LowCover_1.4m";
                go.transform.position = new Vector3(0f, 0.7f, 5f);
                go.transform.localScale = new Vector3(4f, 1.4f, 0.4f);
                var obs = go.AddComponent<ShotObstacle>();
                obs.ObstacleId = "low_cover";
                Renderer r = go.GetComponent<Renderer>();
                if (r != null) r.material.color = new Color(0.7f, 0.6f, 0.4f);
                lowCover = go;
            }

            if (fullCover == null)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "FullCover_2.8m";
                go.transform.position = new Vector3(0f, 1.4f, 5f);
                go.transform.localScale = new Vector3(4f, 2.8f, 0.4f);
                var obs = go.AddComponent<ShotObstacle>();
                obs.ObstacleId = "full_cover";
                Renderer r = go.GetComponent<Renderer>();
                if (r != null) r.material.color = new Color(0.4f, 0.4f, 0.45f);
                fullCover = go;
            }

            SetCoverMode(1);
        }

        public void SetCoverMode(int mode)
        {
            coverMode = mode;
            if (lowCover != null) lowCover.SetActive(mode == 2);
            if (fullCover != null) fullCover.SetActive(mode == 3);
        }

        public void RestartCombat()
        {
            episode++;
            tick = 0;
            elapsed = 0d;
            roundEnded = false;

            if (player != null)
            {
                player.transform.SetPositionAndRotation(playerStartPos, playerStartRot);
                player.ResetCombatEpisode(episode, seed);
            }

            if (rival != null)
            {
                rival.transform.SetPositionAndRotation(rivalStartPos, rivalStartRot);
                rival.ResetTrial();
                if (rivalWeapon != null) rivalWeapon.ResetEpisode(episode, seed + 1);
            }

            if (rivalLine != null) rivalLine.enabled = false;
        }

        private void Update()
        {
            // 키보드 엄폐 및 모드 전환 입력 (Input System 및 Legacy Input 동시 지원)
            bool toggleFire = false;
            bool resetPressed = false;
            int coverKey = 0;

#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) coverKey = 1;
                else if (kb.digit2Key.wasPressedThisFrame) coverKey = 2;
                else if (kb.digit3Key.wasPressedThisFrame) coverKey = 3;

                if (kb.bKey.wasPressedThisFrame) toggleFire = true;
                if (kb.rKey.wasPressedThisFrame) resetPressed = true;
            }
#endif
            if (Input.GetKeyDown(KeyCode.Alpha1)) coverKey = 1;
            else if (Input.GetKeyDown(KeyCode.Alpha2)) coverKey = 2;
            else if (Input.GetKeyDown(KeyCode.Alpha3)) coverKey = 3;

            if (Input.GetKeyDown(KeyCode.B)) toggleFire = true;
            if (Input.GetKeyDown(KeyCode.R)) resetPressed = true;

            if (coverKey > 0) SetCoverMode(coverKey);
            if (toggleFire) returnFire = !returnFire;
            if (resetPressed) RestartCombat();

            if (Time.time >= rivalLineUntil && rivalLine != null)
            {
                rivalLine.enabled = false;
            }

            if (!roundEnded && player != null && rival != null)
            {
                if (!player.Actor.Alive || !rival.Alive)
                {
                    roundEnded = true;
                }
            }
        }

        private void FixedUpdate()
        {
            if (roundEnded || rival == null || rivalWeapon == null || rivalVision == null) return;

            // 라이벌이 살아있고 플레이어가 살아있으면 플레이어를 향해 회전 정렬
            if (rival.Alive && player != null && player.Actor != null && player.Actor.Alive)
            {
                Vector3 toPlayer = player.transform.position - rival.transform.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 0.01f)
                {
                    rival.transform.rotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
                }
            }

            Physics.SyncTransforms();

            // 라이벌 틱 처리 (반격 설정 시 사격 실행)
            rivalWeapon.SetCommand(episode, returnFire);
            VisibilityFrame rivalFrame = rivalVision.Sample(episode, tick, rivalSettings.mask);
            if (rival.Alive)
            {
                rivalWeapon.Tick(episode, tick, elapsed, rivalFrame);
            }

            tick++;
            elapsed += Time.fixedDeltaTime;
        }

        private void OnRivalShot(ShotResult shot)
        {
            if (rivalLine != null && rivalSettings.tracerSeconds > 0f)
            {
                rivalLine.SetPosition(0, shot.Origin);
                rivalLine.SetPosition(1, shot.Contact.Point);
                rivalLine.enabled = true;
                rivalLineUntil = Time.time + rivalSettings.tracerSeconds;
            }
        }

        private void OnGUI()
        {
            const int width = 480;
            const int height = 205;
            int x = (Screen.width - width) / 2;
            int y = 10;

            GUI.Box(new Rect(x, y, width, height), $"[1대1 이동·사격 통합 검증 대전 - Episode #{episode}]");
            GUILayout.BeginArea(new Rect(x + 10, y + 25, width - 20, height - 30));

            string pHP = player != null ? $"{player.Actor.Health:F0}" : "0";
            string rHP = rival != null ? $"{rival.Health:F0}" : "0";
            string rStatus = rivalWeapon != null ? rivalWeapon.Status.ToString() : "-";
            GUILayout.Label($"<b><color=cyan>Player HP: {pHP}</color></b>   VS   <b><color=orange>Rival HP: {rHP}</color></b>   |   적 반격(B키): {(returnFire ? "<color=red>ON</color>" : "<color=gray>OFF</color>")} ({rStatus})", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 13 });

            if (player != null)
            {
                float acc = player.CurrentAccuracy * 100f;
                float lockTime = player.TargetLockDuration;
                bool isDyn = player.ShootingSettings != null && player.ShootingSettings.useDynamicAccuracy;
                string dynText = isDyn ? $"<color=lime>동적 모드</color> (조준: {lockTime:F2}s)" : "<color=gray>고정 모드</color>";
                GUILayout.Label($"플레이어 명중률: <color=yellow><b>{acc:F1}%</b></color> [{dynText}]", new GUIStyle(GUI.skin.label) { richText = true });
            }

            string coverName = coverMode == 1 ? "1. 개방 (Open)" : (coverMode == 2 ? "2. 낮은 엄폐 (Low Cover 1.4m)" : "3. 완전 엄폐 (Full Cover 2.8m)");
            GUILayout.Label($"현재 엄폐물: <color=yellow><b>{coverName}</b></color>", new GUIStyle(GUI.skin.label) { richText = true });

            if (roundEnded)
            {
                string winner = player != null && player.Actor.Alive ? "<color=lime>PLAYER VICTORY!</color>" : "<color=red>RIVAL VICTORY!</color>";
                GUILayout.Label($"<b>라운드 종료: {winner}</b> (R 키로 재시작)", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14 });
            }
            else
            {
                GUILayout.Label($"상태: 교전 진행 중 (시간: {elapsed:F1}s)");
            }

            GUILayout.Space(3);
            GUILayout.Label("단축키: [1/2/3: 엄폐물 변경] | [B: 적 사격] | [T: 명중률 토글] | [R: 리셋]");
            GUILayout.EndArea();
        }
    }
}
