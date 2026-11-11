using System.Text.RegularExpressions;
using Mt.Authoring.Model;

namespace Mt.Authoring.Editing;

// 툴에서 정의하는 리그의 명령 — 각각 되돌리기 한 칸.
public sealed partial class MotionEditor
{
    /// <summary>새 그림 오브젝트의 처음 크기(px) — 그림을 주면 그림의 크기로 바뀐다.</summary>
    public const float DefaultImageSize = 160f;

    /// <summary>세트 이름 = 그 타임라인의 이름 — 코어의 이름 규칙(<c>[a-z0-9_]+</c>)을 지키고 영문자로 시작한다.</summary>
    private static readonly Regex RigKindRule = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>리그를 문서에 더한다. 노드는 사본으로 들어간다. id가 겹치거나 부모가 앞에 없으면 막는다(세울 수 없는 리그다).</summary>
    public RigAsset AddRig(RigAsset rig)
    {
        ArgumentNullException.ThrowIfNull(rig);

        if (!RigKindRule.IsMatch(rig.Kind ?? ""))
        {
            throw new InvalidOperationException($"세트 이름은 영문 소문자로 시작하는 소문자·숫자·_ 다 ('{rig.Kind}')");
        }

        if (Document.FindRig(rig.Kind) is not null)
        {
            throw new InvalidOperationException($"리그 '{rig.Kind}'가 이미 있다.");
        }

        RigAsset copy = CloneRig(rig);
        RequireBuildable(copy);
        Mutate(MotionChangeKind.Document, () => Document.Rigs.Add(copy));
        return copy;
    }

    /// <summary>세트를 지운다 — 그 타임라인도 함께.</summary>
    public void RemoveRig(string kind)
    {
        RigAsset rig = RequireRig(kind);
        Mutate(MotionChangeKind.Document, () => Document.Rigs.Remove(rig));
    }

    // ── 리그 편집기 — 노드를 하나씩 짓고 옮긴다 ────────

    /// <summary>
    /// 노드를 더한다 — <paramref name="parent"/>(비면 루트) 밑의 맨 뒤(그 부모의 식구들 뒤 = 가장 위에 그려진다). id는 그 리그에서 하나뿐이어야 한다.
    /// </summary>
    public RigNodeAsset AddRigNode(string kind, string id, string? parent = null)
    {
        RigAsset rig = RequireRig(kind);
        string name = RequireNodeId(rig, id);
        string under = parent ?? "";

        if (under.Length > 0 && rig.Find(under) is null)
        {
            throw new KeyNotFoundException($"부모 노드 '{under}'가 리그 '{kind}'에 없다.");
        }

        var node = new RigNodeAsset { Id = name, Parent = under };
        int at = under.Length == 0 ? rig.Nodes.Count : SubtreeEnd(rig, rig.Nodes.FindIndex(n => n.Id == under));
        Mutate(MotionChangeKind.Document, () => rig.Nodes.Insert(at, node));
        return node;
    }

    /// <summary>노드를 지운다 — 그 밑의 노드도 함께. 그 노드를 쓰던 채널은 남고 검증이 알린다(되살리면 다시 맞는다).</summary>
    public void RemoveRigNode(string kind, string id)
    {
        RigAsset rig = RequireRig(kind);
        int index = RequireNodeIndex(rig, id);
        int end = SubtreeEnd(rig, index);
        Mutate(MotionChangeKind.Document, () => rig.Nodes.RemoveRange(index, end - index));
    }

    /// <summary>
    /// 노드의 이름을 바꾼다 — 그 이름을 가리키던 것이 같은 칸에서 따라온다: 자식의 부모, 그 리그의 모션 채널.
    /// </summary>
    public void RenameRigNode(string kind, string id, string newId)
    {
        RigAsset rig = RequireRig(kind);
        RigNodeAsset node = rig.Nodes[RequireNodeIndex(rig, id)];

        if (string.Equals(id, newId?.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        string name = RequireNodeId(rig, newId);

        // 그 리그의 모션에 새 이름의 채널이 이미 있으면(지운 노드의 흔적) 합칠 수 없다.
        if (Document.Motions.Any(m => m.Rig == kind && m.Channels.Any(c => c.Node == name)))
        {
            throw new InvalidOperationException($"리그 '{kind}'의 모션에 노드 '{name}'의 채널이 이미 있다 — 먼저 그 채널을 지운다.");
        }

        Mutate(MotionChangeKind.Document, () =>
        {
            node.Id = name;

            foreach (RigNodeAsset child in rig.Nodes.Where(n => n.Parent == id))
            {
                child.Parent = name;
            }

            foreach (ChannelAsset channel in Document.Motions.Where(m => m.Rig == kind).SelectMany(m => m.Channels).Where(c => c.Node == id))
            {
                channel.Node = name;
            }
        });
    }

    /// <summary>노드의 값(위치 · 크기 · 그림 · 색 · 불투명도를 받는가)을 고친다. 빈 그림 경로는 그림 없음이다. id와 부모는 따로(<see cref="RenameRigNode"/> · <see cref="SetRigNodeParent"/>).</summary>
    public void UpdateRigNode(string kind, string id, RigNodeAsset value)
    {
        ArgumentNullException.ThrowIfNull(value);
        RigAsset rig = RequireRig(kind);
        RigNodeAsset node = rig.Nodes[RequireNodeIndex(rig, id)];

        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Width) || !float.IsFinite(value.Height) || value.Width < 0f || value.Height < 0f)
        {
            throw new InvalidOperationException("노드의 위치는 수, 크기는 0 이상의 수다.");
        }

        if (!float.IsFinite(value.Rotation) || !float.IsFinite(value.ScaleX) || !float.IsFinite(value.ScaleY) || value.ScaleX == 0f || value.ScaleY == 0f)
        {
            throw new InvalidOperationException("노드의 회전은 수, 배율은 0이 아닌 수다.");
        }

        string? color = NormalizeColor(value.Color);
        string? image = string.IsNullOrWhiteSpace(value.Image) ? null : value.Image.Trim().Replace('\\', '/');

        if (node.X == value.X && node.Y == value.Y && node.Width == value.Width && node.Height == value.Height && node.Image == image && node.Alpha == value.Alpha
            && node.Rotation == value.Rotation && node.ScaleX == value.ScaleX && node.ScaleY == value.ScaleY && node.Color == color)
        {
            return;
        }

        Mutate(MotionChangeKind.Document, () =>
        {
            node.X = value.X;
            node.Y = value.Y;
            node.Width = value.Width;
            node.Height = value.Height;
            node.Image = image;
            node.Alpha = value.Alpha;
            node.Rotation = value.Rotation;
            node.ScaleX = value.ScaleX;
            node.ScaleY = value.ScaleY;
            node.Color = color;
        });
    }

    private static readonly Regex ColorRule = new("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.Compiled);

    /// <summary>색 글을 고른 꼴로 — "#RRGGBB" 또는 "#RRGGBBAA"(대문자). 비면 null(색 오브젝트가 아니다). 꼴이 아니면 막는다.</summary>
    public static string? NormalizeColor(string? color)
    {
        string text = (color ?? "").Trim();

        if (text.Length == 0)
        {
            return null;
        }

        if (!text.StartsWith('#'))
        {
            text = "#" + text;
        }

        return ColorRule.IsMatch(text) ? text.ToUpperInvariant() : throw new InvalidOperationException($"색은 #RRGGBB 또는 #RRGGBBAA다 ('{color}')");
    }

    /// <summary>노드를 다른 부모 밑으로 옮긴다(비면 루트) — 밑의 노드도 함께, 새 부모의 식구들 맨 뒤로. 제 밑으로는 못 옮긴다.</summary>
    public void SetRigNodeParent(string kind, string id, string? parent)
    {
        RigAsset rig = RequireRig(kind);
        int index = RequireNodeIndex(rig, id);
        string under = parent ?? "";

        if (rig.Nodes[index].Parent == under)
        {
            return;
        }

        int end = SubtreeEnd(rig, index);
        List<RigNodeAsset> subtree = rig.Nodes.GetRange(index, end - index);

        if (under.Length > 0 && rig.Find(under) is null)
        {
            throw new KeyNotFoundException($"부모 노드 '{under}'가 리그 '{kind}'에 없다.");
        }

        if (subtree.Any(n => n.Id == under))
        {
            throw new InvalidOperationException($"노드 '{id}'를 제 밑('{under}')으로 옮길 수 없다.");
        }

        Mutate(MotionChangeKind.Document, () =>
        {
            rig.Nodes.RemoveRange(index, subtree.Count);
            subtree[0].Parent = under;
            int at = under.Length == 0 ? rig.Nodes.Count : SubtreeEnd(rig, rig.Nodes.FindIndex(n => n.Id == under));
            rig.Nodes.InsertRange(at, subtree);
        });
    }

    /// <summary>형제 사이에서 순서를 옮긴다(−1 = 앞으로 = 아래에 그려진다, +1 = 뒤로 = 위에) — 밑의 노드도 함께. 끝이면 아무 일도 없다.</summary>
    public void MoveRigNode(string kind, string id, int direction)
    {
        RigAsset rig = RequireRig(kind);
        int index = RequireNodeIndex(rig, id);
        string parent = rig.Nodes[index].Parent;
        List<int> siblings = Enumerable.Range(0, rig.Nodes.Count).Where(i => rig.Nodes[i].Parent == parent).ToList();
        int place = siblings.IndexOf(index) + Math.Sign(direction);

        if (direction == 0 || place < 0 || place >= siblings.Count)
        {
            return;
        }

        // 앞쪽 식구와 뒤쪽 식구(각각 밑의 노드까지)를 맞바꾼다 — 형제는 이어져 있다.
        int first = Math.Min(index, siblings[place]);
        int second = Math.Max(index, siblings[place]);
        int secondEnd = SubtreeEnd(rig, second);
        List<RigNodeAsset> front = rig.Nodes.GetRange(first, second - first);
        List<RigNodeAsset> back = rig.Nodes.GetRange(second, secondEnd - second);

        Mutate(MotionChangeKind.Document, () =>
        {
            rig.Nodes.RemoveRange(first, secondEnd - first);
            rig.Nodes.InsertRange(first, back.Concat(front));
        });
    }

    /// <summary>그 노드의 식구(밑의 노드 전부)가 끝나는 자리 — 부모가 자식보다 먼저 오므로 식구는 이어져 있다.</summary>
    private static int SubtreeEnd(RigAsset rig, int index)
    {
        var family = new HashSet<string>(StringComparer.Ordinal) { rig.Nodes[index].Id };
        int end = index + 1;

        while (end < rig.Nodes.Count && family.Contains(rig.Nodes[end].Parent))
        {
            family.Add(rig.Nodes[end].Id);
            end++;
        }

        return end;
    }

    private static int RequireNodeIndex(RigAsset rig, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        int index = rig.Nodes.FindIndex(n => string.Equals(n.Id, id, StringComparison.Ordinal));
        return index >= 0 ? index : throw new KeyNotFoundException($"노드 '{id}'가 리그 '{rig.Kind}'에 없다.");
    }

    private static readonly Regex NodeIdRule = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private static string RequireNodeId(RigAsset rig, string? id)
    {
        string name = (id ?? "").Trim();

        if (!NodeIdRule.IsMatch(name))
        {
            throw new InvalidOperationException($"노드 이름은 영문자로 시작하는 영문·숫자·_ 다 ('{id}')");
        }

        if (rig.Find(name) is not null)
        {
            throw new InvalidOperationException($"노드 '{name}'가 리그 '{rig.Kind}'에 이미 있다.");
        }

        return name;
    }

    private static void RequireBuildable(RigAsset rig)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { RigAssetCodec.RootId };

        foreach (RigNodeAsset node in rig.Nodes)
        {
            if (string.IsNullOrEmpty(node.Id) || node.Id.Contains('/', StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"노드 id가 비었거나 '/'가 들었다 ('{node.Id}')");
            }

            if (!string.IsNullOrEmpty(node.Parent) && !seen.Contains(node.Parent))
            {
                throw new InvalidOperationException($"노드 '{node.Id}'의 부모 '{node.Parent}'가 앞에 없다 — 부모가 자식보다 먼저 와야 한다.");
            }

            if (!seen.Add(node.Id))
            {
                throw new InvalidOperationException($"노드 '{node.Id}'가 둘이다.");
            }
        }
    }

    /// <summary>세트의 사본 — 타임라인까지(저작본 글자로 한 바퀴 돌린다). 노드의 부모 · 색은 고른 꼴로.</summary>
    private static RigAsset CloneRig(RigAsset rig)
    {
        RigAsset copy = Serialization.MotionDocumentJson.Read(Serialization.MotionDocumentJson.Write(new MotionDocument { Rigs = { rig } })).Rigs[0];

        foreach (RigNodeAsset node in copy.Nodes)
        {
            node.Parent ??= "";
            node.Color = NormalizeColor(node.Color);
        }

        return copy;
    }

    private RigAsset RequireRig(string kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        return Document.FindRig(kind) ?? throw new KeyNotFoundException($"리그 '{kind}'가 문서에 없다.");
    }
}
