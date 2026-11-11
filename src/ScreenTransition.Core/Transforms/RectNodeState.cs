namespace ScreenTransition.Core
{
    /// <summary>
    /// 노드 하나의 RectTransform 상태 값. 불변 구조체.
    ///
    /// anchoredPosition이 실제 localPosition이 되려면
    /// anchorMin/anchorMax/pivot/sizeDelta와 "부모 rect 크기"가 계산에 들어감.
    ///
    /// 그 중, 필요한 것 5종만 담음.
    ///
    /// 담은 것
    /// - AnchoredPosition : 위치 채널이 쓰는 자리
    /// - AnchorMin/Max    : 스트레치 (0,0)-(1,1) 또는 고정 앵커 (0.5,0.5)
    /// - Pivot            : 기본 (0.5,0.5)
    /// - SizeDelta        : 스트레치 앵커면 "앵커 간격 대비 증감", 고정 앵커면 "크기 그 자체"
    /// - LocalScale       : 배율 채널. z는 유니티 기본값 1을 유지.
    /// - LocalEulerAngles : 회전 채널. 3축 전부 가져옴.
    ///
    /// 뺀 것
    /// - localPosition.z  : 어디에서도 쓰지 않는다. 항상 0으로 취급.
    /// - offsetMin/Max    : anchoredPosition + sizeDelta와 같은 정보의 다른 표현이라 중복.
    /// </summary>
    public readonly struct RectNodeState
    {
        public readonly Vec2 AnchoredPosition;
        public readonly Vec2 AnchorMin;
        public readonly Vec2 AnchorMax;
        public readonly Vec2 Pivot;
        public readonly Vec2 SizeDelta;
        public readonly Vec3 LocalScale;
        public readonly Vec3 LocalEulerAngles;

        public RectNodeState(
            Vec2 anchoredPosition,
            Vec2 anchorMin,
            Vec2 anchorMax,
            Vec2 pivot,
            Vec2 sizeDelta,
            Vec3 localScale,
            Vec3 localEulerAngles)
        {
            AnchoredPosition = anchoredPosition;
            AnchorMin = anchorMin;
            AnchorMax = anchorMax;
            Pivot = pivot;
            SizeDelta = sizeDelta;
            LocalScale = localScale;
            LocalEulerAngles = localEulerAngles;
        }

        /// <summary>
        /// 빌더 StretchFull과 같은 값: 앵커 (0,0)-(1,1) / pivot 가운데 / 크기 증감 0 / 스케일 1.
        /// 노드의 초기 상태.
        /// </summary>
        public static readonly RectNodeState StretchFull = new(
            anchoredPosition: Vec2.Zero,
            anchorMin: Vec2.Zero,
            anchorMax: Vec2.One,
            pivot: Vec2.Half,
            sizeDelta: Vec2.Zero,
            localScale: Vec3.One,
            localEulerAngles: Vec3.Zero);

        public RectNodeState WithAnchoredPosition(Vec2 value)
            => new(value, AnchorMin, AnchorMax, Pivot, SizeDelta, LocalScale, LocalEulerAngles);

        public RectNodeState WithAnchors(Vec2 min, Vec2 max)
            => new(AnchoredPosition, min, max, Pivot, SizeDelta, LocalScale, LocalEulerAngles);

        public RectNodeState WithPivot(Vec2 value)
            => new(AnchoredPosition, AnchorMin, AnchorMax, value, SizeDelta, LocalScale, LocalEulerAngles);

        public RectNodeState WithSizeDelta(Vec2 value)
            => new(AnchoredPosition, AnchorMin, AnchorMax, Pivot, value, LocalScale, LocalEulerAngles);

        public RectNodeState WithLocalScale(Vec3 value)
            => new(AnchoredPosition, AnchorMin, AnchorMax, Pivot, SizeDelta, value, LocalEulerAngles);

        public RectNodeState WithLocalEuler(Vec3 value)
            => new(AnchoredPosition, AnchorMin, AnchorMax, Pivot, SizeDelta, LocalScale, value);
    }

    /// <summary>
    /// 체인이 딛고 서는 최상위 공간(예: 무대 루트)의 rect.
    ///
    /// 코어의 "월드"는 절대 월드가 아니라 이 공간의 로컬 좌표다.
    /// 호스트가 트리를 화면 어디에 놓든 코어 계산은 영향받지 않는다.
    /// </summary>
    public readonly struct RectSpace
    {
        public readonly Vec2 Size;
        public readonly Vec2 Pivot;

        public RectSpace(Vec2 size, Vec2 pivot)
        {
            Size = size;
            Pivot = pivot;
        }

        // 가운데 pivot의 기준 공간.
        public static RectSpace Centered(float width, float height)
            => new(new Vec2(width, height), Vec2.Half);
    }
}