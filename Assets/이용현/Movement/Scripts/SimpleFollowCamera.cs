using UnityEngine;

namespace Reinforce.Movement
{
    /// <summary>
    /// 플레이어 이동 검증용 3인칭 추적 카메라
    /// </summary>
    public class SimpleFollowCamera : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;

        [Header("Offsets")]
        [SerializeField] private Vector3 offset = new Vector3(0f, 6f, -9f);
        [SerializeField] private float smoothSpeed = 10f;
        [SerializeField] private float lookAtHeight = 1.2f;

        private void LateUpdate()
        {
            if (target == null) return;

            Vector3 desiredPosition = target.position + offset;
            transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);

            Vector3 lookTarget = target.position + Vector3.up * lookAtHeight;
            transform.LookAt(lookTarget);
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            if (target != null)
            {
                transform.position = target.position + offset;
                transform.LookAt(target.position + Vector3.up * lookAtHeight);
            }
        }
    }
}