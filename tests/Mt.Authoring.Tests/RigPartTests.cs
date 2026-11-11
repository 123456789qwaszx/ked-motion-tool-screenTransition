using Ked.Presentation.Core;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Tests;

/// <summary>조각 — 그림을 든 층. 무대에 서고, 모션의 채널이 된다.</summary>
public class RigPartTests
{
    private static readonly RigSchemaFile RigFile = RigTemplates.File();

    [Fact]
    public void 조각은_그림을_든_오브젝트이고_캐릭터에는_없다()
    {
        Assert.Equal(new[] { "Emoji_Part1", "Emoji_Part2", "Emoji_Part3", "Emoji_Part4" }, RigParts.Of(RigFile, RigTemplates.EmojiKind));
        Assert.Empty(RigParts.Of(RigFile, "character"));
        Assert.Empty(RigParts.Of(RigFile, "nope"));
        Assert.Empty(RigParts.Of(null, RigTemplates.EmojiKind));

        // 조각 노드는 세트의 보통 노드다 — 무대에 서고, 모션의 채널이 된다.
        RectNodeTree tree = ScenePreview.Build(RigFile, RigTemplates.SceneOf(RigTemplates.EmojiKind));
        Assert.True(tree.Contains("e1/Emoji_Part3"));
        Assert.Equal(RigTemplates.DefaultPartSize, tree.GetRectSize("e1/Emoji_Part1").X, 3);
        Assert.Contains("Emoji_Part1", RigFile.NodeIds[RigTemplates.EmojiKind]);
    }
}
