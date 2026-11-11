using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Tests;

/// <summary>테스트의 발판 세트 — 이모지(자리 › 묶음 › 조각 그림 넷)와 캐릭터(끄덕임·절 픽스처가 움직이는 두 마디).</summary>
public static class RigTemplates
{
    public const string EmojiKind = "emoji";
    public const int DefaultParts = 4;
    public const float DefaultPartSize = 160f;
    public const string CharacterKind = "character";

    /// <summary>nod·bow 픽스처가 움직이는 마디의 사슬(자리 › 좌우 › 상하 › 흔들 축 › 연기 배율) — 끝은 400×600 회색 사각형.</summary>
    public static RigAsset Character() => new()
    {
        Kind = CharacterKind,
        Nodes =
        {
            new RigNodeAsset { Id = "CharacterPortrait_Track_Move" },
            new RigNodeAsset { Id = "CharacterPortrait_Track_Move_X", Parent = "CharacterPortrait_Track_Move" },
            new RigNodeAsset { Id = "CharacterPortrait_Track_Move_Y", Parent = "CharacterPortrait_Track_Move_X" },
            new RigNodeAsset { Id = "CharacterPortrait_SwayPivot", Parent = "CharacterPortrait_Track_Move_Y" },
            new RigNodeAsset { Id = "CharacterPortrait_ActingScale", Parent = "CharacterPortrait_SwayPivot", Width = 400f, Height = 600f, Color = "#808080" },
        },
    };

    /// <summary>그 세트의 오브젝트 하나(<paramref name="id"/>)를 세운 미리보기 장면.</summary>
    public static SceneAsset SceneOf(string rig, string id = "e1") => new() { Objects = { new SceneObject { Id = id, Rig = rig } } };

    /// <summary>발판 세트 둘(캐릭터 · 이모지)로 지은 리그 스키마 — 미리보기·검증이 서는 바탕.</summary>
    public static RigSchemaFile File()
    {
        var document = new MotionDocument();
        document.Rigs.Add(Character());
        document.Rigs.Add(Emoji());
        return RigAssets.Compose(document);
    }

    public static RigAsset Emoji(string kind = EmojiKind, int parts = DefaultParts, float partSize = DefaultPartSize)
    {
        var rig = new RigAsset
        {
            Kind = kind,
            Nodes =
            {
                new RigNodeAsset { Id = "Emoji_Anchor" },
                new RigNodeAsset { Id = "Emoji_Body", Parent = "Emoji_Anchor", Alpha = true },
            },
        };

        for (int i = 1; i <= parts; i++)
        {
            rig.Nodes.Add(new RigNodeAsset { Id = $"Emoji_Part{i}", Parent = "Emoji_Body", Width = partSize, Height = partSize, Image = $"art/part{i}.png", Alpha = true });
        }

        return rig;
    }
}
