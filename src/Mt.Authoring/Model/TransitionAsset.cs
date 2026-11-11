using Ked.Presentation.Core;

namespace Mt.Authoring.Model;

/// <summary>
/// 전환 — 층들의 계층과 그 타임라인 하나. 미리보기는 이것으로 무대를 세운다(<see cref="Preview.StageTree"/>).
///
/// 층은 단순하다: 부모의 가운데에 앵커·피벗을 둔 사각형(위치 · 크기) + 그림 · 색 + 불투명도를 받는가.
/// 루트(<c>__root</c>)는 적지 않는다 — 무대를 세울 때 붙는다(무대를 꽉 채운다). 부모가 빈 층은 루트 밑이다.
/// </summary>
public sealed class TransitionAsset
{
    /// <summary>이름 — 타임라인의 이름이기도 하다. 영문 소문자로 시작하는 소문자 · 숫자 · _.</summary>
    public string Name { get; set; } = "";

    /// <summary>층들. 부모가 자식보다 먼저 온다. 그리는 순서도 이 순서다(뒤의 것이 위).</summary>
    public List<LayerAsset> Layers { get; set; } = new();

    private TimelineAsset _timeline;

    public TransitionAsset()
    {
        _timeline = new TimelineAsset { Owner = this };
    }

    /// <summary>이 전환의 타임라인 — 전환마다 하나. 놓는 순간 주인이 이 전환이 된다.</summary>
    public TimelineAsset Timeline
    {
        get => _timeline;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _timeline = value;
            value.Owner = this;
        }
    }

    public LayerAsset? Find(string id) => Layers.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.Ordinal));
}

/// <summary>층 — 그림 또는 색 사각형 하나, 또는 묶기만 하는 점. id는 타임라인의 채널이 가리키는 이름이다(채널의 <c>node</c>).</summary>
public sealed class LayerAsset
{
    public string Id { get; set; } = "";

    /// <summary>부모 층의 id. 비면 루트 밑.</summary>
    public string Parent { get; set; } = "";

    /// <summary>부모 가운데에서의 위치(px).</summary>
    public float X { get; set; }

    public float Y { get; set; }

    /// <summary>사각형의 크기(px). 그림 층이면 그림이 이 안에 종횡비로 맞춰진다. 0이면 점(묶는 층).</summary>
    public float Width { get; set; }

    public float Height { get; set; }

    /// <summary>가만히 선 자세의 회전(도, 반시계 +)과 배율 — 배치 모드에서 잡는다. 애니메이션은 이 위에 얹힌다.</summary>
    public float Rotation { get; set; }

    public float ScaleX { get; set; } = 1f;

    public float ScaleY { get; set; } = 1f;

    /// <summary>
    /// 색 — 있으면 **단색 사각형**이다: 그림 없이 이 층의 사각형을 이 색으로 채운다.
    /// "#RRGGBB" 또는 "#RRGGBBAA". null이면 색 층이 아니다. 그림과 함께 켜면 그림 뒤에 색이 깔린다.
    /// </summary>
    public string? Color { get; set; }

    /// <summary>그리는 그림(문서 기준 상대 경로) — 사각형 안에 종횡비로 맞춰진다. null이면 그림 층이 아니다.</summary>
    public string? Image { get; set; }

    /// <summary>불투명도를 받는가(CanvasGroup) — 알파 채널을 놓을 수 있다.</summary>
    public bool Alpha { get; set; }

    public LayerAsset Clone() => (LayerAsset)MemberwiseClone();
}
