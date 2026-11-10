# 화면 이동 연출 예시

화면 이동 연출 하나를 **세트**로 지은 것이다(`docs/PLAN-set.md` S6) — 이모지 밖에서 범용 구조가 서는지 보려고 만들었다.

- `station/station.motion-authoring.json` — 앱에서 **열기…** 로 연다. 그림은 `station/art/`(층 PNG 열 장 + 층 표 `layers.tsv`).
- `pillar/pillar.motion-authoring.json` — 기둥과 노을. 층들을 같은 타이밍으로 놓고 **시차**로 밀어 지은 것(지평선의 층들이 차례로 솟고, 해가 떠오르고, 기둥이 넘어지듯 들어온다).
  해는 제 차례(0.6초) 전까지 숨어 있다("제 차례 전까지 숨기기") — 떠오르고, 머묾에서 빛이 두 번 숨쉬고, 퇴장의 처음에 진다.
- `lapis/` · `heredity/` · `mercury/` — 나머지 세 장면.
  - `lapis`(바닷속 기둥들) — 바닥과 기둥이 차례로 솟고 양옆의 줄기가 닫히듯 들어온다("화면 밖에서 들어온다"). 구슬이 톡 나타나 둥실거리다 사그라든다.
  - `heredity`(달과 떠 있는 섬) — 달이 커지며 떠오르고 섬이 솟는다. 씨앗(구슬 + 소용돌이)이 돌고 수정(수정 + 빛)이 반짝인다.
  - `mercury`(행성과 고래) — 대기가 번지고 행성이 솟고 고래가 가로질러 들어온다. 별이 켜지고 깜빡이다 꺼진다.
- 문서는 편집기 명령으로 지었다: `tests/Mt.Authoring.Tests/TransitionExamples.cs`. 고쳤으면
  `MT_WRITE_EXAMPLES=1 dotnet test tests/Mt.Authoring.Tests --filter EmojiExampleTests`로 다시 쓴다.
- 그림은 `tools/make-transition-art.py`가 도형으로 그린다(다섯 장면 전부 저장소에 있다).
- 화면을 그림으로 뽑기: `MT_RENDER_DIR=<폴더> dotnet test tests/Mt.App.Tests --filter TransitionExampleShots`.

## 어떻게 지었나

1. 빈 세트 `station`에 **층 표를 들인다** — 바탕색(색 사각형) + 층 열 장이 오브젝트 열하나와 스킨으로 한 번에 선다.
2. 애니메이션 셋(전부 **상대값**: 가만히 선 자세에서 얼마나 떨어져 있는가):
   - `station_in`(1.2초) — 바탕색이 화면을 덮고(0.25초에 이벤트 `covered` — 대본이 여기서 장면을 바꾼다), 층들이 시차를 두고 제자리로 들어온다.
   - `station_hold`(2초, 되풀이) — 미생물이 떠다니고 끝의 빛이 숨쉰다.
   - `station_out`(0.9초) — 층들이 들어온 길로 물러나고 바탕색이 걷힌다.
3. 연출 `station` — **화면에 선다**, 1초 머문다. 대본은 이 이름으로 부른다.

## 보는 법

- **연출** 탭에서 `station`을 고르면 수명 한 번이 돈다(씬에 세트를 놓지 않아도 된다). 재생 줄의 **무대만**을 켜면 무대 밖이 가려져 게임에서 보이는 그대로다.
- 왼쪽 **세트** 목록에서 `station`을 열면 배치 모드 — 층을 끌어 자리를 잡는다. 그 아래 **애니메이션** 목록에서 하나를 고르면 그 애니메이션을 고친다.
