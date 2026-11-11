using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Rig;

/// <summary>문서의 세트들을 코어의 리그 스키마로 — 미리보기는 이것만으로 선다.</summary>
public static class RigAssets
{
    /// <summary>기준 화면(무대)의 크기 — 세트를 짓는 공간이다.</summary>
    public static readonly Vec2 Stage = new(1920f, 1080f);

    /// <summary>문서의 세트 전부. 세울 수 없는 모양의 세트가 있으면 예외 — 호출부(세션)가 사람에게 알린다.</summary>
    public static RigSchemaFile Compose(MotionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return RigSchemaFile.Of(Stage, document.Rigs.Select(r => RigAssetCodec.ToDto(r, Stage)));
    }
}
