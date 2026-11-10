using Mt.Authoring.Model;

namespace Mt.App.Tests;

/// <summary>테스트의 발판 — 작은 세트 하나(자리 › 묶음 › 조각 그림 넷). 세트의 명령·검증·내보내기를 재는 데 쓴다.</summary>
public static class RigTemplates
{
    public const string EmojiKind = "emoji";
    public const int DefaultParts = 4;
    public const float DefaultPartSize = 160f;

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
            rig.Nodes.Add(new RigNodeAsset { Id = $"Emoji_Part{i}", Parent = "Emoji_Body", Width = partSize, Height = partSize, Image = true, Alpha = true });
        }

        return rig;
    }
}
