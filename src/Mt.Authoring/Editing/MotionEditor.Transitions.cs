using System.Text.RegularExpressions;
using Mt.Authoring.Model;

namespace Mt.Authoring.Editing;

// 툴에서 정의하는 리그의 명령 — 각각 되돌리기 한 칸.
public sealed partial class MotionEditor
{
    /// <summary>새 그림 오브젝트의 처음 크기(px) — 그림을 주면 그림의 크기로 바뀐다.</summary>
    public const float DefaultImageSize = 160f;

    /// <summary>세트 이름 = 그 타임라인의 이름 — 코어의 이름 규칙(<c>[a-z0-9_]+</c>)을 지키고 영문자로 시작한다.</summary>
    private static readonly Regex TransitionNameRule = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>리그를 문서에 더한다. 노드는 사본으로 들어간다. id가 겹치거나 부모가 앞에 없으면 막는다(세울 수 없는 리그다).</summary>
    public TransitionAsset AddTransition(TransitionAsset rig)
    {
        ArgumentNullException.ThrowIfNull(rig);

        if (!TransitionNameRule.IsMatch(rig.Name ?? ""))
        {
            throw new InvalidOperationException($"세트 이름은 영문 소문자로 시작하는 소문자·숫자·_ 다 ('{rig.Name}')");
        }

        if (Document.FindTransition(rig.Name) is not null)
        {
            throw new InvalidOperationException($"리그 '{rig.Name}'가 이미 있다.");
        }

        TransitionAsset copy = CloneTransition(rig);
        RequireBuildable(copy);
        Mutate(MotionChangeKind.Document, () => Document.Transitions.Add(copy));
        return copy;
    }

    /// <summary>세트를 지운다 — 그 타임라인도 함께.</summary>
    public void RemoveTransition(string kind)
    {
        TransitionAsset rig = RequireTransition(kind);
        Mutate(MotionChangeKind.Document, () => Document.Transitions.Remove(rig));
    }

    // ── 리그 편집기 — 노드를 하나씩 짓고 옮긴다 ────────

    /// <summary>
    /// 노드를 더한다 — <paramref name="parent"/>(비면 루트) 밑의 맨 뒤(그 부모의 식구들 뒤 = 가장 위에 그려진다). id는 그 리그에서 하나뿐이어야 한다.
    /// </summary>
    public LayerAsset AddLayer(string kind, string id, string? parent = null)
    {
        TransitionAsset rig = RequireTransition(kind);
        string name = RequireNodeId(rig, id);
        string under = parent ?? "";

        if (under.Length > 0 && rig.Find(under) is null)
        {
            throw new KeyNotFoundException($"부모 노드 '{under}'가 리그 '{kind}'에 없다.");
        }

        var node = new LayerAsset { Id = name, Parent = under };
        int at = under.Length == 0 ? rig.Layers.Count : SubtreeEnd(rig, rig.Layers.FindIndex(n => n.Id == under));
        Mutate(MotionChangeKind.Document, () => rig.Layers.Insert(at, node));
        return node;
    }

    /// <summary>노드를 지운다 — 그 밑의 노드도 함께. 그 노드를 쓰던 채널은 남고 검증이 알린다(되살리면 다시 맞는다).</summary>
    public void RemoveLayer(string kind, string id)
    {
        TransitionAsset rig = RequireTransition(kind);
        int index = RequireNodeIndex(rig, id);
        int end = SubtreeEnd(rig, index);
        Mutate(MotionChangeKind.Document, () => rig.Layers.RemoveRange(index, end - index));
    }

    /// <summary>
    /// 노드의 이름을 바꾼다 — 그 이름을 가리키던 것이 같은 칸에서 따라온다: 자식의 부모, 그 리그의 모션 채널.
    /// </summary>
    public void RenameLayer(string kind, string id, string newId)
    {
        TransitionAsset rig = RequireTransition(kind);
        LayerAsset node = rig.Layers[RequireNodeIndex(rig, id)];

        if (string.Equals(id, newId?.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        string name = RequireNodeId(rig, newId);

        // 그 리그의 모션에 새 이름의 채널이 이미 있으면(지운 노드의 흔적) 합칠 수 없다.
        if (Document.Timelines.Any(m => m.Name == kind && m.Channels.Any(c => c.Node == name)))
        {
            throw new InvalidOperationException($"리그 '{kind}'의 모션에 노드 '{name}'의 채널이 이미 있다 — 먼저 그 채널을 지운다.");
        }

        Mutate(MotionChangeKind.Document, () =>
        {
            node.Id = name;

            foreach (LayerAsset child in rig.Layers.Where(n => n.Parent == id))
            {
                child.Parent = name;
            }

            foreach (ChannelAsset channel in Document.Timelines.Where(m => m.Name == kind).SelectMany(m => m.Channels).Where(c => c.Node == id))
            {
                channel.Node = name;
            }
        });
    }

    /// <summary>노드의 값(위치 · 크기 · 그림 · 색 · 불투명도를 받는가)을 고친다. 빈 그림 경로는 그림 없음이다. id와 부모는 따로(<see cref="RenameLayer"/> · <see cref="SetLayerParent"/>).</summary>
    public void UpdateLayer(string kind, string id, LayerAsset value)
    {
        ArgumentNullException.ThrowIfNull(value);
        TransitionAsset rig = RequireTransition(kind);
        LayerAsset node = rig.Layers[RequireNodeIndex(rig, id)];

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
    public void SetLayerParent(string kind, string id, string? parent)
    {
        TransitionAsset rig = RequireTransition(kind);
        int index = RequireNodeIndex(rig, id);
        string under = parent ?? "";

        if (rig.Layers[index].Parent == under)
        {
            return;
        }

        int end = SubtreeEnd(rig, index);
        List<LayerAsset> subtree = rig.Layers.GetRange(index, end - index);

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
            rig.Layers.RemoveRange(index, subtree.Count);
            subtree[0].Parent = under;
            int at = under.Length == 0 ? rig.Layers.Count : SubtreeEnd(rig, rig.Layers.FindIndex(n => n.Id == under));
            rig.Layers.InsertRange(at, subtree);
        });
    }

    /// <summary>형제 사이에서 순서를 옮긴다(−1 = 앞으로 = 아래에 그려진다, +1 = 뒤로 = 위에) — 밑의 노드도 함께. 끝이면 아무 일도 없다.</summary>
    public void MoveLayer(string kind, string id, int direction)
    {
        TransitionAsset rig = RequireTransition(kind);
        int index = RequireNodeIndex(rig, id);
        string parent = rig.Layers[index].Parent;
        List<int> siblings = Enumerable.Range(0, rig.Layers.Count).Where(i => rig.Layers[i].Parent == parent).ToList();
        int place = siblings.IndexOf(index) + Math.Sign(direction);

        if (direction == 0 || place < 0 || place >= siblings.Count)
        {
            return;
        }

        // 앞쪽 식구와 뒤쪽 식구(각각 밑의 노드까지)를 맞바꾼다 — 형제는 이어져 있다.
        int first = Math.Min(index, siblings[place]);
        int second = Math.Max(index, siblings[place]);
        int secondEnd = SubtreeEnd(rig, second);
        List<LayerAsset> front = rig.Layers.GetRange(first, second - first);
        List<LayerAsset> back = rig.Layers.GetRange(second, secondEnd - second);

        Mutate(MotionChangeKind.Document, () =>
        {
            rig.Layers.RemoveRange(first, secondEnd - first);
            rig.Layers.InsertRange(first, back.Concat(front));
        });
    }

    /// <summary>그 노드의 식구(밑의 노드 전부)가 끝나는 자리 — 부모가 자식보다 먼저 오므로 식구는 이어져 있다.</summary>
    private static int SubtreeEnd(TransitionAsset rig, int index)
    {
        var family = new HashSet<string>(StringComparer.Ordinal) { rig.Layers[index].Id };
        int end = index + 1;

        while (end < rig.Layers.Count && family.Contains(rig.Layers[end].Parent))
        {
            family.Add(rig.Layers[end].Id);
            end++;
        }

        return end;
    }

    private static int RequireNodeIndex(TransitionAsset rig, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        int index = rig.Layers.FindIndex(n => string.Equals(n.Id, id, StringComparison.Ordinal));
        return index >= 0 ? index : throw new KeyNotFoundException($"노드 '{id}'가 리그 '{rig.Name}'에 없다.");
    }

    private static readonly Regex NodeIdRule = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private static string RequireNodeId(TransitionAsset rig, string? id)
    {
        string name = (id ?? "").Trim();

        if (!NodeIdRule.IsMatch(name))
        {
            throw new InvalidOperationException($"노드 이름은 영문자로 시작하는 영문·숫자·_ 다 ('{id}')");
        }

        if (rig.Find(name) is not null)
        {
            throw new InvalidOperationException($"노드 '{name}'가 리그 '{rig.Name}'에 이미 있다.");
        }

        return name;
    }

    private static void RequireBuildable(TransitionAsset rig)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { Preview.StageTree.RootKey };

        foreach (LayerAsset node in rig.Layers)
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
    private static TransitionAsset CloneTransition(TransitionAsset rig)
    {
        TransitionAsset copy = Serialization.MotionDocumentJson.Read(Serialization.MotionDocumentJson.Write(new MotionDocument { Transitions = { rig } })).Transitions[0];

        foreach (LayerAsset node in copy.Layers)
        {
            node.Parent ??= "";
            node.Color = NormalizeColor(node.Color);
        }

        return copy;
    }

    private TransitionAsset RequireTransition(string kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        return Document.FindTransition(kind) ?? throw new KeyNotFoundException($"리그 '{kind}'가 문서에 없다.");
    }
}
