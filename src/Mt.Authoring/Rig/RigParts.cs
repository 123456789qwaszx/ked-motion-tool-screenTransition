using Ked.Presentation.Core;

namespace Mt.Authoring.Rig;

/// <summary>리그의 조각 — 그림을 하나씩 그리는 노드들(세트에서 그림을 든 오브젝트).</summary>
public static class RigParts
{
    /// <summary>리그의 조각 노드 id — 세트에 적힌 순서(= 그리는 순서: 뒤의 것이 위).</summary>
    public static IReadOnlyList<string> Of(RigSchemaRigDto rig)
    {
        ArgumentNullException.ThrowIfNull(rig);
        return rig.nodes.Where(n => n.hasImage).Select(n => n.id).ToList();
    }

    /// <summary>그 rigKind의 색 오브젝트 — 노드 id → 색("#RRGGBB[AA]"). 없으면 빈 표.</summary>
    public static IReadOnlyDictionary<string, string> ColorsOf(RigSchemaFile? file, string rigKind)
        => file is not null && file.TryGetRig(rigKind, out RigSchemaRigDto dto)
            ? dto.nodes.Where(n => !string.IsNullOrEmpty(n.fillColor)).ToDictionary(n => n.id, n => n.fillColor, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>그 rigKind의 조각 노드 id. 없는 세트면 빈 목록.</summary>
    public static IReadOnlyList<string> Of(RigSchemaFile? file, string rigKind)
        => file is not null && file.TryGetRig(rigKind, out RigSchemaRigDto dto) ? Of(dto) : Array.Empty<string>();
}
