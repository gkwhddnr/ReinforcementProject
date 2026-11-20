using UnityEngine;
using UnityEngine.InputSystem;

namespace Reinforcement.Shooting.Demo
{
    // A fixed local exercise. The rival only fires when its scripted command is enabled.
    public sealed class TrialDuel : MonoBehaviour
    {
        [SerializeField] private TrialActor player;
        [SerializeField] private TrialActor rival;
        [SerializeField] private GameObject lowCover;
        [SerializeField] private GameObject fullCover;
        [SerializeField] private ShootingSettings settings = new ShootingSettings();
        [SerializeField] private bool showTracers = true;
        [SerializeField] private int seed = 731;

        private ShootingWeapon playerWeapon, rivalWeapon;
        private TrialVision playerVision, rivalVision;
        private LineRenderer playerLine, rivalLine;
        private float playerLineUntil, rivalLineUntil;
        private Vector3 playerSpawn;
        private Quaternion playerFacing;
        private int episode, tick, cover;
        private double elapsed;
        private bool ended, returnFire, waitForRelease;
        private string lastContact;

        private void Start()
        {
            playerSpawn = player.transform.position;
            playerFacing = player.transform.rotation;
            Prepare(player, rival, new Color(0.07f, 0.65f, 0.87f), out playerWeapon, out playerVision, out playerLine);
            Prepare(rival, player, new Color(0.95f, 0.33f, 0.17f), out rivalWeapon, out rivalVision, out rivalLine);
            foreach (ShotObstacle obstacle in GetComponentsInChildren<ShotObstacle>(true))
            {
                var tint = obstacle.ObstacleId == "floor" ? new Color(0.27f, 0.32f, 0.34f) :
                    new Color(0.68f, 0.62f, 0.45f);
                Tint(obstacle.GetComponent<Renderer>(), tint);
            }
            Restart();
        }

        private void Prepare(TrialActor actor, TrialActor opponent, Color color,
            out ShootingWeapon weapon, out TrialVision vision, out LineRenderer line)
        {
            weapon = actor.gameObject.AddComponent<ShootingWeapon>();
            weapon.Configure(actor, actor.Eye, actor.Mount, actor.Muzzle, settings);
            weapon.ShotResolved += OnShot;
            vision = actor.gameObject.AddComponent<TrialVision>();
            vision.Configure(actor, actor.Eye, opponent);
            Renderer body = actor.GetComponentInChildren<MeshRenderer>();
            Tint(body, color);
            Tint(actor.Muzzle.GetComponentInChildren<MeshRenderer>(), Color.gray);
            var traceObject = new GameObject(actor.name + " tracer");
            traceObject.transform.SetParent(transform, false);
            line = traceObject.AddComponent<LineRenderer>();
            line.sharedMaterial = body.sharedMaterial;
            line.generateLightingData = true;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.widthMultiplier = 0.055f;
            Tint(line, Color.Lerp(color, Color.white, 0.4f));
            line.enabled = false;
        }

        private static void Tint(Renderer renderer, Color color)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(block);
        }

        private static bool FirePressed()
        {
            return (Keyboard.current != null && Keyboard.current.spaceKey.isPressed) ||
                (Mouse.current != null && Mouse.current.leftButton.isPressed);
        }

        private void SetCover(int mode)
        {
            cover = mode;
            lowCover.SetActive(mode == 2);
            fullCover.SetActive(mode == 3);
        }

        private void Restart()
        {
            episode++;
            player.transform.SetPositionAndRotation(playerSpawn, playerFacing);
            player.ResetTrial();
            rival.ResetTrial();
            playerWeapon.ResetEpisode(episode, seed);
            rivalWeapon.ResetEpisode(episode, seed + 1);
            playerLine.enabled = rivalLine.enabled = false;
            returnFire = ended = false;
            waitForRelease = FirePressed();
            tick = 0;
            elapsed = 0;
            lastContact = "No shots";
            SetCover(1);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.rKey.wasPressedThisFrame) Restart();
                if (!ended)
                {
                    if (keyboard.digit1Key.wasPressedThisFrame) SetCover(1);
                    if (keyboard.digit2Key.wasPressedThisFrame) SetCover(2);
                    if (keyboard.digit3Key.wasPressedThisFrame) SetCover(3);
                    if (keyboard.cKey.wasPressedThisFrame) player.SetCrouched(!player.Crouched);
                    if (keyboard.bKey.wasPressedThisFrame) returnFire = !returnFire;
                }
            }
            if (!FirePressed()) waitForRelease = false;
            if (Time.time >= playerLineUntil) playerLine.enabled = false;
            if (Time.time >= rivalLineUntil) rivalLine.enabled = false;
        }

        private void FixedUpdate()
        {
            if (ended) return;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                float strafe = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                float turn = (keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f);
                Vector3 position = player.transform.position;
                position.x = Mathf.Clamp(position.x + strafe * 3f * Time.fixedDeltaTime,
                    playerSpawn.x - 3.5f, playerSpawn.x + 3.5f);
                player.transform.position = position;
                player.transform.Rotate(Vector3.up, turn * 100f * Time.fixedDeltaTime);
            }
            Physics.SyncTransforms();
            playerWeapon.SetCommand(episode, !waitForRelease && FirePressed());
            rivalWeapon.SetCommand(episode, returnFire);
            playerWeapon.Tick(episode, tick, elapsed, playerVision.Sample(episode, tick, settings.mask));
            if (rival.Alive)
                rivalWeapon.Tick(episode, tick, elapsed, rivalVision.Sample(episode, tick, settings.mask));
            if (!player.Alive || !rival.Alive)
            {
                ended = true;
                playerWeapon.EndEpisode();
                rivalWeapon.EndEpisode();
                playerLine.enabled = rivalLine.enabled = false;
            }
            tick++;
            elapsed += Time.fixedDeltaTime;
        }

        private void OnShot(ShotResult shot)
        {
            bool fromPlayer = shot.ShooterId == player.ActorId;
            string hit = shot.Contact.Kind == ContactKind.Obstacle ? shot.Contact.ObstacleId :
                shot.Contact.Kind == ContactKind.Actor ? $"{shot.Region} / {shot.Damage.Applied:0} damage" : "miss";
            string who = fromPlayer ? "Player" : "Rival";
            lastContact = $"{who} shot {shot.Sequence}  |  {hit}";
            if (!showTracers || settings.tracerSeconds <= 0f) return;
            LineRenderer line = fromPlayer ? playerLine : rivalLine;
            line.SetPosition(0, shot.Origin);
            line.SetPosition(1, shot.Contact.Point);
            line.enabled = true;
            if (fromPlayer) playerLineUntil = Time.time + settings.tracerSeconds;
            else rivalLineUntil = Time.time + settings.tracerSeconds;
        }

        private void OnDisable()
        {
            if (!playerWeapon) return;
            ended = true;
            playerWeapon.EndEpisode();
            rivalWeapon.EndEpisode();
            playerLine.enabled = rivalLine.enabled = false;
        }

        private void OnGUI()
        {
            if (!playerWeapon) return;
            GUILayout.BeginArea(new Rect(12f, 12f, Mathf.Min(Screen.width - 24f, 405f), 220f), GUI.skin.box);
            GUILayout.Label($"1v1 shooting trial   |   Episode {episode}   |   Seed {seed}");
            GUILayout.Label($"Player HP {player.Health:0}     Rival HP {rival.Health:0}     Scripted return fire {(returnFire ? "ON" : "OFF")}");
            string mode = cover == 1 ? "Open" : cover == 2 ? "Low cover" : "Full cover";
            string state = ended ? "Round ended - press R" : playerWeapon.Status.ToString();
            GUILayout.Label($"{mode}   |   {state}   |   {(player.Crouched ? "Crouching" : "Standing")}");
            GUILayout.Label(lastContact);
            GUILayout.Space(4f);
            GUILayout.Label("Space / LMB   Fire       A / D   Strafe       Q / E   Turn");
            GUILayout.Label("1   Open       2   Low cover       3   Full cover");
            GUILayout.Label("C   Crouch       B   Rival fire       R   Restart");
            GUILayout.EndArea();
        }
    }
}
