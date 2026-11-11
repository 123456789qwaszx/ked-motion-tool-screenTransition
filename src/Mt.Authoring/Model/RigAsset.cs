using Ked.Presentation.Core;

namespace Mt.Authoring.Model;

/// <summary>
/// 세트 — 툴에서 정의한 리그(오브젝트들의 계층). Spine처럼 **툴이 원본**이고 미리보기·런타임은 이것으로 세운다.
///
/// 노드는 단순하다: 부모의 가운데에 앵커·피벗을 둔 사각형(위치 · 크기) + 그림 · 색 + 불투명도를 받는가.
/// 루트(<c>__root</c>)는 적지 않는다 — 내보낼 때 붙는다(부모를 꽉 채운다). 부모가 빈 노드는 루트 밑이다.
/// </summary>
public sealed class RigAsset
{
    /// <summary>rigKind — 모션이 이 이름으로 가리킨다. 영문자로 시작하는 영문·숫자·_.</summary>
    public string Kind { get; set; } = "";

    /// <summary>부모가 자식보다 먼저 온다. 그리는 순서도 이 순서다(뒤의 것이 위).</summary>
    public List<RigNodeAsset> Nodes { get; set; } = new();

    private MotionAsset _timeline;

    public RigAsset()
    {
        _timeline = new MotionAsset { Owner = this };
    }

    /// <summary>이 세트의 타임라인 — 세트마다 하나. 놓는 순간 주인이 이 세트가 된다.</summary>
    public MotionAsset Timeline
    {
        get => _timeline;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _timeline = value;
            value.Owner = this;
        }
    }

    public RigNodeAsset? Find(string id) => Nodes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.Ordinal));
}

public sealed class RigNodeAsset
{
    public string Id { get; set; } = "";

    /// <summary>부모 노드의 id. 비면 루트 밑.</summary>
    public string Parent { get; set; } = "";

    /// <summary>부모 가운데에서의 위치(px).</summary>
    public float X { get; set; }

    public float Y { get; set; }

    /// <summary>사각형의 크기(px). 그림 노드면 그림이 이 안에 종횡비로 맞춰진다. 0이면 점(묶는 노드).</summary>
    public float Width { get; set; }

    public float Height { get; set; }

    /// <summary>가만히 선 자세의 회전(도, 반시계 +)과 배율 — 배치 모드에서 잡는다. 애니메이션은 이 위에 얹힌다.</summary>
    public float Rotation { get; set; }

    public float ScaleX { get; set; } = 1f;

    public float ScaleY { get; set; } = 1f;

    /// <summary>
    /// 색 — 있으면 **단색 사각형**이다: 그림 없이 이 노드의 사각형을 이 색으로 채운다.
    /// "#RRGGBB" 또는 "#RRGGBBAA". null이면 색 오브젝트가 아니다. 그림과 함께 켜면 그림 뒤에 색이 깔린다.
    /// </summary>
    public string? Color { get; set; }

    /// <summary>그리는 그림(문서 기준 상대 경로) — 사각형 안에 종횡비로 맞춰진다. null이면 그림 노드가 아니다.</summary>
    public string? Image { get; set; }

    /// <summary>불투명도를 받는가(CanvasGroup) — 알파 채널을 놓을 수 있다.</summary>
    public bool Alpha { get; set; }

    public RigNodeAsset Clone() => (RigNodeAsset)MemberwiseClone();
}

/// <summary>툴의 리그 ↔ 코어의 리그 스키마. 해석(트리 세우기)은 코어가 한다.</summary>
public static class RigAssetCodec
{
    public const string RootId = RigSchemaLoader.RootKey;

    /// <summary>
    /// 코어 스키마로 — 루트를 붙이고, 노드는 가운데 앵커·가운데 피벗으로.
    /// </summary>
    public static RigSchemaRigDto ToDto(RigAsset rig, Vec2 stageSize)
    {
        ArgumentNullException.ThrowIfNull(rig);
        var nodes = new List<RigSchemaNodeDto>
        {
            new()
            {
                id = RootId,
                parent = "",
                anchoredPosition = F2(0f, 0f),
                anchorMin = F2(0f, 0f),
                anchorMax = F2(1f, 1f),
                pivot = F2(0.5f, 0.5f),
                sizeDelta = F2(0f, 0f),
                localScale = F3(1f, 1f, 1f),
                localEulerAngles = F3(0f, 0f, 0f),
                measuredRectSize = F2(stageSize.X, stageSize.Y),
                canvasGroupAlpha = 1f,
            },
        };

        foreach (RigNodeAsset node in rig.Nodes)
        {
            nodes.Add(new RigSchemaNodeDto
            {
                id = node.Id,
                parent = string.IsNullOrEmpty(node.Parent) ? RootId : node.Parent,
                anchoredPosition = F2(node.X, node.Y),
                anchorMin = F2(0.5f, 0.5f),
                anchorMax = F2(0.5f, 0.5f),
                pivot = F2(0.5f, 0.5f),
                sizeDelta = F2(node.Width, node.Height),
                localScale = F3(node.ScaleX, node.ScaleY, 1f),
                localEulerAngles = F3(0f, 0f, node.Rotation),
                measuredRectSize = F2(node.Width, node.Height),
                hasCanvasGroup = node.Alpha,
                canvasGroupAlpha = 1f,
                hasImage = node.Image is not null,
                fillColor = node.Color,
            });
        }

        return new RigSchemaRigDto { rigKind = rig.Kind, sourcePrefab = "", nodes = nodes };
    }

    private static Float2Dto F2(float x, float y) => new() { x = x, y = y };

    private static Float3Dto F3(float x, float y, float z) => new() { x = x, y = y, z = z };
}
