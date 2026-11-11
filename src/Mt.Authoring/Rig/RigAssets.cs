using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Rig;

/// <summary>
/// 문서가 정의한 리그를 덤프와 한 벌로(<c>PLAN-parts.md</c> §9), 그리고 런타임이 읽을 파일로.
/// </summary>
public static class RigAssets
{
    /// <summary>
    /// 덤프의 리그 + 문서의 리그. 같은 rigKind면 문서의 것이 이긴다. 문서의 리그가 세울 수 없는 모양이면 예외 —
    /// 호출부(세션)가 덤프만으로 물러나고 사람에게 알린다.
    /// </summary>
    public static RigSchemaFile Compose(RigSchemaFile dump, MotionDocument document)
    {
        ArgumentNullException.ThrowIfNull(dump);
        ArgumentNullException.ThrowIfNull(document);
        Vec2 stage = dump.Dto.capturedUnderParentSize.ToVec2();
        return dump.With(document.Rigs.Select(r => RigAssetCodec.ToDto(r, stage)));
    }
}
