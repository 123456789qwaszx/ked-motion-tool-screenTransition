# 화면 전환 예시

화면 전환 다섯 장면 — 각각 **세트** 하나와 **타임라인** 하나로 지었다(쓰는 법은 `docs/USER-GUIDE.md`).

- `station/station.transition-authoring.json` — 앱에서 **열기…** 로 연다. 그림은 `station/art/`(층 PNG 열 장 + 층 표 `layers.tsv`).
- `pillar/pillar.transition-authoring.json` — 기둥과 노을. 층들을 같은 타이밍으로 놓고 **시차**로 밀어 지은 것(지평선의 층들이 차례로 솟고, 해가 떠오르고, 기둥이 넘어지듯 들어온다).
  해는 제 차례(0.6초) 전까지 숨어 있다("제 차례 전까지 숨기기") — 떠오르고, 머묾에서 빛이 두 번 숨쉬고, 퇴장의 처음에 진다.
- `lapis/` · `heredity/` · `mercury/` — 나머지 세 장면.
  - `lapis`(바닷속 기둥들) — 바닥과 기둥이 차례로 솟고 양옆의 줄기가 닫히듯 들어온다("화면 밖에서 들어온다"). 구슬이 톡 나타나 둥실거리다 사그라든다.
  - `heredity`(달과 떠 있는 섬) — 달이 커지며 떠오르고 섬이 솟는다. 씨앗(구슬 + 소용돌이)이 돌고 수정(수정 + 빛)이 반짝인다.
  - `mercury`(행성과 고래) — 대기가 번지고 행성이 솟고 고래가 가로질러 들어온다. 별이 켜지고 깜빡이다 꺼진다.
- 문서는 편집기 명령으로 지었다: `tests/Mt.Authoring.Tests/TransitionExamples.cs`. 고쳤으면
  `MT_WRITE_EXAMPLES=1 dotnet test tests/Mt.Authoring.Tests --filter TransitionExampleTests`로 다시 쓴다.
- 그림은 `tools/make-transition-art.py`가 도형으로 그린다(다섯 장면 전부 저장소에 있다).
- 화면을 그림으로 뽑기: `MT_RENDER_DIR=<폴더> dotnet test tests/Mt.App.Tests --filter TransitionExampleShots`.

## 어떻게 지었나

1. 빈 세트 `station`에 **층 표를 들인다** — 바탕색(색 사각형) + 층 열 장이 그림을 든 오브젝트 열하나로 한 번에 선다.
2. 타임라인 `station`(4.1초, 키는 가만히 선 자세에서 얼마나 떨어져 있는가) — 빌더가 세 구간을 따로 짓고 한 타임라인으로 잇는다:
   - 등장(0 ~ 1.2초) — 바탕색이 화면을 덮고(0.25초에 표지 `covered` — 게임이 여기서 장면을 바꾼다), 층들이 시차를 두고 제자리로 들어온다.
   - 머묾(1.2 ~ 3.2초, 한 바퀴) — 미생물이 떠다니고 끝의 빛이 숨쉰다. 처음과 끝의 모습이 같아 되풀이할 수 있다.
   - 퇴장(3.2초의 표지 `release`부터 4.1초까지) — 층들이 들어온 길로 물러나고 바탕색이 걷힌다.

## 보는 법

- 세트 목록에서 `station`을 고르면 그 타임라인이 묶인다. 재생 줄의 **무대만**을 켜면 무대 밖이 가려져 게임에서 보이는 그대로다.
- **배치** 토글을 켜면 배치 모드 — 층을 끌어 가만히 선 자세를 잡는다. 끄면 끌기가 타임라인의 키가 된다.
