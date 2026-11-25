#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Reinforce.Integration;
using Reinforce.Movement;
using Reinforcement.Shooting;
using Reinforcement.Shooting.Demo;

namespace ReinProject.Landscape.Editor
{
    public static class FixPlayerGunMounts
    {
        [MenuItem("Tools/Fix Player Gun Mounts (Right Muzzle Standard)")]
        public static void Execute()
        {
            FixPrefab();
            FixCombatIntegrationScene();
            FixOutdoorArenaScene();
            AssetDatabase.SaveAssets();
            Debug.Log("<color=green>[FixPlayerGunMounts] Successfully standardized all Player Gun Mounts and Muzzles to Right Muzzle (0.48, 0, 0.46)!</color>");
        }

        private static void FixPrefab()
        {
            string prefabPath = "Assets/이용현/Movement/Prefabs/Player_Combat_Integrated.prefab";
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (prefabRoot != null)
            {
                SetupGunHierarchy(prefabRoot.transform, out Transform eye, out Transform mount, out Transform muzzle);
                
                var weapon = prefabRoot.GetComponent<ShootingWeapon>();
                var combat = prefabRoot.GetComponent<PlayerCombatIntegration>();
                var shotActor = prefabRoot.GetComponent<PlayerShotActor>();

                if (combat != null)
                {
                    var so = new SerializedObject(combat);
                    var pEye = so.FindProperty("eye");
                    if (pEye != null) pEye.objectReferenceValue = eye;
                    var pMount = so.FindProperty("mount");
                    if (pMount != null) pMount.objectReferenceValue = mount;
                    var pMuzzle = so.FindProperty("muzzle");
                    if (pMuzzle != null) pMuzzle.objectReferenceValue = muzzle;
                    
                    var pShooting = so.FindProperty("shootingSettings");
                    if (pShooting != null)
                    {
                        var pDyn = pShooting.FindPropertyRelative("useDynamicAccuracy");
                        if (pDyn != null) pDyn.boolValue = true;
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                if (weapon != null)
                {
                    var soW = new SerializedObject(weapon);
                    var pOwner = soW.FindProperty("owner");
                    if (pOwner != null) pOwner.objectReferenceValue = shotActor;
                    var pEye = soW.FindProperty("eye");
                    if (pEye != null) pEye.objectReferenceValue = eye;
                    var pMount = soW.FindProperty("mount");
                    if (pMount != null) pMount.objectReferenceValue = mount;
                    var pMuzzle = soW.FindProperty("muzzle");
                    if (pMuzzle != null) pMuzzle.objectReferenceValue = muzzle;

                    var pSettings = soW.FindProperty("settings");
                    if (pSettings != null)
                    {
                        var pDyn = pSettings.FindPropertyRelative("useDynamicAccuracy");
                        if (pDyn != null) pDyn.boolValue = true;
                    }
                    soW.ApplyModifiedPropertiesWithoutUndo();
                }

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                PrefabUtility.UnloadPrefabContents(prefabRoot);
                Debug.Log($"[FixPlayerGunMounts] Updated Prefab: {prefabPath}");
            }
        }

        private static void FixCombatIntegrationScene()
        {
            string scenePath = "Assets/공유/Scenes/CombatMovementIntegrationScene.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            
            // 1. Player 오브젝트
            GameObject player = GameObject.Find("Player");
            if (player != null)
            {
                SetupGunHierarchy(player.transform, out Transform eye, out Transform mount, out Transform muzzle);

                var combat = player.GetComponent<PlayerCombatIntegration>();
                if (combat != null)
                {
                    var so = new SerializedObject(combat);
                    var pEye = so.FindProperty("eye");
                    if (pEye != null) pEye.objectReferenceValue = eye;
                    var pMount = so.FindProperty("mount");
                    if (pMount != null) pMount.objectReferenceValue = mount;
                    var pMuzzle = so.FindProperty("muzzle");
                    if (pMuzzle != null) pMuzzle.objectReferenceValue = muzzle;
                    
                    var pShooting = so.FindProperty("shootingSettings");
                    if (pShooting != null)
                    {
                        var pDyn = pShooting.FindPropertyRelative("useDynamicAccuracy");
                        if (pDyn != null) pDyn.boolValue = true;
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                var weapon = player.GetComponent<ShootingWeapon>();
                if (weapon != null)
                {
                    var soW = new SerializedObject(weapon);
                    var pEye = soW.FindProperty("eye");
                    if (pEye != null) pEye.objectReferenceValue = eye;
                    var pMount = soW.FindProperty("mount");
                    if (pMount != null) pMount.objectReferenceValue = mount;
                    var pMuzzle = soW.FindProperty("muzzle");
                    if (pMuzzle != null) pMuzzle.objectReferenceValue = muzzle;
                    soW.ApplyModifiedPropertiesWithoutUndo();
                }

                EditorUtility.SetDirty(player);
                Debug.Log($"[FixPlayerGunMounts] Updated CombatMovementIntegrationScene: Player gun mount fixed.");
            }

            // 2. 혹시 존재하는 Rival / Rival_Dummy 오브젝트도 표준 마운트로 정리
            GameObject rival = GameObject.Find("Rival") ?? GameObject.Find("Rival_Dummy");
            if (rival != null)
            {
                SetupGunHierarchy(rival.transform, out _, out _, out _);
                EditorUtility.SetDirty(rival);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void FixOutdoorArenaScene()
        {
            string scenePath = "Assets/하종욱/Landscape/Scenes/OutdoorArena.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            // 1. 씬에 이미 배치되어 있는 Player 오브젝트들 탐색 (Spawn A/Player 등)
            var allPlayers = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
            foreach (var t in allPlayers)
            {
                if (t.name == "Player" || t.name.Contains("Combatant") || t.CompareTag("ArenaPlayer"))
                {
                    SetupGunHierarchy(t, out Transform eye, out Transform mount, out Transform muzzle);

                    var combat = t.GetComponent<PlayerCombatIntegration>();
                    if (combat != null)
                    {
                        var so = new SerializedObject(combat);
                        var pEye = so.FindProperty("eye");
                        if (pEye != null) pEye.objectReferenceValue = eye;
                        var pMount = so.FindProperty("mount");
                        if (pMount != null) pMount.objectReferenceValue = mount;
                        var pMuzzle = so.FindProperty("muzzle");
                        if (pMuzzle != null) pMuzzle.objectReferenceValue = muzzle;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }

                    var weapon = t.GetComponent<ShootingWeapon>();
                    if (weapon != null)
                    {
                        var soW = new SerializedObject(weapon);
                        var pEye = soW.FindProperty("eye");
                        if (pEye != null) pEye.objectReferenceValue = eye;
                        var pMount = soW.FindProperty("mount");
                        if (pMount != null) pMount.objectReferenceValue = mount;
                        var pMuzzle = soW.FindProperty("muzzle");
                        if (pMuzzle != null) pMuzzle.objectReferenceValue = muzzle;
                        soW.ApplyModifiedPropertiesWithoutUndo();
                    }

                    EditorUtility.SetDirty(t.gameObject);
                    Debug.Log($"[FixPlayerGunMounts] Updated OutdoorArena scene object: {t.name} gun mount fixed.");
                }
            }

            // 2. ArenaSpawnManager의 prefab 연결 상태 확인
            var spawnManager = Object.FindFirstObjectByType<ArenaSpawnManager>();
            if (spawnManager != null)
            {
                var soSpawn = new SerializedObject(spawnManager);
                var pPrefab = soSpawn.FindProperty("_combatantPrefab");
                if (pPrefab != null && pPrefab.objectReferenceValue == null)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/이용현/Movement/Prefabs/Player_Combat_Integrated.prefab");
                    if (prefab != null)
                    {
                        pPrefab.objectReferenceValue = prefab;
                        soSpawn.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(spawnManager);
                        Debug.Log($"[FixPlayerGunMounts] OutdoorArena: Re-linked ArenaSpawnManager._combatantPrefab");
                    }
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void SetupGunHierarchy(Transform root, out Transform eye, out Transform mount, out Transform muzzle)
        {
            // 1. Eye (0, 1.82, 0)
            eye = root.Find("Eye");
            if (eye == null)
            {
                GameObject eObj = new GameObject("Eye");
                eObj.transform.SetParent(root, false);
                eye = eObj.transform;
            }
            eye.localPosition = new Vector3(0f, 1.82f, 0f);
            eye.localRotation = Quaternion.identity;

            // 2. Mount (0, 1.24, 0) - 이승재 TrialActor 양식: 어깨/몸통 중심 높이 1.24m
            mount = root.Find("Mount");
            if (mount == null)
            {
                GameObject mObj = new GameObject("Mount");
                mObj.transform.SetParent(root, false);
                mount = mObj.transform;
            }
            mount.localPosition = new Vector3(0f, 1.24f, 0f);
            mount.localRotation = Quaternion.identity;

            // 3. Right muzzle (0.48, 0, 0.46) - Mount의 자식으로 우측 0.48m, 전방 0.46m
            muzzle = mount.Find("Right muzzle");
            if (muzzle == null)
            {
                Transform oldM = mount.Find("Muzzle") ?? root.Find("Muzzle") ?? root.Find("Right muzzle");
                if (oldM != null)
                {
                    muzzle = oldM;
                }
                else
                {
                    GameObject rObj = new GameObject("Right muzzle");
                    muzzle = rObj.transform;
                }
            }
            muzzle.name = "Right muzzle";
            muzzle.SetParent(mount, false);
            muzzle.localPosition = new Vector3(0.48f, 0f, 0.46f);
            muzzle.localRotation = Quaternion.identity;

            // 4. Barrel - Right muzzle 자식으로 시각적 총신 큐브 메쉬 (0.12, 0.12, 0.35, 회색)
            Transform barrel = muzzle.Find("Barrel");
            if (barrel == null)
            {
                GameObject bObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bObj.name = "Barrel";
                barrel = bObj.transform;
                barrel.SetParent(muzzle, false);
                Object.DestroyImmediate(bObj.GetComponent<Collider>());
            }
            barrel.localPosition = Vector3.zero;
            barrel.localRotation = Quaternion.identity;
            barrel.localScale = new Vector3(0.12f, 0.12f, 0.35f);

            Renderer rend = barrel.GetComponent<Renderer>();
            if (rend != null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (s != null)
                {
                    Material m = new Material(s) { color = Color.gray };
                    rend.material = m;
                }
            }
        }
    }
}
#endif
