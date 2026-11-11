using Ked.Presentation.Core;

namespace Mt.Authoring.Rig;

/// <summary>
/// 세트들을 코어의 리그 스키마 모양으로 든 것(<see cref="RigAssets.Compose"/>가 짓는다). 노드 이름·계층·앵커·피벗·크기는 전부 여기서 온다.
/// 해석(<see cref="RigSchemaLoader"/>)은 코어가 한다.
/// </summary>
public sealed class RigSchemaFile
{
    private readonly Dictionary<string, ICollection<string>> _nodeIds;
    private readonly Dictionary<string, ICollection<string>> _alphaNodeIds;

    private RigSchemaFile(RigSchemasFileDto dto)
    {
        Dto = dto;
        _nodeIds = new Dictionary<string, ICollection<string>>(StringComparer.Ordinal);
        _alphaNodeIds = new Dictionary<string, ICollection<string>>(StringComparer.Ordinal);

        foreach (RigSchemaRigDto rig in dto.rigs)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);

            foreach (RigSchemaNodeDto node in rig.nodes)
            {
                ids.Add(node.id);
            }

            _nodeIds[rig.rigKind] = ids;
            _alphaNodeIds[rig.rigKind] = new HashSet<string>(rig.nodes.Where(n => n.hasCanvasGroup).Select(n => n.id), StringComparer.Ordinal);
        }
    }

    public RigSchemasFileDto Dto { get; }

    public IEnumerable<string> RigKinds => _nodeIds.Keys;

    /// <summary>rigKind → 노드 id 집합. <c>MotionValidator</c>·<c>MotionRules</c>에 그대로 넘긴다.</summary>
    public IReadOnlyDictionary<string, ICollection<string>> NodeIds => _nodeIds;

    /// <summary>
    /// rigKind → 불투명도를 받을 수 있는 노드(<c>hasCanvasGroup</c>). 알파 채널은 이 노드에만 놓인다 — 호스트가 쓸 CanvasGroup이 있어야 한다.
    /// </summary>
    public IReadOnlyDictionary<string, ICollection<string>> AlphaNodeIds => _alphaNodeIds;

    public bool TryGetRig(string rigKind, out RigSchemaRigDto rig)
    {
        foreach (RigSchemaRigDto candidate in Dto.rigs)
        {
            if (string.Equals(candidate.rigKind, rigKind, StringComparison.Ordinal))
            {
                rig = candidate;
                return true;
            }
        }

        rig = null!;
        return false;
    }

    /// <summary>리그 하나를 코어 트리로 세운다 — 뷰포트와 미리보기의 시작값이 여기서 나온다.</summary>
    public RectNodeTree BuildTree(string rigKind, string keyPrefix = "")
        => RigSchemaLoader.BuildTree(Dto, rigKind, keyPrefix);

    /// <summary>
    /// 무대 크기와 리그들로 짓는다. 손상된 리그면 예외다 — 트리를 한 번 세워 보는 것으로 코어의 필드 검사를 전부 지난다.
    /// </summary>
    public static RigSchemaFile Of(Vec2 stage, IEnumerable<RigSchemaRigDto> rigs)
    {
        ArgumentNullException.ThrowIfNull(rigs);
        var dto = new RigSchemasFileDto
        {
            capturedUnderParentSize = new Float2Dto { x = stage.X, y = stage.Y },
            rigs = rigs.ToList(),
        };

        foreach (RigSchemaRigDto rig in dto.rigs)
        {
            RigSchemaLoader.BuildTree(dto, rig.rigKind);
        }

        return new RigSchemaFile(dto);
    }
}
