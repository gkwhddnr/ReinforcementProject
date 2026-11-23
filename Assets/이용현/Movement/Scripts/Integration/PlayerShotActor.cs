using UnityEngine;
using Reinforcement.Shooting;
using Reinforce.Movement;

namespace Reinforce.Integration
{
    /// <summary>
    /// PlayerMovementController와 연동되는 피격/체력 액터 어댑터
    /// 이동 컨트롤러의 자세 변경(서기 2.0m <-> 앉기 1.2m)에 맞춰 피격 캡슐 콜라이더를 실시간 동기화합니다.
    /// 체력 고갈 시 PlayerMovementController.Die()를 호출하여 HFSM DeadState로 전이시킵니다.
    /// </summary>
    [RequireComponent(typeof(PlayerMovementController))]
    public class PlayerShotActor : ShotActor
    {
        [Header("Actor Identity")]
        [SerializeField] private int actorId = 1;
        [SerializeField] private int teamId = 1;

        [Header("Health Settings")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float currentHealth = 100f;

        private PlayerMovementController movementController;

        public override int ActorId => actorId;
        public override int TeamId => teamId;
        public override bool Alive => currentHealth > 0f && (movementController == null || !movementController.IsDead);
        public float Health => currentHealth;
        public float MaxHealth => maxHealth;

        public void SetIdentity(int newActorId, int newTeamId)
        {
            actorId = newActorId;
            teamId = newTeamId;
        }

        private void Awake()
        {
            movementController = GetComponent<PlayerMovementController>();

            // 피격용 CapsuleCollider 확인 (CharacterController 외에 Raycast 피격용 캡슐 확보)
            if (Capsule == null)
            {
                Capsule = GetComponent<CapsuleCollider>();
                if (Capsule == null)
                {
                    Capsule = gameObject.AddComponent<CapsuleCollider>();
                    Capsule.isTrigger = false;
                }
            }

            SyncCapsuleWithPosture(movementController != null ? movementController.Posture : PostureState.Standing);
        }

        private void OnEnable()
        {
            if (movementController != null)
            {
                movementController.OnPostureChanged += OnPostureChanged;
            }
        }

        private void OnDisable()
        {
            if (movementController != null)
            {
                movementController.OnPostureChanged -= OnPostureChanged;
            }
        }

        private void OnPostureChanged(PostureState posture)
        {
            SyncCapsuleWithPosture(posture);
        }

        public void SyncCapsuleWithPosture(PostureState posture)
        {
            if (Capsule == null) return;

            float height = (posture == PostureState.Crouching) ? 1.2f : 2.0f;
            Capsule.height = height;
            Capsule.center = new Vector3(0f, height * 0.5f, 0f);
            Capsule.radius = 0.5f;
        }

        public void ResetHealth()
        {
            currentHealth = maxHealth;
            SyncCapsuleWithPosture(PostureState.Standing);
        }

        public override DamageReceipt ReceiveDamage(DamageRequest request)
        {
            if (!Alive) return new DamageReceipt(0f, false);

            float applied = Mathf.Clamp(request.Amount, 0f, currentHealth);
            currentHealth -= applied;

            bool killed = currentHealth <= 0f;
            if (killed)
            {
                currentHealth = 0f;
                if (movementController != null && !movementController.IsDead)
                {
                    movementController.Die();
                }
            }

            return new DamageReceipt(applied, killed);
        }
    }
}
