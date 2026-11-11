using ScreenTransition.Core;
using ScreenTransition.Authoring.Preview;

namespace ScreenTransition.Authoring.Tests;

/// <summary>무대 트리 — 전환의 층이 무대 루트(1920×1080을 채운다) 밑에 그대로 선다.</summary>
public class StageTreeTests
{
    [Fact]
    public void 전환의_층은_무대_루트_밑에_그_값_그대로_선다()
    {
        RectNodeTree tree = StageTree.Build(TransitionTemplates.Emoji());

        Assert.Equal(StageTree.Stage, tree.GetRectSize(StageTree.RootKey));
        Assert.Equal(StageTree.RootKey, tree.GetParentKey("Emoji_Anchor"));
        Assert.Equal("Emoji_Body", tree.GetParentKey("Emoji_Part3"));
        Assert.Equal(TransitionTemplates.DefaultPartSize, tree.GetRectSize("Emoji_Part1").X, 3);
    }
}
