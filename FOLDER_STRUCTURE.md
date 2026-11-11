# 프로젝트 폴더 구조

Unity 6000.3.28f1 / URP 17.3.0. 담당자별 기능을 분리하고 공유 자산은 협의하여 관리한다.

| 경로 | 담당 및 범위 |
|---|---|
| Assets/이용현/Movement | 이동·회전·경사 이동 |
| Assets/이승재/Shooting | 조준·사격·탄약·재장전 |
| Assets/김현석/Decision | 상황 판단·행동 지침·상태 전환 |
| Assets/김현석/Training | ML-Agents 연결·관측·행동·보상 |
| Assets/하종욱/Landscape | 시드 기반 지형·지형지물·스폰·경로 검증 |
| Assets/공유 | 통합 씬·공통 프리팹·머티리얼·합의된 인터페이스 |

각 기능 아래 Scripts(코드), Prefabs(재사용 오브젝트), Scenes(담당자 검증 씬), Data(설정 에셋)를 둔다. Landscape의 Materials는 지형 머티리얼, Art는 모델·텍스처용이다. 공유/Contracts는 여러 기능이 함께 사용하는 합의된 계약 전용이며 기능 소유 코드는 담당자 폴더에 둔다. Python 학습 설정·실험 기록의 위치는 실제 학습 구성 시 정한다.

## 공동작업 원칙

- 담당자 검증 씬에서 작업하고 공유 씬은 수정 담당을 정해 통합한다. 같은 씬·프리팹의 동시 편집을 피한다.
- 다른 담당자 폴더와 공유 계약의 변경은 사전 협의하고 PR로 검토한다.
- 에셋 이동·이름 변경은 Unity에서 수행하며 .meta와 GUID를 보존한다. 새 에셋과 폴더의 .meta도 함께 커밋한다.
- 빈 하위 폴더는 .gitkeep으로 유지한다. 파일이 생기면 해당 .gitkeep은 제거해도 된다.
- 기존 Assets/Scenes, Settings, TutorialInfo와 기본 에셋은 이동하지 않았다.
- .Codex/, .Unity/ 등 AI 지침서는 GitHub에서 제외한다. Library/, Temp/, Logs/, UserSettings/ 및 생성된 IDE 파일도 추적하지 않는다.
- 저장소: https://github.com/gkwhddnr/ReinforcementProject
