using System;

namespace Ked.Presentation.Core
{
    /// <summary>채널이 쓰는 노드 속성 — 위치 · 배율 · 회전 · 불투명도.</summary>
    public enum MotionChannelKind
    {
        /// <summary>anchoredPosition (X·Y). 리그 로컬 px.</summary>
        Position,

        /// <summary>localScale (X·Y). z는 건드리지 않는다.</summary>
        Scale,

        /// <summary>localEulerAngles (X·Y·Z). 도.</summary>
        Euler,

        /// <summary>
        /// CanvasGroup alpha (X 하나, 0~1).
        /// RectNodeState에 살지 않으므로 시작값은 호출부가 든 불투명도에서 온다(MotionMath.StartOf의 alpha 인자).
        /// relative는 배율처럼 **곱**이다(중립값 1) — "지금 불투명도에서 흐려진다".
        /// </summary>
        Alpha,
    }

    /// <summary>
    /// 키 값을 시작값에 대한 변화로 읽을지(relative), 그 자체로 읽을지(absolute).
    /// **채널이 아니라 쓰는 쪽(호출부)이 정한다** — 같은 클립을 "지금 자리에서"(relative)로도 "이 포즈로"(absolute)로도 쓸 수 있다.
    /// relative의 뜻은 종류마다 다르다 — 위치·회전은 **덧셈**, 배율·불투명도는 **곱셈**(MotionMath.Neutral).
    /// </summary>
    public enum MotionChannelMode
    {
        Relative,
        Absolute,
    }

    /// <summary>
    /// 모션이 끝난 뒤 무엇이 남는가.
    /// </summary>
    public enum MotionEnd
    {
        /// <summary>
        /// 아무것도 남기지 않는다 — 흔들기·끄덕임. 끝나면 노드가 **시작값으로 돌아간다**(재생이 시작값을 되쓴다).
        /// 마지막 키가 중립값이 아니면 끝에서 튄다 — 규칙이 막지 않는다(mode를 모르면 중립값의 뜻이 없다).
        /// </summary>
        Restore,

        /// <summary>
        /// 마지막 키가 남는다 — 고개 숙인 채로, 자세 바꾸기. 채널마다 마지막 키가 그 노드에 남는다.
        /// 중립값으로 끝나는 채널은 자연히 아무것도 남기지 않으므로, "어깨는 남고 고개는 돌아오는" 모션도 commit이다.
        /// </summary>
        Commit,
    }

    /// <summary>
    /// 모션의 채널 하나 — 노드 하나의 속성 하나를 시간에 따라 움직인다.
    /// 성분(X·Y·Z)은 따로 키를 갖고, null인 성분은 "건드리지 않는다"(시작값 유지). 값은 **mode를 모른다** —
    /// relative/absolute는 재생하는 쪽이 정한다(MotionChannelMode).
    /// 키의 Time은 **초**다(이징 곡선의 정규화 0~1과 다르다). 마지막 키의 Time = 클립 길이.
    ///
    /// readonly가 아닌 이유: 유니티 직렬화([Serializable])를 타야 한다 — CurveKey와 같은 사정.
    /// </summary>
    [Serializable]
    public struct MotionChannel
    {
        /// <summary>리그 스키마의 노드 id (예: CharacterPortrait_Track_Move_Y). 슬롯 접두사는 없다.</summary>
        public string Node;

        public MotionChannelKind Kind;

        public CurveKey[] X;
        public CurveKey[] Y;

        /// <summary>Euler에서만 쓴다. Position·Scale이면 null이어야 한다(규칙).</summary>
        public CurveKey[] Z;

        public bool HasX => X is { Length: > 0 };
        public bool HasY => Y is { Length: > 0 };
        public bool HasZ => Z is { Length: > 0 };
    }

    /// <summary>
    /// 모션의 이벤트 키 — 재생이 Time을 지나는 순간 호스트가 (Name, Value)로 한 번 알린다(예: <c>covered</c> — 화면이 다 덮였다).
    /// 상태가 아니다: 건너뛰기 · 되감기에서 다시 울리지 않는다(효과음과 같은 급).
    /// </summary>
    [Serializable]
    public struct MotionEvent
    {
        /// <summary>초. 0 ≤ Time ≤ 클립 길이.</summary>
        public float Time;

        /// <summary>이벤트 이름 — [a-z0-9_]+. 호스트가 이 이름으로 받는 쪽을 고른다.</summary>
        public string Name;

        /// <summary>딸린 값(자유 문자열, 없으면 null). 예: 이모지 키 · 효과음 이름.</summary>
        public string Value;
    }

    /// <summary>
    /// 모션 하나 — 이름 · 어느 리그용인지 · 길이(초) · 채널들.
    ///
    /// 어느 노드의 어느 값이 몇 초에 얼마인지, 여러 채널이 동시에. 키 하나의 수학은 CurveKey · CurveFunctions다.
    /// 값은 리그 로컬 단위(px·배율·도)다.
    ///
    /// 종점은 평가 결과가 아니라 **키 값**이다 — commit이면 채널마다 마지막 키가 남고(MotionMath.EndValue), restore면 남는 것이 없다.
    /// </summary>
    [Serializable]
    public sealed class MotionClip
    {
        public string Name;

        /// <summary>리그 스키마 이름(rigKind). 노드 id는 이 스키마로 검증한다.</summary>
        public string Rig;

        /// <summary>초. 모든 채널의 모든 성분이 이 시각에 마지막 키를 둔다(규칙).</summary>
        public float Duration;

        /// <summary>끝난 뒤 무엇이 남는가.</summary>
        public MotionEnd End;

        public MotionChannel[] Channels;

        /// <summary>이벤트 키들(시각 오름차순). 없으면 null.</summary>
        public MotionEvent[] Events;
    }
}
