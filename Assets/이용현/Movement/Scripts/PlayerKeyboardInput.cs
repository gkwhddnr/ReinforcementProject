using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Reinforce.Movement
{
    /// <summary>
    /// 게임 베이스 수동 검증용 키보드(WASD/Shift/Ctrl) 및 마우스 입력 프로바이더
    /// Unity New Input System 및 Legacy Input System을 모두 안전하게 지원합니다.
    /// </summary>
    public class PlayerKeyboardInput : MonoBehaviour, IPlayerInputProvider
    {
        [Header("Input Settings")]
        [Tooltip("마우스 커서 방향을 바라볼지 여부 (false면 이동 방향을 바라봄)")]
        [SerializeField] private bool lookAtMouseCursor = true;

        private Camera mainCamera;

        private void Awake()
        {
            mainCamera = Camera.main;
        }

        public MovementInputData GetInput()
        {
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
            }

            Vector2 moveVector = Vector2.zero;
            Vector2 mousePos = Vector2.zero;
            bool isSprint = false;
            bool isCrouch = false;

#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;

            if (keyboard != null)
            {
                float h = 0f;
                float v = 0f;

                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) v += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) v -= 1f;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) h -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) h += 1f;

                moveVector = new Vector2(h, v);
                isSprint = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                isCrouch = keyboard.leftCtrlKey.isPressed || keyboard.cKey.isPressed;
            }

            if (mouse != null)
            {
                mousePos = mouse.position.ReadValue();
            }
#else
            try
            {
                float h = Input.GetAxisRaw("Horizontal");
                float v = Input.GetAxisRaw("Vertical");
                moveVector = new Vector2(h, v);
                isSprint = Input.GetKey(KeyCode.LeftShift);
                isCrouch = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
                mousePos = Input.mousePosition;
            }
            catch
            {
                // Legacy Input 비활성화 시 무시
            }
#endif

            if (moveVector.sqrMagnitude > 1f)
            {
                moveVector.Normalize();
            }

            // 시선/조준 목표 방향 계산
            Vector3 lookDirection = transform.forward;

            if (lookAtMouseCursor && mainCamera != null && mousePos != Vector2.zero)
            {
                Ray ray = mainCamera.ScreenPointToRay(mousePos);
                Plane groundPlane = new Plane(Vector3.up, transform.position);

                if (groundPlane.Raycast(ray, out float enter))
                {
                    Vector3 hitPoint = ray.GetPoint(enter);
                    Vector3 toCursor = hitPoint - transform.position;
                    toCursor.y = 0f;
                    if (toCursor.sqrMagnitude > 0.01f)
                    {
                        lookDirection = toCursor.normalized;
                    }
                }
            }
            else
            {
                Vector3 worldMove = GetWorldMoveDirection(moveVector);
                if (worldMove.sqrMagnitude > 0.01f)
                {
                    lookDirection = worldMove;
                }
            }

            return new MovementInputData
            {
                MoveVector = moveVector,
                LookDirection = lookDirection,
                IsSprint = isSprint,
                IsCrouch = isCrouch
            };
        }

        private Vector3 GetWorldMoveDirection(Vector2 moveVec)
        {
            if (mainCamera != null)
            {
                Vector3 camForward = Vector3.ProjectOnPlane(mainCamera.transform.forward, Vector3.up).normalized;
                Vector3 camRight = Vector3.ProjectOnPlane(mainCamera.transform.right, Vector3.up).normalized;
                return (camRight * moveVec.x + camForward * moveVec.y).normalized;
            }

            return new Vector3(moveVec.x, 0f, moveVec.y).normalized;
        }
    }
}