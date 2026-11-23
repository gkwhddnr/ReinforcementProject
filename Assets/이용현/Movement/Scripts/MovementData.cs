using UnityEngine;

namespace Reinforce.Movement
{
    /// <summary>
    /// 캐릭터의 실제 적용된 자세 상태 (팀 전달 요청서 규격: Standing=0, Crouching=1)
    /// </summary>
    public enum PostureState
    {
        Standing = 0,
        Crouching = 1
    }

    /// <summary>
    /// 입력 프로바이더(키보드 또는 ML-Agents 브릿지)가 컨트롤러에 전달하는 입력 데이터 구조체
    /// </summary>
    public struct MovementInputData
    {
        public Vector2 MoveVector;     // x: 좌우(-1~1), y: 전후(-1~1)
        public Vector3 LookDirection;  // 월드 공간 목표 전방 방향 벡터 (수평 회전용)
        public bool IsSprint;          // 전력 질주 여부
        public bool IsCrouch;          // 앉기 여부
        public bool IsFire;            // 사격 발사 유지 여부 (Fire Held)
    }
}
