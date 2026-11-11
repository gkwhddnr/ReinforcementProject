# ReinforcementProject

랜덤 산악 지형의 배틀로얄 게임 환경에서 생존·전략 행동을 학습하는 강화학습 팀 프로젝트입니다.

## 개발 환경

- Unity **6.3 LTS (6000.3.28f1)**
- Universal Render Pipeline (URP) **17.3.0**
- 강화학습 도구: Unity ML-Agents 도입 예정 (현재 패키지 미설치)

Unity Hub에서 위 Editor 버전을 설치하고 이 저장소의 루트 폴더를 프로젝트로 추가합니다. `Assets`, `Packages`, `ProjectSettings`를 함께 내려받아야 합니다.

## 담당 및 폴더

| 담당 | 경로 | 역할 |
|---|---|---|
| 이용현 | Assets/이용현/Movement | 이동·회전·경사 이동 |
| 이승재 | Assets/이승재/Shooting | 조준·사격·탄약·재장전 |
| 김현석 | Assets/김현석/Decision, Training | 상황 판단·강화학습 연결 |
| 하종욱 | Assets/하종욱/Landscape | 지형 생성·지형지물·스폰·경로 검증 |
| 공동 | Assets/공유 | 통합 씬·공통 프리팹·머티리얼·인터페이스 |

상세 배치와 협업 원칙은 [폴더 구조](FOLDER_STRUCTURE.md)를 참고합니다. 현재는 프로젝트와 폴더 구조를 준비한 단계이며 게임 및 학습 기능은 이후 구현합니다.

## 소스 관리

에셋과 `.meta` 파일을 함께 커밋합니다. Unity 캐시·빌드 결과·로컬 설정 및 AI 지침서 폴더(`.Codex`, `.Unity` 등)는 GitHub에 올리지 않습니다. 공유 씬·프리팹은 동시 편집을 피하고 수정 담당을 정해 통합합니다.

이전 UE5 프로젝트는 현재 소스에서 Unity 프로젝트로 대체했으며, 과거 커밋 이력은 유지합니다.
