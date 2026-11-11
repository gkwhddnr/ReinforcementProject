# ReinforcementProject

랜덤 산악 지형의 배틀로얄 게임 환경에서 생존·전략 행동을 학습하는 강화학습 팀 프로젝트입니다.

## 개발 환경

- Unity **6.3 LTS (6000.3.28f1)**
- Universal Render Pipeline (URP) **17.3.0**
- 강화학습 도구: Unity ML-Agents **4.2.0** (Python 학습 도구는 별도 설치)
- Unity MCP: **MCP for Unity 10.3.0** (com.coplaydev.unity-mcp), Git 패키지로 설치

Unity Hub에서 위 Editor 버전을 설치하고 이 저장소의 루트 폴더를 프로젝트로 추가합니다. `Assets`, `Packages`, `ProjectSettings`를 함께 내려받아야 합니다.

## 패키지 설치 및 연결

- Unity에서 처음 프로젝트를 열면 `Packages/manifest.json`과 `Packages/packages-lock.json`에 따라 MCP 및 ML-Agents와 의존 패키지가 복원됩니다. Git 패키지 다운로드를 위해 Git 설치와 네트워크 연결이 필요합니다.
- MCP는 `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main`을 참조하며 현재 잠금 파일의 커밋은 `81542a7e86292b5c4c21b1782c5af23b567f45ce`입니다. 팀원은 잠금 파일을 함께 사용하고 임의 업데이트를 피합니다.
- MCP 패키지 설치와 AI 클라이언트 연결은 별개입니다. 각자 Unity MCP 창에서 서버 실행 및 사용하는 클라이언트 연결을 설정합니다. 개인 MCP 설정과 AI 지침서는 공유하지 않습니다.
- ML-Agents Unity 패키지는 설치되어 있습니다. 실제 학습 실행에는 호환되는 Python 학습 환경을 별도로 구성해야 합니다. 현재 학습 환경이나 에이전트 구현은 포함하지 않습니다.
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
