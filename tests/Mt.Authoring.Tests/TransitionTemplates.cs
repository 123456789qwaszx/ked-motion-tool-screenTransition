using Mt.Authoring.Editing;
using Mt.Authoring.Model;

namespace Mt.Authoring.Tests;

/// <summary>테스트의 발판 전환 — 이모지(자리 › 묶음 › 조각 그림 넷)와 인형(nod · bow가 움직이는 마디의 사슬). 전환마다 타임라인이 하나다.</summary>
public static class TransitionTemplates
{
    public const string EmojiName = "emoji";
    public const int DefaultParts = 4;
    public const float DefaultPartSize = 160f;
    public const string FigureName = "figure";

    /// <summary>nod · bow가 움직이는 마디의 사슬(자리 › 좌우 › 상하 › 흔들 축 › 판) — 끝은 400×600 회색 사각형.</summary>
    public static TransitionAsset Figure(string kind = FigureName) => new()
    {
        Name = kind,
        Layers =
        {
            new LayerAsset { Id = "Mover" },
            new LayerAsset { Id = "MoverX", Parent = "Mover" },
            new LayerAsset { Id = "MoverY", Parent = "MoverX" },
            new LayerAsset { Id = "Pivot", Parent = "MoverY" },
            new LayerAsset { Id = "Panel", Parent = "Pivot", Width = 400f, Height = 600f, Color = "#808080" },
        },
    };

    public static TransitionAsset Emoji(string kind = EmojiName, int parts = DefaultParts, float partSize = DefaultPartSize)
    {
        var asset = new TransitionAsset
        {
            Name = kind,
            Layers =
            {
                new LayerAsset { Id = "Emoji_Anchor" },
                new LayerAsset { Id = "Emoji_Body", Parent = "Emoji_Anchor", Alpha = true },
            },
        };

        for (int i = 1; i <= parts; i++)
        {
            asset.Layers.Add(new LayerAsset { Id = $"Emoji_Part{i}", Parent = "Emoji_Body", Width = partSize, Height = partSize, Image = $"art/part{i}.png", Alpha = true });
        }

        return asset;
    }

    /// <summary>전환 하나(= 타임라인 하나)를 더한다 — <paramref name="layers"/>가 있으면 그 층들로, 없으면 빈 전환. 길이를 준다.</summary>
    public static TimelineAsset AddTimeline(this MotionEditor editor, string name, float duration, TransitionAsset? layers = null)
    {
        TransitionAsset asset = layers ?? new TransitionAsset();
        asset.Name = name;
        editor.AddTransition(asset);
        editor.SetDuration(name, duration);
        return editor.Document.Find(name)!;
    }
}
