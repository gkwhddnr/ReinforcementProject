# 장애물 라벨 관리 가이드

## 개요
현재 **ShotObstacle** 시스템은 `ShotObstacle` 컴포넌트를 부착하고 고유 `ObstacleId` 를 가진 오브젝트만 총알이 흡수되고 통계가 기록됩니다. 라벨 기반으로 전환하면 Inspector 에서 라벨만 선택하면 되므로 관리가 훨씬 수월해집니다. 기존 `ObstacleId`는 그대로 유지해 통계 기록에는 영향을 주지 않으며, 라벨은 비트 마스크(`[Flags]`) 형태로 여러 개를 동시에 지정할 수 있습니다.

---

## 1️⃣ 라벨 열거형 추가 – `ObstacleLabel.cs`
```csharp
// 경로: Assets/Scripts/Obstacles/ObstacleLabel.cs
using UnityEngine;

[System.Flags]                     // 여러 라벨을 동시에 지정 가능
public enum ObstacleLabel
{
    None        = 0,
    Wall        = 1 << 0,
    Cover       = 1 << 1,
    Terrain     = 1 << 2,
    EnemyBase   = 1 << 3,
    // 필요에 따라 추가
}
```

---

## 2️⃣ `ShotObstacle.cs` 수정
```csharp
// 경로: Assets/Scripts/Obstacles/ShotObstacle.cs
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ShotObstacle : MonoBehaviour
{
    // 기존 고유 ID (통계용)
    [Tooltip("통계 기록용 고유 ID")]
    public string ObstacleId;

    // ★ 새 라벨 필드
    [Tooltip("이 오브젝트의 의미 라벨(복수 선택 가능)")]
    public ObstacleLabel Labels = ObstacleLabel.None;

    // -----------------------------------------------------------------
    // 기존 총알 충돌 처리 (간단히)
    // -----------------------------------------------------------------
    private void OnCollisionEnter(Collision collision)
    {
        var bullet = collision.gameObject.GetComponent<Bullet>();
        if (bullet == null) return;

        // ★ 라벨 기반 필터링
        // 예시: Wall 또는 Terrain 라벨이 있으면 흡수 처리
        var absorbMask = ObstacleLabel.Wall | ObstacleLabel.Terrain;
        if ((Labels & absorbMask) == 0) return;   // 라벨이 맞지 않으면 무시

        // 기존 로직 – 통계 기록, 총알 소멸 등
        RecordHit(bullet);
        bullet.Absorb();      // Bullet 클래스에 정의된 흡수 로직
    }

    private void RecordHit(Bullet bullet)
    {
        // 기존대로 ObstacleId 로 통계 로그
        StatsLogger.RecordObstacleHit(ObstacleId, bullet.Damage);
    }
}
```
### 변경점 정리
| 구분 | 기존 | 새로 추가/변경 |
|------|------|----------------|
| 감지 조건 | `ShotObstacle` 존재 + `ObstacleId` | `ShotObstacle` 존재 + 라벨 마스크가 일치 |
| 설정 방식 | 컴포넌트를 일일이 추가 | Inspector 의 **Labels** 드롭다운에서 선택 |
| 통계 | `ObstacleId` 로 기록 | 동일 – `ObstacleId`는 그대로 사용 |

---

## 3️⃣ 프리팹·씬 오브젝트 라벨 지정
1. 프리팹 혹은 씬 안의 오브젝트를 열고 `ShotObstacle` 컴포넌트를 확인합니다.
2. `ObstacleId`는 기존 그대로 유지하거나 필요시 새로 생성합니다.
3. **Labels** 드롭다운에서 해당 오브젝트에 알맞은 라벨을 선택합니다. (Shift‑클릭으로 다중 선택 가능)
4. 통계가 필요 없는 단순 라벨만 쓰는 경우엔 `ShotObstacle` 컴포넌트를 제거하고 아래 **옵션 스크립트**를 사용할 수 있습니다.

---

## 4️⃣ 선택 사항 – 라벨 전용 가벼운 스크립트
라벨만 필요하고 통계 기록이 필요 없는 정적 오브젝트가 많다면 별도 경량 스크립트를 만들고, 총알 충돌 로직을 해당 스크립트를 검사하도록 확장합니다.
```csharp
// 경로: Assets/Scripts/Obstacles/ObstacleTag.cs
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ObstacleTag : MonoBehaviour
{
    public ObstacleLabel Labels = ObstacleLabel.None;
}
```
총알 충돌 코드에서 `ShotObstacle` 혹은 `ObstacleTag` 중 하나가 존재하면 라벨을 확인하도록 하면 됩니다.

---

## 5️⃣ 인수인계 체크리스트
| ✅ | 작업 항목 | 비고 |
|---|----------|------|
| 1 | `ObstacleLabel.cs` (enum) 추가 | |
| 2 | `ShotObstacle.cs`에 `Labels` 필드와 라벨 마스크 검사 로직 삽입 | |
| 3 | 모든 장애물 프리팹·씬 오브젝트에 라벨 지정, `ObstacleId` 유지 | |
| 4 | (선택) `ObstacleTag.cs` 추가 및 충돌 로직 수정 | 라벨‑전용 객체용 |
| 5 | `StatsLogger.RecordObstacleHit` 호출이 기존대로 동작하는지 테스트 | |
| 6 | 플레이 테스트 – 라벨이 맞는 오브젝트에 총알 맞으면 흡수·통계 기록 확인 | |
| 7 | 변경 내용 커밋 및 커밋 메시지: “라벨 기반 장애물 감지 도입” | |

---

## 6️⃣ 기대 효과
* **관리 용이** – 라벨만 선택하면 되므로 UI 상에서 바로 확인·수정 가능.
* **성능** – 비트 마스크 연산은 O(1) 수준, 기존 컴포넌트 검사와 거의 동일하거나 더 가볍게.
* **호환성** – 기존 `ObstacleId` 로 통계는 그대로 기록되므로 기존 분석 파이프라인에 영향 없음.

---

> **추가 요청**이 있으면 언제든 알려 주세요. (예: 라벨 자동 생성 스크립트, 대량 프리팹 업데이트 툴 등)
