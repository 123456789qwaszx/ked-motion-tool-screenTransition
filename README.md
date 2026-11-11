# ked-motion-tool-screenTransition

**완전히 독립된** 2D 움직임 저작 도구(.NET 10 + Avalonia 12). 그림 여러 장을 읽어 와 각각을 움직이고, 그래프의 곡선으로 타이밍을 잡는다.
목적은 렌더된 동영상을 대신하는 것이다 — 이 툴에서 만든 것을 유니티에서 불러오기만 해도 UI 오브젝트들이 생성되어 의도한 대로 움직인다.
화면을 덮고 걷는 전환은 그 쓰임의 하나다. 깊이 · 카메라 · 캐릭터 리그는 없다. `ked-presentation-runtime`과는 무관하다.

**지금은 기반 시설이다**(소유자, 2026-11-11). 당장 쓰는 툴이 아니라 뒤에 설 전환 툴의 바탕으로, 출발점(`ked-motion-tool-extend`의 `a522149`)에서
화면 전환에 필요 없는 것을 과감하게 걷어 내는 중이다. **산출물이 아직 없다** — 저작 원본(`*.motion-authoring.json`)을 쓰고 읽을 뿐이고,
산출물(과 그것을 읽는 유니티 쪽 코드)은 소유자가 의도와 목적을 확정한 뒤의 일이다(보류). 계획 · 지금 어디인가 · 정해진 것은 [`docs/PLAN-screenTransition.md`](docs/PLAN-screenTransition.md),
쓰는 법은 [`docs/USER-GUIDE.md`](docs/USER-GUIDE.md).

## 남아 있는 것

- **세트** — 층(그림 · 색 사각형)의 계층. 층마다 그림 한 장(경로)을 든다. 층 표 가져오기 · 그림 가져오기 · 배치 모드(끌어서 자리 잡기) · 색 견본.
- **애니메이션** — 세트의 오브젝트에 자리 · 회전 · 배율 · 불투명도의 키. 그래프 에디터 · 도프 시트 · 타임 슬라이더 · 채널 박스 · 이벤트 키(`covered`).
  시차 · 화면 밖에서 들어온다/나간다 · 제 차례 전까지 숨기기.
- **표지** — 이름 붙은 시각(이벤트 키). 예시는 `covered`(다 덮였다) · `release`(걷기 시작)를 둔다.
- **되돌리기** — 조작 하나 = 한 칸. 문제 칸(검증과 돕는 손). 설정 · 자동 저장.
- **예시 다섯** — `examples/transitions/`(station · pillar · lapis · heredity · mercury).
- **방향 코드** — 화면 비율 맞춤(`ScreenLayout`) · 덮임 검사(`CoverCheck`). 아직 화면에 안 붙었다(T3′ · T4).

## 구조

```
Ked.Presentation.Core (재생의 계산 — 순수 C#, netstandard2.1)  ──►  Mt.Authoring (도메인)  ──►  Mt.App (Avalonia 화면)
tests: Ked.Presentation.Core.Tests (NUnit) · Mt.Authoring.Tests (xunit) · Mt.App.Tests (xunit + Avalonia.Headless)
```

미리보기는 문서의 세트만으로 선다: `RigAssets.Compose(문서)` → 코어의 사각형 계층(`RectNodeTree`) → 모션의 포즈(`MotionPreview`).
무대는 기준 화면 1920×1080이고, 뷰포트에는 세트 하나가 선다(연 세트 → 고른 애니메이션의 세트).

## 명령

클라우드 컨테이너에 SDK가 없으면 `apt-get install -y dotnet-sdk-10.0`.

```bash
dotnet run --project src/Mt.App/Mt.App.csproj
```

```bash
dotnet build KedMotionTool.sln -c Release
```

```bash
dotnet test KedMotionTool.sln -c Release --no-build
```

예시 문서를 다시 쓴다(빌더 `tests/Mt.Authoring.Tests/TransitionExamples.cs`를 고친 뒤):

```bash
MT_WRITE_EXAMPLES=1 dotnet test tests/Mt.Authoring.Tests -c Release --filter TransitionExampleTests
```

화면을 그림으로 뽑는다(헤드리스가 Skia로 실제 렌더):

```bash
MT_RENDER_DIR=<폴더> dotnet test tests/Mt.App.Tests -c Release --filter TransitionExampleShots
```

작업 관습은 [`CLAUDE.md`](CLAUDE.md).
