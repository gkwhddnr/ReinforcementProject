using UnityEngine;

namespace Reinforce.Movement
{
    [CreateAssetMenu(fileName = "MovementSettings", menuName = "Reinforce/Movement Settings")]
    public class MovementSettingsSO : ScriptableObject
    {
        [Header("Speeds (m/s)")]
        [Tooltip("기본 걷기 속도")]
        public float walkSpeed = 4.0f;
        [Tooltip("전력 질주 속도")]
        public float sprintSpeed = 6.5f;
        [Tooltip("앉아서 이동 속도")]
        public float crouchSpeed = 2.0f;
        [Tooltip("초당 회전 속도 (도)")]
        public float rotationSpeed = 360.0f;

        [Header("Slope & Physics")]
        [Tooltip("최대 등반 가능 경사각 (도)")]
        public float maxSlopeLimit = 75.0f;
        [Tooltip("75도 초과 급경사 슬라이드 속도")]
        public float slideSpeed = 8.0f;
        [Tooltip("중력 가속도 (2g)")]
        public float gravity = 19.6f;
        [Tooltip("지면 밀착 하향 속도")]
        public float stickToGroundForce = 5.0f;

        [Header("Collision Dimensions")]
        [Tooltip("기본 서기 키")]
        public float standingHeight = 2.0f;
        [Tooltip("앉기 키")]
        public float crouchingHeight = 1.2f;
        [Tooltip("캡슐 반지름")]
        public float colliderRadius = 0.5f;
        [Tooltip("단차 극복 한계")]
        public float stepOffset = 0.3f;
    }
}
