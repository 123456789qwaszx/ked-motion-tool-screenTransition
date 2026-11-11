using Mt.Authoring.Editing;
using Mt.Authoring.Model;

namespace Mt.Authoring.Tests;

/// <summary>테스트의 발판 세트 — 이모지(자리 › 묶음 › 조각 그림 넷)와 인형(nod · bow가 움직이는 마디의 사슬). 세트마다 타임라인이 하나다.</summary>
public static class RigTemplates
{
    public const string EmojiKind = "emoji";
    public const int DefaultParts = 4;
    public const float DefaultPartSize = 160f;
    public const string FigureKind = "figure";

    /// <summary>nod · bow가 움직이는 마디의 사슬(자리 › 좌우 › 상하 › 흔들 축 › 판) — 끝은 400×600 회색 사각형.</summary>
    public static RigAsset Figure(string kind = FigureKind) => new()
    {
        Kind = kind,
        Nodes =
        {
            new RigNodeAsset { Id = "Mover" },
            new RigNodeAsset { Id = "MoverX", Parent = "Mover" },
            new RigNodeAsset { Id = "MoverY", Parent = "MoverX" },
            new RigNodeAsset { Id = "Pivot", Parent = "MoverY" },
            new RigNodeAsset { Id = "Panel", Parent = "Pivot", Width = 400f, Height = 600f, Color = "#808080" },
        },
    };

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

    /// <summary>세트 하나(= 타임라인 하나)를 더한다 — <paramref name="layers"/>가 있으면 그 층들로, 없으면 빈 세트. 길이를 준다.</summary>
    public static MotionAsset AddTimeline(this MotionEditor editor, string name, float duration, RigAsset? layers = null)
    {
        RigAsset rig = layers ?? new RigAsset();
        rig.Kind = name;
        editor.AddRig(rig);
        editor.SetDuration(name, duration);
        return editor.Document.Find(name)!;
    }
}
