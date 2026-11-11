namespace Mt.Authoring.Model;

/// <summary>
/// 뷰포트의 무대 — 문서에는 없다(보기 전용). 지금은 세트 하나가 서는 자리다: 연 세트 · 고른 연출의 세트 · 고른 애니메이션의 세트.
/// 노드 키는 코어 규약대로 <c>{id}/{nodeId}</c>다.
/// </summary>
public sealed class SceneAsset
{
    public List<SceneObject> Objects { get; set; } = new();

    public SceneObject? Find(string id)
        => Objects.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.Ordinal));
}

/// <summary>무대에 선 세트 하나 — 그 세트(<see cref="Rig"/>)와 입힌 스킨.</summary>
public sealed class SceneObject
{
    public string Id { get; set; } = "";

    /// <summary>세트(rigKind).</summary>
    public string Rig { get; set; } = "";

    /// <summary>입힌 스킨의 이름. null이면 조각을 안 그린다(색 사각형은 그린다).</summary>
    public string? Skin { get; set; }

    /// <summary>노드 키 접두사 — 코어 <c>StageState.NodeKeyOf</c>와 같은 꼴.</summary>
    public string Prefix => Id + "/";

    public string KeyOf(string nodeId) => Prefix + nodeId;
}
