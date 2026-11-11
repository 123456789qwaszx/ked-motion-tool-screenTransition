using Ked.Presentation.Core;

namespace Mt.Authoring.Model;

/// <summary>
/// 저작 원본 한 벌 (<c>*.motion-authoring.json</c>, 스키마 <see cref="AuthoringFormat.AuthoringSchema"/>).
///
/// 제작 정보 — 탄젠트 모드·끊김·재생 기준(relative/absolute)—를 함께 든다. 검증은 <see cref="MotionAsset"/>을 코어 <see cref="MotionClip"/>으로 바꿔 코어 규칙을 지난다.
///
/// 전부 가변 클래스다. 편집은 <see cref="Editing.MotionEditor"/>만 하고, 되돌리기는 문서 전체 스냅샷이다
/// (VnTool <c>ProjectEditor</c>의 방식) — 명령마다 역연산을 짓지 않는다.
/// </summary>
public sealed class MotionDocument
{
    public string Schema { get; set; } = AuthoringFormat.AuthoringSchema;

    public List<MotionAsset> Motions { get; set; } = new();

    /// <summary>
    /// 세트들 — 층(오브젝트)의 사각형 · 그림 · 색. 미리보기는 이것만으로 선다(<see cref="Rig.RigAssets.Compose"/>).
    /// 저작본에는 <see cref="RigsOrNull"/>로 실린다(없으면 안 적는다).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<RigAsset> Rigs { get; set; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("rigs")]
    public List<RigAsset>? RigsOrNull
    {
        get => Rigs.Count == 0 ? null : Rigs;
        set => Rigs = value ?? new List<RigAsset>();
    }

    public RigAsset? FindRig(string? kind)
        => kind is null ? null : Rigs.FirstOrDefault(r => string.Equals(r.Kind, kind, StringComparison.Ordinal));

    public MotionAsset? Find(string name)
        => Motions.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.Ordinal));
}

/// <summary>모션 하나의 저작본. 수치는 코어 <see cref="MotionClip"/>과 같고, 제작 정보만 더 든다.</summary>
public sealed class MotionAsset
{
    public string Name { get; set; } = "";

    /// <summary>이 모션이 움직이는 세트(rigKind).</summary>
    public string Rig { get; set; } = "";

    /// <summary>초.</summary>
    public float Duration { get; set; }

    public MotionEnd End { get; set; } = MotionEnd.Restore;

    /// <summary>
    /// 뷰포트가 키 값을 어떻게 보여 줄지 — relative(시작값 + 키, 배율은 ×) 또는 absolute(키 그대로).
    /// 매니퓰레이터·채널 박스가 포즈를 키 값으로 바꿀 때도 이 렌즈를 쓴다(PoseEdit.KeyValueFor).
    /// </summary>
    public MotionChannelMode PreviewMode { get; set; } = MotionChannelMode.Relative;

    /// <summary>motion-authoring/1의 <c>defaultMode</c> — 읽을 때 <see cref="PreviewMode"/>로 옮기고 비운다. 쓰지 않는다.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("defaultMode")]
    public MotionChannelMode? LegacyDefaultMode { get; set; }

    public List<ChannelAsset> Channels { get; set; } = new();

    /// <summary>
    /// 이벤트 키 — 재생이 그 시각을 지나는 순간 런타임이 이름과 값으로 알린다(예: <c>covered</c> — 화면이 다 덮였다).
    /// 시각 오름차순을 편집기가 지킨다. 저작본에는 <see cref="EventsOrNull"/>로 실린다(없으면 안 적는다).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<MotionEventAsset> Events { get; set; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("events")]
    public List<MotionEventAsset>? EventsOrNull
    {
        get => Events.Count == 0 ? null : Events;
        set => Events = value ?? new List<MotionEventAsset>();
    }

    public ChannelAsset? FindChannel(string node, MotionChannelKind kind)
        => Channels.FirstOrDefault(c => string.Equals(c.Node, node, StringComparison.Ordinal) && c.Kind == kind);
}

/// <summary>이벤트 키 하나 — 시각(초) · 이름(<c>[a-z0-9_]+</c>) · 딸린 값(자유 글, 없어도 된다).</summary>
public sealed class MotionEventAsset
{
    public float T { get; set; }

    public string Name { get; set; } = "";

    public string? Value { get; set; }
}

/// <summary>노드 하나의 속성 하나. 성분이 null이면 "건드리지 않는다"(코어와 같은 뜻).</summary>
public sealed class ChannelAsset
{
    public string Node { get; set; } = "";
    public MotionChannelKind Kind { get; set; }

    public ComponentCurve? X { get; set; }
    public ComponentCurve? Y { get; set; }
    public ComponentCurve? Z { get; set; }

    public ComponentCurve? Get(Axis axis) => axis switch
    {
        Axis.X => X,
        Axis.Y => Y,
        Axis.Z => Z,
        _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "모르는 축"),
    };

    public void Set(Axis axis, ComponentCurve? curve)
    {
        switch (axis)
        {
            case Axis.X: X = curve; break;
            case Axis.Y: Y = curve; break;
            case Axis.Z: Z = curve; break;
            default: throw new ArgumentOutOfRangeException(nameof(axis), axis, "모르는 축");
        }
    }
}

public enum Axis
{
    X,
    Y,
    Z,
}

/// <summary>성분 하나의 키 목록. 시각 오름차순을 편집기가 지킨다.</summary>
public sealed class ComponentCurve
{
    public List<KeyAsset> Keys { get; set; } = new();
}

/// <summary>
/// 키 하나. 수치 넷은 코어 <see cref="CurveKey"/>와 1:1이고, <see cref="Tangent"/>·<see cref="Broken"/>은
/// 그래프 편집기의 손잡이 상태다 — 산출물에는 탄젠트 **값**만 나간다.
/// </summary>
public sealed class KeyAsset
{
    /// <summary>초.</summary>
    public float T { get; set; }

    public float V { get; set; }
    public float InTangent { get; set; }
    public float OutTangent { get; set; }

    /// <summary>탄젠트를 누가 정하는가. 가져온 키는 <see cref="TangentMode.Free"/>다 — 값을 그대로 지킨다.</summary>
    public TangentMode Tangent { get; set; } = TangentMode.Free;

    /// <summary>in·out 탄젠트가 따로 노는가(마야의 break tangents).</summary>
    public bool Broken { get; set; }

    public CurveKey ToCurveKey() => new(T, V, InTangent, OutTangent);

}

/// <summary>
/// 마야의 탄젠트 종류 중 규약 안에서 가능한 것(가중·계단은 규약이 거부한다). 산수는 <c>TangentMath</c>.
/// 이름이 곧 저작 원본의 글자(소문자)다 — 바꾸면 옛 파일을 못 읽는다.
/// </summary>
public enum TangentMode
{
    /// <summary>이웃 할선, 극값·끝은 평평, 가파른 쪽의 3배 안으로(마야 auto에 가깝다).</summary>
    Auto,

    Flat,

    /// <summary>앞·뒤 키와의 할선(끊김).</summary>
    Linear,

    /// <summary>손으로 둔 값 그대로.</summary>
    Free,

    /// <summary>이웃을 잇는 할선 그대로(Catmull-Rom). 부드럽지만 넘칠 수 있다 — 마야 spline.</summary>
    Spline,

    /// <summary>auto보다 더 눌러 이웃 값을 넘지 않게. 이웃과 같은 값이면 평평 — 마야 plateau.</summary>
    Plateau,
}
