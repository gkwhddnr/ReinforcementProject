using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Reinforcement.Shooting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Reinforce.Training.Tests
{
    public sealed class OutdoorArenaTelemetryTests
    {
        private const string IntegrationType = "Reinforce.Integration.PlayerCombatIntegration";
        private const string ArenaType = "ReinProject.Landscape.ArenaSpawnManager";
        private const string TelemetryType = "Reinforce.LearningIntegration.OutdoorArenaTelemetry";

        [UnityTearDown]
        public IEnumerator ExitPlayModeAfterFailure()
        {
            if (EditorApplication.isPlaying)
                yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator ActualArenaExposesBothPlayersWithoutChangingTheirState()
        {
            yield return new EnterPlayMode();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/하종욱/Landscape/Scenes/OutdoorArena.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;

            var arena = (Component)UnityEngine.Object.FindFirstObjectByType(OwnerType(ArenaType));
            Assert.IsNotNull(arena);
            var first = Read<Component>(arena, "CombatantA");
            var second = Read<Component>(arena, "CombatantB");
            Assert.IsNotNull(first);
            Assert.IsNotNull(second);
            Vector3 beforePosition = first.transform.position;
            float beforeHealth = Read<float>(first.GetComponent<ShotActor>(), "Health");

            Component telemetry = arena.gameObject.AddComponent(OwnerType(TelemetryType));
            Call(telemetry, "Bind", arena);
            Assert.IsTrue(Read<bool>(telemetry, "HasFirstState"));
            Assert.IsTrue(Read<bool>(telemetry, "HasSecondState"));
            PlayerState state = Read<PlayerState>(telemetry, "FirstState");
            Assert.AreEqual(first.GetComponent<ShotActor>().ActorId, state.ActorId);
            Assert.AreEqual(beforePosition, state.Position);
            Assert.AreEqual(beforeHealth, state.Health);
            Assert.AreEqual(beforePosition, first.transform.position);
            Assert.AreEqual(beforeHealth, Read<float>(first.GetComponent<ShotActor>(), "Health"));
            Assert.AreNotEqual(state.ActorId, Read<PlayerState>(telemetry, "SecondState").ActorId);
            UnityEngine.Object.Destroy(telemetry);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator IntegratedPrefabsDeliverActualShotsAndUnsubscribeOnDisable()
        {
            yield return new EnterPlayMode();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Telemetry Prefab Review"));
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.layer = 8;
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(40f, 1f, 40f);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/이용현/Movement/Prefabs/Player_Combat_Integrated.prefab");
            Assert.IsNotNull(prefab);
            var first = UnityEngine.Object.Instantiate(prefab, new Vector3(0f, 0f, -5f), Quaternion.identity);
            var second = UnityEngine.Object.Instantiate(prefab, new Vector3(0f, 0f, 5f), Quaternion.Euler(0f, 180f, 0f));
            var a = first.GetComponent(OwnerType(IntegrationType));
            var b = second.GetComponent(OwnerType(IntegrationType));
            Call(first.GetComponent<ShotActor>(), "SetIdentity", 1, 1);
            Call(second.GetComponent<ShotActor>(), "SetIdentity", 2, 2);
            Call(a, "SetOpponents", (object)new ShotActor[] { second.GetComponent<ShotActor>() });
            Call(b, "SetOpponents", (object)new ShotActor[] { first.GetComponent<ShotActor>() });

            var arena = new GameObject("Telemetry Arena Source").AddComponent(OwnerType(ArenaType));
            SetField(arena, "_autoSpawnOnStart", false);
            SetField(arena, "_instanceA", a);
            SetField(arena, "_instanceB", b);
            yield return null;

            Component telemetry = arena.gameObject.AddComponent(OwnerType(TelemetryType));
            Call(telemetry, "Bind", arena);
            foreach (Component combatant in new[] { a, b })
            {
                var settings = Read<ShootingSettings>(combatant, "ShootingSettings");
                settings.useDynamicAccuracy = false;
                settings.accuracy = 1f;
                var input = combatant.GetComponent(OwnerType("Reinforce.Movement.PlayerAgentInputBridge"));
                Call(Read<Component>(combatant, "Movement"), "SetInputProvider", input);
                Call(input, "SetActionsFromAgent", 0f, 0f, false, false, true, combatant.transform.forward);
            }

            var buffer = Read<CombatTelemetryBuffer>(telemetry, "Buffer");
            // EditMode tests continue through editor updates even after entering Play Mode.
            double deadline = EditorApplication.timeSinceStartup + 5d;
            while (buffer.Shots.Count < 2 && EditorApplication.timeSinceStartup < deadline)
                yield return null;
            Assert.GreaterOrEqual(buffer.Shots.Count, 2,
                $"A: {Read<ShootingWeapon>(a, "Weapon").Status}, target={Read<bool>(a, "HasTarget")}, " +
                $"position={first.transform.position}, tick={Read<int>(a, "TargetUpdateTick")}; " +
                $"B: {Read<ShootingWeapon>(b, "Weapon").Status}, target={Read<bool>(b, "HasTarget")}, " +
                $"position={second.transform.position}, tick={Read<int>(b, "TargetUpdateTick")}");
            Assert.That(buffer.Shots[0].AppliedDamage, Is.GreaterThan(0f));
            Assert.AreNotEqual(buffer.Shots[0].ShooterId, buffer.Shots[0].HitActorId);

            ((Behaviour)telemetry).enabled = false;
            Call(telemetry, "CaptureLatest");
            Assert.IsFalse(Read<bool>(telemetry, "HasFirstState"));
            int count = buffer.Shots.Count;
            int targetTick = Read<int>(a, "TargetUpdateTick") + 15;
            deadline = EditorApplication.timeSinceStartup + 5d;
            while (Read<int>(a, "TargetUpdateTick") < targetTick && EditorApplication.timeSinceStartup < deadline)
                yield return null;
            Assert.GreaterOrEqual(Read<int>(a, "TargetUpdateTick"), targetTick);
            Assert.AreEqual(count, buffer.Shots.Count);
            yield return new ExitPlayMode();
        }

        // The owners' components live in the predefined assembly, which a named test assembly cannot reference.
        private static Type OwnerType(string name) => Type.GetType(name + ", Assembly-CSharp", true);

        private static T Read<T>(Component component, string property) =>
            (T)component.GetType().GetProperty(property).GetValue(component);

        private static void Call(Component component, string method, params object[] args) =>
            component.GetType().InvokeMember(method, BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                null, component, args);

        private static void SetField(Component component, string field, object value) =>
            component.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(component, value);
    }
}
