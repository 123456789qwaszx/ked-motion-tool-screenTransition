using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;

namespace Mt.Authoring.Tests;

/// <summary>스킨과 조각(<c>PLAN-parts.md</c> P1) — 리그의 조각마다 붙일 그림의 묶음.</summary>
public class SkinTests
{
    private static readonly RigSchemaFile RigFile = RigTemplates.File();

    private static MotionEditor NewEditor()
    {
        var editor = new MotionEditor(new MotionDocument());
        return editor;
    }

    [Fact]
    public void 조각은_그림으로_표시한_오브젝트이고_캐릭터에는_없다()
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

    // ── 조각의 덧붙임(PLAN-parts.md §13) — 그림 넘기기 · 놓임 ───────────

    [Fact]
    public void 조각에_장을_여럿_주면_장당_시간마다_넘어가고_되풀이하거나_마지막에서_멈춘다()
    {
        MotionEditor editor = NewEditor();
        editor.AddSkin("땀", RigTemplates.EmojiKind);
        long revision = editor.Revision;

        // 장들을 한 번에 — 한 칸. 첫 장이 그 조각의 그림이다. 빈 것은 버린다.
        editor.SetSkinPartImages("땀", "Emoji_Part1", new[] { " a.png ", "b.png", "", "c.png" });
        editor.SetSkinPartImages("땀", "Emoji_Part1", new[] { "a.png", "b.png", "c.png" }); // 같은 값은 무기록
        Assert.Equal(revision + 1, editor.Revision);
        SkinAsset skin = editor.Document.FindSkin("땀")!;
        Assert.Equal("a.png", skin.Parts["Emoji_Part1"]);
        Assert.Equal(new[] { "a.png", "b.png", "c.png" }, skin.ImagesOf("Emoji_Part1"));
        Assert.Equal(new[] { "a.png", "b.png", "c.png" }, skin.AllImages());

        // 기본: 장당 0.2초, 되풀이.
        Assert.Equal("a.png", skin.ImageAt("Emoji_Part1", 0));
        Assert.Equal("a.png", skin.ImageAt("Emoji_Part1", 0.19));
        Assert.Equal("b.png", skin.ImageAt("Emoji_Part1", 0.2));
        Assert.Equal("c.png", skin.ImageAt("Emoji_Part1", 0.45));
        Assert.Equal("a.png", skin.ImageAt("Emoji_Part1", 0.6));
        Assert.Equal("a.png", skin.ImageAt("Emoji_Part1", -1)); // 뜨기 전
        Assert.Null(skin.ImageAt("Emoji_Part2", 0));

        // 장당 시간과 되풀이 — 끄면 마지막 장에서 멈춘다.
        SkinPartSetup setup = skin.SetupOf("Emoji_Part1").Clone();
        setup.FrameSeconds = 0.5f;
        setup.Loop = false;
        editor.SetSkinPartSetup("땀", "Emoji_Part1", setup);
        Assert.Equal("b.png", skin.ImageAt("Emoji_Part1", 0.7));
        Assert.Equal("c.png", skin.ImageAt("Emoji_Part1", 99));

        // 막는 것.
        revision = editor.Revision;
        Assert.Throws<InvalidOperationException>(() => editor.SetSkinPartSetup("땀", "Emoji_Part2", new SkinPartSetup { X = 1f })); // 그림이 없는 조각
        Assert.Throws<InvalidOperationException>(() => editor.SetSkinPartSetup("땀", "Emoji_Part1", new SkinPartSetup { Scale = 0f }));
        Assert.Throws<InvalidOperationException>(() => editor.SetSkinPartSetup("땀", "Emoji_Part1", new SkinPartSetup { FrameSeconds = 0f }));
        Assert.Equal(revision, editor.Revision);

        // 저작본 왕복. 덧붙임이 없는 스킨에는 안 적힌다.
        string json = MotionDocumentJson.Write(editor.Document);
        Assert.Contains("\"setups\"", json, StringComparison.Ordinal);
        Assert.Equal(json, MotionDocumentJson.Write(MotionDocumentJson.Read(json)));
        Assert.Equal("c.png", MotionDocumentJson.Read(json).FindSkin("땀")!.ImageAt("Emoji_Part1", 99));

        // 한 장으로 줄이고 기본값으로 돌리면 덧붙임이 사라진다. 그림을 비우면 덧붙임도 함께.
        editor.SetSkinPartImages("땀", "Emoji_Part1", new[] { "a.png" });
        editor.SetSkinPartSetup("땀", "Emoji_Part1", new SkinPartSetup());
        Assert.Empty(skin.Setups);
        Assert.DoesNotContain("\"setups\"", MotionDocumentJson.Write(editor.Document), StringComparison.Ordinal);
        editor.SetSkinPartSetup("땀", "Emoji_Part1", new SkinPartSetup { X = 30f });
        editor.SetSkinPart("땀", "Emoji_Part1", null);
        Assert.Empty(skin.Setups);
    }
}
