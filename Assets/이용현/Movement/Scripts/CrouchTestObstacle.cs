using UnityEngine;

namespace Reinforce.Movement
{
    /// <summary>
    /// 앉기(Crouch) 및 기립 공간 검사(CanStandUp) 수동 검증용 테스트 장애물 터널
    /// 바닥에서 1.5m 높이에 천장이 위치하여 서서는 통과할 수 없고 앉아서만 통과 가능합니다.
    /// </summary>
    public class CrouchTestObstacle : MonoBehaviour
    {
        [Header("Obstacle Settings")]
        [Tooltip("천장 아래 통과 가능 높이 (서기 2.0m > 1.5m > 앉기 1.2m)")]
        [SerializeField] private float clearanceHeight = 1.5f;
        [SerializeField] private float tunnelWidth = 4.0f;
        [SerializeField] private float tunnelLength = 5.0f;
        [SerializeField] private float ceilingThickness = 0.3f;

        private void Awake()
        {
            BuildTunnel();
        }

        private void BuildTunnel()
        {
            // 이미 생성된 자식 오브젝트가 있다면 중복 생성 방지
            if (transform.childCount > 0) return;

            // 1. 천장 (Ceiling)
            GameObject ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceiling.name = "Ceiling";
            ceiling.transform.SetParent(transform, false);
            float ceilingCenterY = clearanceHeight + ceilingThickness * 0.5f;
            ceiling.transform.localPosition = new Vector3(0f, ceilingCenterY, 0f);
            ceiling.transform.localScale = new Vector3(tunnelWidth, ceilingThickness, tunnelLength);

            // 2. 좌측 기둥 벽 (Left Wall)
            GameObject leftWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftWall.name = "LeftWall";
            leftWall.transform.SetParent(transform, false);
            leftWall.transform.localPosition = new Vector3(-tunnelWidth * 0.5f - 0.1f, clearanceHeight * 0.5f, 0f);
            leftWall.transform.localScale = new Vector3(0.2f, clearanceHeight, tunnelLength);

            // 3. 우측 기둥 벽 (Right Wall)
            GameObject rightWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightWall.name = "RightWall";
            rightWall.transform.SetParent(transform, false);
            rightWall.transform.localPosition = new Vector3(tunnelWidth * 0.5f + 0.1f, clearanceHeight * 0.5f, 0f);
            rightWall.transform.localScale = new Vector3(0.2f, clearanceHeight, tunnelLength);

            // 색상 눈에 띄게 (주황/노랑)
            Color obstacleColor = new Color(0.9f, 0.5f, 0.1f, 1f);
            Renderer rCeil = ceiling.GetComponent<Renderer>();
            if (rCeil != null) rCeil.material.color = obstacleColor;
            Renderer rLeft = leftWall.GetComponent<Renderer>();
            if (rLeft != null) rLeft.material.color = obstacleColor * 0.8f;
            Renderer rRight = rightWall.GetComponent<Renderer>();
            if (rRight != null) rRight.material.color = obstacleColor * 0.8f;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Vector3 center = transform.position + new Vector3(0f, clearanceHeight * 0.5f, 0f);
            Gizmos.DrawWireCube(center, new Vector3(tunnelWidth, clearanceHeight, tunnelLength));
        }
    }
}
