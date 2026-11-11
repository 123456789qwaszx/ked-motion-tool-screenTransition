# ked-motion-tool-screenTransition

**완전히 독립된** 2D 움직임 저작 도구(.NET 10 + Avalonia 12). 그림 여러 장을 읽어 와 각각을 움직이고, 그래프의 곡선으로 타이밍을 잡는다. 목적: 렌더된 동영상 대신 — 유니티에서 불러오기만 해도 UI 오브젝트들이 생성되어 의도한 대로 움직인다.

**쓰임(소유자, 2026-11-11)**: 유니티 UI로 **화면 전환(ScreenTransition) · UI 컷인(CutIn)** 같은 연출을 툴의 뷰포트에서 "perspectiveView 느낌으로" 보며 편하게 만든다. **특정 유니티 환경을 크게 요구하지 않는다** — 유니티 쪽은 기본 UGUI(`RectTransform` · `Image` · `CanvasGroup`)만으로 선다. 자세히는 계획서 §0 "이 작업을 어디에 쓰나".

**지금은 기능 개발이 아니라 기반 시설을 다듬고 불필요한 아키텍처를 걷어 내는 때다**(소유자, 2026-11-11). 기능 단계(T3′ 이후 · G1~G5 · T6)는 소유자가 의도와 목적을 확정할 때까지 보류다.

- 먼저 읽을 것: `docs/PLAN-screenTransition.md` — 맨 위 "지금 어디인가" · 정해진 것(§2) · 단계(§7 — T1~T6, 범용화 G1~G5는 §10) · 걷어 낼 것과 남길 것. 단계가 끝날 때마다 그 문서에 결정과 상태를 적는다.
- `main`은 출발점(`ked-motion-tool-extend`의 `a522149`) 그대로다. 일은 `dev`에서 한다.
- 이 저장소는 **따로 뻗어 나간다**. `ked-presentation-runtime`과 무관하고, 같은 뿌리의 `ked-motion-tool-extend`(컷) · `ked-motion-tool-characterRig`(캐릭터 모션 · 이모지)와도 맞물려 있지 않다 — 그 둘의 일(카메라 · 깊이 · 캐릭터 · 씬)도, 런타임 저장소를 고치거나 따라가는 일도 여기서 하지 않는다.
- 기준 런타임은 유니티 UGUI 하나. 기능을 받는 잣대는 "UGUI에서 특수한 셰이더 없이 되는 것".

## 구조

`ScreenTransition.Core`(재생의 계산 — 순수 C#, netstandard2.1 · C# 9) → `ScreenTransition.Authoring`(도메인) → `ScreenTransition.App`(Avalonia 화면).
테스트: `tests/ScreenTransition.Authoring.Tests` · `tests/ScreenTransition.App.Tests`(xunit — 화면은 `HeadlessUi.Run`) · `tests/ScreenTransition.Core.Tests`(NUnit).

## 명령

클라우드 컨테이너에 SDK가 없으면: `apt-get install -y dotnet-sdk-10.0`.

```bash
dotnet build ScreenTransition.sln -c Release
```

```bash
dotnet test ScreenTransition.sln -c Release --no-build
```

예시 문서를 다시 쓴다(빌더 `tests/ScreenTransition.Authoring.Tests/TransitionExamples.cs`를 고친 뒤):

```bash
ST_WRITE_EXAMPLES=1 dotnet test tests/ScreenTransition.Authoring.Tests -c Release --filter TransitionExampleTests
```

화면을 그림으로 뽑는다(헤드리스가 Skia로 실제 렌더 — 고친 화면은 눈으로 확인한다):

```bash
ST_RENDER_DIR=<폴더> dotnet test tests/ScreenTransition.App.Tests -c Release --filter TransitionExampleShots
```

## 관습

- 코드 주석 · 문서 · 커밋 메시지는 **한국어**. 커밋은 "무엇을 왜" 한 줄 + 본문.
- **한 번에 하나.** 단계마다 전체 테스트가 통과한 채로 커밋한다.
- `TreatWarningsAsErrors` — 안 쓰는 필드·using·인자는 빌드가 막는다.
- 편집은 `MotionEditor`의 명령으로만. **조작 하나 = 되돌리기 한 칸**, 같은 값은 기록하지 않는다. 테스트가 `editor.Revision`으로 이것을 본다.
- **안 쓰는 기능은 흔적 없이 지운다** — 코드 · 주석 · 문서 · 테스트까지. 죽은 코드를 남기지 않는다.
- 단축키는 F1 표에, 쓰는 법은 README와 `docs/USER-GUIDE.md`에.
- 화면의 기능에는 테스트 손잡이(`internal … FromForm()`)와 헤드리스 테스트를 붙인다.
- 툴의 미리보기와 게임의 재생은 **같은 계산**이어야 한다 — 그 계산은 `ScreenTransition.Core`에 순수 함수로 두고, 둘이 같은 모습을 내는지 프레임마다 맞추는 테스트를 둔다. 여기서 "게임"은 **이 툴의 산출물을 읽는 유니티 쪽 코드**다 — 그 코드도 이 툴의 것이다(런타임 저장소에 넣지 않는다, 뒤의 일).
- PR은 요청받았을 때만 만든다.
