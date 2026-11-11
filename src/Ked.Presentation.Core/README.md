# Ked.Presentation.Core — 재생의 계산

툴의 미리보기와 게임의 재생이 **같은 계산**을 쓰도록 둔 순수 C#(외부 의존성 없음).
`ked-stage-runtime`의 연출 코어(`dc690c1`)에서 갈라져 나왔고, 이제 이 저장소의 것이다 — 저쪽과 동기화하지 않는다(계획서 §2의 6).
무대 상태 · 리듀서 · 커맨드 토큰 · 튜닝 표 · 이징은 화면 전환에 필요 없어 걷었다(T1-5, 2026-11-11). 리그 스키마(`RigSchemaDto` · `RigSchemaLoader`)도 걷었다 — 툴이 세트에서 트리를 바로 세운다(구조 #2). 옛 모습은 출발 커밋 `a522149`에 있다.

| 폴더 | 담은 것 |
|---|---|
| `Primitives/` | `Vec2` · `Vec3` — 유니티 Vector2/3의 순수 대응 |
| `Transforms/` | `RectNodeState`(RectTransform 상태 값) · `RectSpace` · `RectChainMath`(앵커 · 피벗 · 배율 · 회전 Z→X→Y 사슬의 좌표 변환) |
| `State/` | `RectNodeTree` — 노드 키로 부모 사슬을 세워 `RectChainMath`에 넘긴다 |
| `Ease/` | `CurveKey` · `CurveFunctions`(유니티 AnimationCurve.Evaluate의 대응 — 에르미트, 계단 키) |
| `Motion/` | `MotionClip`(채널 · 이벤트 키) · `MotionMath`(샘플 · relative/absolute 합성) · `MotionRules`(모양 검증) · `Timing`(24fps) |

## 안전핀

`Ked.Presentation.Core.csproj`가 netstandard2.1 · C# 9 · 엔진 참조 없음을 컴파일러 제약으로 지킨다 — 이 코드가 유니티(UGUI)의
재생기로 그대로 들어가야 한다(계획서 §2의 11). 산출물을 읽는 유니티 쪽 재생기(보류)도 이 계산 위에 선다.
