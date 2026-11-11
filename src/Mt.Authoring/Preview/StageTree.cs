using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Preview;

/// <summary>
/// 무대의 시작 상태 — 세트 하나를 기준 화면(<see cref="Stage"/>) 위에 세운 사각형 계층. 노드 키는 노드 이름 그대로다.
/// 루트(<see cref="RootKey"/>)는 무대를 꽉 채우고, 노드는 부모의 가운데에 앵커 · 피벗을 둔 사각형이다(위치 · 크기 · 회전 · 배율 — 가만히 선 자세).
/// </summary>
public static class StageTree
{
    /// <summary>기준 화면(무대)의 크기 — 세트를 짓는 공간이다.</summary>
    public static readonly Vec2 Stage = new(1920f, 1080f);

    /// <summary>무대를 꽉 채우는 루트 노드의 키. 부모가 빈 노드는 이 밑에 선다.</summary>
    public const string RootKey = "__root";

    public static RectNodeTree Build(RigAsset set)
    {
        ArgumentNullException.ThrowIfNull(set);
        var tree = new RectNodeTree(new RectSpace(Stage, Vec2.Half));
        tree.Add(RootKey, null, RectNodeState.StretchFull);

        foreach (RigNodeAsset node in set.Nodes)
        {
            tree.Add(node.Id, string.IsNullOrEmpty(node.Parent) ? RootKey : node.Parent, StateOf(node));
        }

        return tree;
    }

    /// <summary>노드의 가만히 선 자세 — 가운데 앵커 · 가운데 피벗.</summary>
    public static RectNodeState StateOf(RigNodeAsset node) => new(
        anchoredPosition: new Vec2(node.X, node.Y),
        anchorMin: Vec2.Half,
        anchorMax: Vec2.Half,
        pivot: Vec2.Half,
        sizeDelta: new Vec2(node.Width, node.Height),
        localScale: new Vec3(node.ScaleX, node.ScaleY, 1f),
        localEulerAngles: new Vec3(0f, 0f, node.Rotation));
}
