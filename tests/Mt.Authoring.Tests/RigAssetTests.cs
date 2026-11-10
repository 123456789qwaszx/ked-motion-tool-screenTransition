using System.Text.Json.Nodes;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;
using Mt.Cli;

namespace Mt.Authoring.Tests;

/// <summary>툴에서 정의한 리그(<c>PLAN-parts.md</c> §9) — 덤프를 기다리지 않고 이모지 리그를 만든다. Spine처럼 툴이 원본.</summary>
public class RigAssetTests
{
    private static readonly RigSchemaFile Dump = RigSchemaFile.Load(Fixtures.PathOf("rig-schemas.json")); // character · screenEffect뿐

    private static MotionEditor NewEditor()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddRig(RigTemplates.Emoji());
        editor.AddSceneObject(null, "e1", RigTemplates.EmojiKind);
        editor.AttachObject(null, "e1", "s1");
        return editor;
    }

    [Fact]
    public void 문서의_리그는_덤프와_한_벌로_서고_조각과_불투명도_노드가_표시대로_나온다()
    {
        MotionEditor editor = NewEditor();
        Assert.DoesNotContain(RigTemplates.EmojiKind, Dump.RigKinds);
        RigSchemaFile rig = RigAssets.Compose(Dump, editor.Document);
        Assert.Contains(RigTemplates.EmojiKind, rig.RigKinds);
        Assert.Contains("character", rig.RigKinds);
        Assert.Equal(Dump.Sha256, rig.Sha256); // 문서가 기억하는 것은 덤프의 해시다
        Assert.Same(Dump, RigAssets.Compose(Dump, new MotionDocument())); // 더할 것이 없으면 그대로

        Assert.Equal(new[] { "Emoji_Part1", "Emoji_Part2", "Emoji_Part3", "Emoji_Part4" }, RigParts.Of(rig, RigTemplates.EmojiKind));
        Assert.Equal(new[] { "Emoji_Body", "Emoji_Part1", "Emoji_Part2", "Emoji_Part3", "Emoji_Part4" }, rig.AlphaNodeIds[RigTemplates.EmojiKind].OrderBy(n => n, StringComparer.Ordinal));
        Assert.Contains("Emoji_Anchor", rig.NodeIds[RigTemplates.EmojiKind]);

        // 씬에 서고(캐릭터의 슬롯 끝에 붙어), 조각은 정의한 크기다.
        RectNodeTree tree = ScenePreview.Build(rig, editor.Document.ActiveSceneAsset!);
        Assert.Equal("s1/CharSlot_Scale", tree.GetParentKey("e1/__root"));
        Assert.Equal("e1/Emoji_Body", tree.GetParentKey("e1/Emoji_Part2"));
        Assert.Equal(RigTemplates.DefaultPartSize, tree.GetRectSize("e1/Emoji_Part1").X, 3);

        // 조각은 이름이 아니라 표시(Image)로 가린다 — 이름 규칙에 안 맞아도 된다.
        editor.AddRig(new RigAsset { Kind = "prop", Nodes = { new RigNodeAsset { Id = "Hand" }, new RigNodeAsset { Id = "Leaf", Parent = "Hand", Width = 80, Height = 40, Image = true } } });
        RigSchemaFile both = RigAssets.Compose(Dump, editor.Document);
        Assert.Equal(new[] { "Leaf" }, RigParts.Of(both, "prop"));
        Assert.Empty(both.AlphaNodeIds["prop"]);

        // 모션·스킨·이모지 검증이 이 리그의 노드로 선다.
        editor.AddMotion("pop", RigTemplates.EmojiKind, 0.2f);
        editor.AddChannel("pop", "Emoji_Body", MotionChannelKind.Alpha);
        editor.SetKeys("pop", "Emoji_Body", MotionChannelKind.Alpha, Axis.X, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(0.2f, 1f) });
        Assert.Empty(MotionValidator.Validate(editor.Document, both.NodeIds, null, both.AlphaNodeIds));
        Assert.NotEmpty(MotionValidator.Validate(editor.Document, Dump.NodeIds)); // 덤프만으로는 모르는 리그다
    }

    [Fact]
    public void 같은_이름이면_문서의_리그가_덤프의_것을_이긴다()
    {
        // 런타임이 나중에 이모지 리그를 덤프에 넣어도, 문서가 정의한 것이 쓰인다(툴이 원본).
        RigSchemaFile dumpWithEmoji = RigSchemaFile.Load(EmojiRigFixture.WriteDump(Fixtures.PathOf("rig-schemas.json")));
        var editor = new MotionEditor(new MotionDocument());
        editor.AddRig(RigTemplates.Emoji(parts: 2, partSize: 64f));
        RigSchemaFile rig = RigAssets.Compose(dumpWithEmoji, editor.Document);
        Assert.Equal(new[] { "Emoji_Part1", "Emoji_Part2" }, RigParts.Of(rig, RigTemplates.EmojiKind));
        Assert.Single(rig.Dto.rigs, r => r.rigKind == RigTemplates.EmojiKind);
    }

    [Fact]
    public void 편집기는_세울_수_있는_리그만_받고_조각을_다시_세운다()
    {
        MotionEditor editor = NewEditor();
        long revision = editor.Revision;

        Assert.Throws<InvalidOperationException>(() => editor.AddRig(RigTemplates.Emoji()));                       // 이미 있다
        Assert.Throws<InvalidOperationException>(() => editor.AddRig(RigTemplates.Emoji("character")));            // 덤프가 원본인 리그
        Assert.Throws<InvalidOperationException>(() => editor.AddRig(RigTemplates.Emoji("camera")));
        Assert.Throws<InvalidOperationException>(() => editor.AddRig(RigTemplates.Emoji("1bad")));
        Assert.Throws<InvalidOperationException>(() => editor.AddRig(new RigAsset { Kind = "x", Nodes = { new RigNodeAsset { Id = "A", Parent = "B" }, new RigNodeAsset { Id = "B" } } })); // 부모가 뒤에
        Assert.Throws<InvalidOperationException>(() => editor.AddRig(new RigAsset { Kind = "x", Nodes = { new RigNodeAsset { Id = "A" }, new RigNodeAsset { Id = "A" } } }));
        Assert.Throws<InvalidOperationException>(() => editor.AddRig(new RigAsset { Kind = "x", Nodes = { new RigNodeAsset { Id = "a/b" } } }));
        Assert.Throws<InvalidOperationException>(() => editor.SetRigParts(RigTemplates.EmojiKind, 0, 100f));
        Assert.Throws<InvalidOperationException>(() => editor.SetRigParts(RigTemplates.EmojiKind, 3, 0f));
        Assert.Throws<KeyNotFoundException>(() => editor.SetRigParts("character", 3, 100f)); // 덤프의 리그는 못 고친다
        Assert.Equal(revision, editor.Revision);

        // 조각 여섯, 크기 96 — 한 칸. 다른 노드는 그대로다.
        editor.SetRigParts(RigTemplates.EmojiKind, 6, 96f);
        Assert.Equal(revision + 1, editor.Revision);
        RigAsset rig = editor.Document.FindRig(RigTemplates.EmojiKind)!;
        Assert.Equal(new[] { "Emoji_Anchor", "Emoji_Body", "Emoji_Part1", "Emoji_Part2", "Emoji_Part3", "Emoji_Part4", "Emoji_Part5", "Emoji_Part6" }, rig.Nodes.Select(n => n.Id));
        Assert.All(rig.Nodes.Where(n => n.Image), n => Assert.Equal((96f, "Emoji_Body", true), (n.Width, n.Parent, n.Alpha)));
        editor.Undo();
        Assert.Equal(4, editor.Document.FindRig(RigTemplates.EmojiKind)!.Nodes.Count(n => n.Image));

        // 받은 객체를 문서에 꽂지 않는다 — 밖에서 고쳐도 문서는 그대로.
        RigAsset given = RigTemplates.Emoji("prop");
        editor.AddRig(given);
        given.Nodes.Clear();
        Assert.Equal(6, editor.Document.FindRig("prop")!.Nodes.Count);

        // 저작본 왕복. 리그가 없는 문서에는 안 적힌다.
        string json = MotionDocumentJson.Write(editor.Document);
        Assert.Equal(json, MotionDocumentJson.Write(MotionDocumentJson.Read(json)));
        Assert.DoesNotContain("\"rigs\"", MotionDocumentJson.Write(new MotionDocument()), StringComparison.Ordinal);

        // 지우면 그 리그의 오브젝트는 남고(검증이 알린다) 씬은 던지지 않고 선다.
        editor.RemoveRig(RigTemplates.EmojiKind);
        var missing = new List<SceneObject>();
        ScenePreview.Build(RigAssets.Compose(Dump, editor.Document), editor.Document.ActiveSceneAsset!, null, missing);
        Assert.Equal("e1", Assert.Single(missing).Id);
    }

    [Fact]
    public void 내보낸_리그는_덤프와_같은_모양이라_같은_로더로_읽힌다()
    {
        MotionEditor editor = NewEditor();
        string json = RigAssets.WriteSchemas(editor.Document, new Vec2(1920f, 1080f));
        Assert.Equal(json, RigAssets.WriteSchemas(editor.Document, new Vec2(1920f, 1080f))); // 결정적

        RigSchemaFile read = RigSchemaFile.Parse(System.Text.Encoding.UTF8.GetBytes(json)); // 캐릭터 덤프를 읽는 그 길
        Assert.Equal(new[] { RigTemplates.EmojiKind }, read.RigKinds);
        Assert.Equal(4, RigParts.Of(read, RigTemplates.EmojiKind).Count);
        Assert.Equal(5, read.AlphaNodeIds[RigTemplates.EmojiKind].Count);
        Assert.Equal(RigTemplates.DefaultPartSize, read.BuildTree(RigTemplates.EmojiKind).GetRectSize("Emoji_Part1").X, 3);

        JsonNode root = JsonNode.Parse(json)!;
        JsonNode part = root["rigs"]![0]!["nodes"]!.AsArray().First(n => n!["id"]!.GetValue<string>() == "Emoji_Part1")!;
        Assert.True(part["hasImage"]!.GetValue<bool>());
        Assert.True(part["hasCanvasGroup"]!.GetValue<bool>());
        Assert.Equal("__root", root["rigs"]![0]!["nodes"]![0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public void 명령줄도_문서의_리그로_검증하고_내보낸다()
    {
        MotionEditor editor = NewEditor();
        editor.AddMotion("pop", RigTemplates.EmojiKind, 0.2f, MotionEnd.Commit, MotionChannelMode.Absolute);
        editor.AddChannel("pop", "Emoji_Body", MotionChannelKind.Position);
        editor.SetKeys("pop", "Emoji_Body", MotionChannelKind.Position, Axis.Y, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(0.2f, 40f) });

        string directory = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"rig-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string document = Path.Combine(directory, "a.motion-authoring.json");
        File.WriteAllText(document, MotionDocumentJson.Write(editor.Document));

        int Run(out JsonNode? json, params string[] args)
        {
            var stdout = new StringWriter();
            int code = MtCli.Run(args, stdout, new StringWriter());
            json = JsonNode.Parse(stdout.ToString());
            return code;
        }

        Assert.Equal(0, Run(out JsonNode? validated, "validate", document)); // 내장 덤프에는 emoji가 없다 — 문서의 리그로 통과한다
        Assert.Empty(validated!["problems"]!.AsArray());
        Assert.Equal(0, Run(out JsonNode? pose, "pose", document, "--motion", "pop", "--time", "0.2"));
        Assert.Equal(40.0, pose!["nodes"]!["Emoji_Body"]!["position"]![1]!.GetValue<double>(), 3);

        string target = Path.Combine(directory, "rigs.json");
        Assert.Equal(0, Run(out JsonNode? exported, "rigs", document, target));
        Assert.Equal(4, exported!["rigs"]![0]!["parts"]!.GetValue<int>());
        Assert.Contains(RigTemplates.EmojiKind, RigSchemaFile.Load(target).RigKinds);
    }

    // ── 리그 편집기(PLAN-parts.md §12) ─────────────────────────────────

    private static string[] Ids(MotionEditor editor, string kind = RigTemplates.EmojiKind) => editor.Document.FindRig(kind)!.Nodes.Select(n => n.Id).ToArray();

    [Fact]
    public void 노드를_짓고_지우고_옮기면_부모가_늘_자식보다_먼저_온다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddRig(RigTemplates.Emoji(parts: 2));
        long revision = editor.Revision;

        // 더하기 — 부모의 식구들 맨 뒤. 그림 노드는 본의 크기와 불투명도를 갖고 선다.
        RigNodeAsset glow = editor.AddRigNode(RigTemplates.EmojiKind, "Emoji_Glow", "Emoji_Anchor", image: true);
        Assert.Equal((160f, 160f, true, true), (glow.Width, glow.Height, glow.Image, glow.Alpha));
        editor.AddRigNode(RigTemplates.EmojiKind, "Emoji_Spark", "Emoji_Glow");
        editor.AddRigNode(RigTemplates.EmojiKind, "Extra"); // 루트 밑
        Assert.Equal(revision + 3, editor.Revision);
        Assert.Equal(new[] { "Emoji_Anchor", "Emoji_Body", "Emoji_Part1", "Emoji_Part2", "Emoji_Glow", "Emoji_Spark", "Extra" }, Ids(editor));

        // 막는 것 — 기록이 안 남는다.
        revision = editor.Revision;
        Assert.Throws<InvalidOperationException>(() => editor.AddRigNode(RigTemplates.EmojiKind, "Emoji_Glow"));       // 이미 있다
        Assert.Throws<InvalidOperationException>(() => editor.AddRigNode(RigTemplates.EmojiKind, "1bad"));
        Assert.Throws<InvalidOperationException>(() => editor.AddRigNode(RigTemplates.EmojiKind, "a/b"));
        Assert.Throws<KeyNotFoundException>(() => editor.AddRigNode(RigTemplates.EmojiKind, "X", "Nope"));
        Assert.Throws<KeyNotFoundException>(() => editor.AddRigNode("character", "X"));                                 // 덤프의 리그는 못 고친다
        Assert.Throws<InvalidOperationException>(() => editor.SetRigNodeParent(RigTemplates.EmojiKind, "Emoji_Glow", "Emoji_Spark")); // 제 밑으로
        Assert.Throws<InvalidOperationException>(() => editor.UpdateRigNode(RigTemplates.EmojiKind, "Extra", new RigNodeAsset { Width = -1f }));
        Assert.Equal(revision, editor.Revision);

        // 형제 사이의 순서 — 식구째 옮긴다. 끝에서는 아무 일도 없다.
        editor.MoveRigNode(RigTemplates.EmojiKind, "Emoji_Glow", -1);
        Assert.Equal(new[] { "Emoji_Anchor", "Emoji_Glow", "Emoji_Spark", "Emoji_Body", "Emoji_Part1", "Emoji_Part2", "Extra" }, Ids(editor));
        editor.MoveRigNode(RigTemplates.EmojiKind, "Emoji_Glow", -1);
        editor.MoveRigNode(RigTemplates.EmojiKind, "Emoji_Part2", +1);
        Assert.Equal(revision + 1, editor.Revision);
        editor.MoveRigNode(RigTemplates.EmojiKind, "Emoji_Anchor", +1);
        Assert.Equal(new[] { "Extra", "Emoji_Anchor", "Emoji_Glow", "Emoji_Spark", "Emoji_Body", "Emoji_Part1", "Emoji_Part2" }, Ids(editor));

        // 부모 바꾸기 — 식구째, 새 부모의 맨 뒤로. 같은 부모면 무기록.
        revision = editor.Revision;
        editor.SetRigNodeParent(RigTemplates.EmojiKind, "Emoji_Glow", "Emoji_Body");
        editor.SetRigNodeParent(RigTemplates.EmojiKind, "Emoji_Glow", "Emoji_Body");
        Assert.Equal(revision + 1, editor.Revision);
        Assert.Equal(new[] { "Extra", "Emoji_Anchor", "Emoji_Body", "Emoji_Part1", "Emoji_Part2", "Emoji_Glow", "Emoji_Spark" }, Ids(editor));
        Assert.Equal("Emoji_Body", editor.Document.FindRig(RigTemplates.EmojiKind)!.Find("Emoji_Glow")!.Parent);
        editor.SetRigNodeParent(RigTemplates.EmojiKind, "Emoji_Body", null); // 루트로
        Assert.Equal(new[] { "Extra", "Emoji_Anchor", "Emoji_Body", "Emoji_Part1", "Emoji_Part2", "Emoji_Glow", "Emoji_Spark" }, Ids(editor));
        Assert.Equal("", editor.Document.FindRig(RigTemplates.EmojiKind)!.Find("Emoji_Body")!.Parent);

        // 값 — 한 칸, 같은 값은 무기록. 세운 나무에 그대로 선다.
        revision = editor.Revision;
        var moved = new RigNodeAsset { X = 30f, Y = -12f, Width = 80f, Height = 40f, Image = true, Alpha = false };
        editor.UpdateRigNode(RigTemplates.EmojiKind, "Emoji_Glow", moved);
        editor.UpdateRigNode(RigTemplates.EmojiKind, "Emoji_Glow", moved);
        Assert.Equal(revision + 1, editor.Revision);
        RigSchemaFile composed = RigAssets.Compose(Dump, editor.Document);
        Assert.True(composed.TryGetRig(RigTemplates.EmojiKind, out RigSchemaRigDto dto));
        RigSchemaNodeDto node = dto.nodes.Single(n => n.id == "Emoji_Glow");
        Assert.Equal((30f, -12f, 80f, 40f, true, false), (node.anchoredPosition.x, node.anchoredPosition.y, node.sizeDelta.x, node.sizeDelta.y, node.hasImage, node.hasCanvasGroup));
        Assert.Contains("Emoji_Glow", RigParts.Of(composed, RigTemplates.EmojiKind));

        // 가만히 선 자세의 회전·배율(배치 모드) — 세운 나무에 그대로 선다. 배율 0은 막는다.
        editor.UpdateRigNode(RigTemplates.EmojiKind, "Emoji_Glow", new RigNodeAsset { X = 30f, Y = -12f, Width = 80f, Height = 40f, Image = true, Rotation = 30f, ScaleX = 2f, ScaleY = -1f });
        Assert.True(RigAssets.Compose(Dump, editor.Document).TryGetRig(RigTemplates.EmojiKind, out RigSchemaRigDto posed));
        RigSchemaNodeDto turned = posed.nodes.Single(n => n.id == "Emoji_Glow");
        Assert.Equal((30f, 2f, -1f), (turned.localEulerAngles.z, turned.localScale.x, turned.localScale.y));
        Assert.Throws<InvalidOperationException>(() => editor.UpdateRigNode(RigTemplates.EmojiKind, "Emoji_Glow", new RigNodeAsset { ScaleX = 0f }));
        editor.Undo();

        // 지우기 — 밑의 노드도 함께. 되돌리면 그 자리에 돌아온다.
        editor.RemoveRigNode(RigTemplates.EmojiKind, "Emoji_Body");
        Assert.Equal(new[] { "Extra", "Emoji_Anchor" }, Ids(editor));
        editor.Undo();
        Assert.Equal(7, Ids(editor).Length);
        RigAssets.Compose(Dump, editor.Document); // 여전히 세울 수 있다
    }

    [Fact]
    public void 노드의_이름을_바꾸면_채널과_스킨과_이모지와_붙는_노드가_따라온다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddRig(RigTemplates.Emoji(parts: 2));
        editor.AddSceneObject(null, "e1", RigTemplates.EmojiKind);
        editor.AddSkin("땀", RigTemplates.EmojiKind);
        editor.SetSkinPart("땀", "Emoji_Part1", "art/sweat.png");
        editor.AddMotion("pop", RigTemplates.EmojiKind, 0.2f);
        editor.AddChannel("pop", "Emoji_Part1", MotionChannelKind.Position);
        editor.AddChannel("pop", "Emoji_Body", MotionChannelKind.Position);
        editor.AddEmoji("sweat", RigTemplates.EmojiKind);

        long revision = editor.Revision;
        editor.RenameRigNode(RigTemplates.EmojiKind, "Emoji_Part1", "Drop");
        editor.RenameRigNode(RigTemplates.EmojiKind, "Emoji_Anchor", "Pin");
        editor.RenameRigNode(RigTemplates.EmojiKind, "Pin", "Pin"); // 같은 이름은 무기록
        Assert.Equal(revision + 2, editor.Revision);

        Assert.Equal(new[] { "Pin", "Emoji_Body", "Drop", "Emoji_Part2" }, Ids(editor));
        Assert.Equal("Pin", editor.Document.FindRig(RigTemplates.EmojiKind)!.Find("Emoji_Body")!.Parent);
        Assert.Equal(new[] { "Drop", "Emoji_Body" }, editor.Document.Find("pop")!.Channels.Select(c => c.Node));
        Assert.Equal("art/sweat.png", editor.Document.FindSkin("땀")!.Parts["Drop"]);
        Assert.False(editor.Document.FindSkin("땀")!.Parts.ContainsKey("Emoji_Part1"));
        Assert.Equal("Pin", editor.Document.FindEmoji("sweat")!.AnchorNode);

        // 이름이 겹치거나 규칙에 안 맞으면 막는다. 되돌리면 전부 함께 돌아온다.
        Assert.Throws<InvalidOperationException>(() => editor.RenameRigNode(RigTemplates.EmojiKind, "Drop", "Emoji_Body"));
        Assert.Throws<InvalidOperationException>(() => editor.RenameRigNode(RigTemplates.EmojiKind, "Drop", "물방울"));
        editor.Undo();
        editor.Undo();
        Assert.Equal("Emoji_Part1", editor.Document.Find("pop")!.Channels[0].Node);
        Assert.Equal("Emoji_Anchor", editor.Document.FindEmoji("sweat")!.AnchorNode);
        Assert.True(editor.Document.FindSkin("땀")!.Parts.ContainsKey("Emoji_Part1"));
    }

    [Fact]
    public void 색_오브젝트는_덤프와_같은_모양에_fillColor로_나가고_색_글은_고른_꼴로_선다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddRig(new RigAsset { Kind = "wipe", Nodes = { new RigNodeAsset { Id = "Body", Alpha = true } } });
        editor.AddRigNode("wipe", "Panel", "Body");
        long revision = editor.Revision;

        // 색을 주면 색 오브젝트 — 한 칸. "#" 없이, 소문자로 적어도 고른 꼴로 선다. 같은 값은 무기록.
        editor.UpdateRigNode("wipe", "Panel", new RigNodeAsset { Width = 1920f, Height = 400f, Color = "3d5a80", Alpha = true });
        editor.UpdateRigNode("wipe", "Panel", new RigNodeAsset { Width = 1920f, Height = 400f, Color = "#3D5A80", Alpha = true });
        Assert.Equal(revision + 1, editor.Revision);
        Assert.Equal("#3D5A80", editor.Document.FindRig("wipe")!.Find("Panel")!.Color);
        Assert.Throws<InvalidOperationException>(() => editor.UpdateRigNode("wipe", "Panel", new RigNodeAsset { Color = "blue" }));
        Assert.Equal("#11223344", MotionEditor.NormalizeColor(" #11223344 ")); // 불투명도까지
        Assert.Null(MotionEditor.NormalizeColor("  "));

        // 세운 리그: 색 표에 그 노드가 서고, 그림 노드는 아니다(조각이 아니다).
        RigSchemaFile composed = RigAssets.Compose(Dump, editor.Document);
        Assert.Equal("#3D5A80", RigParts.ColorsOf(composed, "wipe")["Panel"]);
        Assert.Empty(RigParts.Of(composed, "wipe"));
        Assert.Empty(RigParts.ColorsOf(composed, "character"));

        // 산출물과 저작본 — 색이 없는 노드에는 안 적힌다(저작본).
        Assert.Contains("\"fillColor\": \"#3D5A80\"", RigAssets.WriteSchemas(editor.Document, new Vec2(1920f, 1080f)), StringComparison.Ordinal);
        string json = MotionDocumentJson.Write(editor.Document);
        Assert.Equal(1, json.Split("\"color\"").Length - 1);
        Assert.Equal("#3D5A80", MotionDocumentJson.Read(json).FindRig("wipe")!.Find("Panel")!.Color);

        // 비우면 색 오브젝트가 아니다.
        editor.UpdateRigNode("wipe", "Panel", new RigNodeAsset { Width = 1920f, Height = 400f, Alpha = true });
        Assert.Null(editor.Document.FindRig("wipe")!.Find("Panel")!.Color);
        Assert.Empty(RigParts.ColorsOf(RigAssets.Compose(Dump, editor.Document), "wipe"));
    }

    // ── 시차 — 오브젝트의 움직임을 통째로 민다 ─────────────────────────

    [Fact]
    public void 움직임을_밀면_길이는_그대로이고_비는_쪽은_가만히_넘치는_쪽은_잘린다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddRig(RigTemplates.Emoji(parts: 3));
        editor.AddMotion("in", RigTemplates.EmojiKind, 1f, MotionEnd.Commit);
        KeyAsset Lin(float t, float v) => new() { T = t, V = v, Tangent = TangentMode.Linear };

        foreach (string part in new[] { "Emoji_Part1", "Emoji_Part2", "Emoji_Part3" })
        {
            editor.AddChannel("in", part, MotionChannelKind.Position);
            editor.SetKeys("in", part, MotionChannelKind.Position, Axis.X, new[] { Lin(0f, -100f), Lin(0.2f, -100f), Lin(0.6f, 0f), Lin(1f, 0f) }); // 0.2~0.6초에 들어온다
        }

        (float T, float V)[] Keys(string part) => editor.Document.Find("in")!.FindChannel(part, MotionChannelKind.Position)!.X!.Keys.Select(k => (MathF.Round(k.T, 3), MathF.Round(k.V, 1))).ToArray();
        long revision = editor.Revision;

        // 0.1초 늦게 — 안쪽 키가 밀리고 처음과 끝은 제 값으로 선다. 한 칸.
        Assert.True(editor.ShiftNodeKeys("in", "Emoji_Part1", 0.1f));
        Assert.Equal(revision + 1, editor.Revision);
        Assert.Equal(new[] { (0f, -100f), (0.3f, -100f), (0.7f, 0f), (1f, 0f) }, Keys("Emoji_Part1"));

        // 넘치게 늦추면 잘린다 — 끝의 키는 잘린 자리의 값(아직 들어오는 중).
        editor.ShiftNodeKeys("in", "Emoji_Part2", 0.6f);
        Assert.Equal(new[] { (0f, -100f), (0.8f, -100f), (1f, -50f) }, Keys("Emoji_Part2"));

        // 일찍 — 처음의 키는 잘린 자리의 값(이미 들어오는 중), 끝은 마지막 값으로 가만히.
        editor.ShiftNodeKeys("in", "Emoji_Part3", -0.4f);
        Assert.Equal(new[] { (0f, -50f), (0.2f, 0f), (1f, 0f) }, Keys("Emoji_Part3"));

        // 어느 쪽이든 규칙(처음과 끝에 키)이 그대로 선다.
        RigSchemaFile rig = RigAssets.Compose(Dump, editor.Document);
        Assert.Empty(MotionValidator.Validate(editor.Document, rig.NodeIds, null, rig.AlphaNodeIds));

        // 채널이 없는 오브젝트·0초는 아무 일도 없다(기록 없음).
        revision = editor.Revision;
        Assert.False(editor.ShiftNodeKeys("in", "Emoji_Body", 0.1f));
        Assert.False(editor.ShiftNodeKeys("in", "Emoji_Part1", 0f));
        Assert.Equal(revision, editor.Revision);

        // 시차 — 순서대로 0.1초씩 더: 첫째는 그대로. 통째로 한 칸.
        editor.Undo();
        editor.Undo();
        editor.Undo();
        revision = editor.Revision;
        Assert.True(editor.StaggerNodes("in", new[] { "Emoji_Part1", "Emoji_Part2", "Emoji_Part3" }, 0.1f));
        Assert.Equal(revision + 1, editor.Revision);
        Assert.Equal(0.2f, Keys("Emoji_Part1")[1].T);
        Assert.Equal(0.3f, Keys("Emoji_Part2")[1].T);
        Assert.Equal(0.4f, Keys("Emoji_Part3")[1].T);
    }

    // ── 제 차례 전에는 숨긴다 ───────────────────────────────────────────

    [Fact]
    public void 제_차례_전까지_숨기면_움직이기_시작하는_시각에_나타나는_불투명도_키가_놓인다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddRig(RigTemplates.Emoji(parts: 3));
        editor.AddMotion("in", RigTemplates.EmojiKind, 1f, MotionEnd.Commit);
        KeyAsset Lin(float t, float v) => new() { T = t, V = v, Tangent = TangentMode.Linear };
        editor.AddChannel("in", "Emoji_Part1", MotionChannelKind.Position); // 0.4초에 움직이기 시작해 0.8초에 선다
        editor.SetKeys("in", "Emoji_Part1", MotionChannelKind.Position, Axis.X, new[] { Lin(0f, -100f), Lin(0.4f, -100f), Lin(0.8f, 0f), Lin(1f, 0f) });
        editor.AddChannel("in", "Emoji_Part2", MotionChannelKind.Position); // 처음부터 끝까지 움직인다
        editor.SetKeys("in", "Emoji_Part2", MotionChannelKind.Position, Axis.X, new[] { Lin(0f, -100f), Lin(1f, 0f) });
        (float T, float V)[] Alpha(string part) => editor.Document.Find("in")!.FindChannel(part, MotionChannelKind.Alpha)!.X!.Keys.Select(k => (MathF.Round(k.T, 3), k.V)).ToArray();

        // 제 차례(0.4초) 전까지 0, 0.1초에 걸쳐 나타난다 — 한 칸.
        long revision = editor.Revision;
        Assert.True(editor.HideUntilTurn("in", "Emoji_Part1"));
        Assert.Equal(revision + 1, editor.Revision);
        Assert.Equal(new[] { (0f, 0f), (0.4f, 0f), (0.5f, 1f), (1f, 1f) }, Alpha("Emoji_Part1"));

        // 움직임을 마치면(0.8초) 숨긴다 — 있던 불투명도 커브를 갈아 치운다.
        Assert.True(editor.HideAfterTurn("in", "Emoji_Part1"));
        Assert.Equal(new[] { (0f, 1f), (0.7f, 1f), (0.8f, 0f), (1f, 0f) }, Alpha("Emoji_Part1"));

        // 처음부터(끝까지) 움직이거나 움직임이 없는 오브젝트는 할 일이 없다(기록 없음).
        revision = editor.Revision;
        Assert.False(editor.HideUntilTurn("in", "Emoji_Part2"));
        Assert.False(editor.HideAfterTurn("in", "Emoji_Part2"));
        Assert.False(editor.HideUntilTurn("in", "Emoji_Part3"));
        Assert.Equal(revision, editor.Revision);

        // 전부 — 움직이는 오브젝트 가운데 늦게 나오는 것만, 한 칸. 규칙에 맞는다.
        editor.Undo();
        editor.Undo();
        revision = editor.Revision;
        Assert.Equal(1, editor.HideAllUntilTurn("in"));
        Assert.Equal(revision + 1, editor.Revision);
        Assert.Null(editor.Document.Find("in")!.FindChannel("Emoji_Part2", MotionChannelKind.Alpha));
        RigSchemaFile rig = RigAssets.Compose(Dump, editor.Document);
        Assert.Empty(MotionValidator.Validate(editor.Document, rig.NodeIds, null, rig.AlphaNodeIds));
    }

    // ── 세트가 세트를 품는다(PLAN-set.md S9) ───────────────────────────

    /// <summary>car(Body › Shell(그림) · Wheel(색)) · sun(Disc(그림)) · city(Ground(색) + car 인스턴스 + sun 인스턴스).</summary>
    private static MotionEditor NestedEditor()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddRig(new RigAsset { Kind = "car", Nodes = { new RigNodeAsset { Id = "Body", Alpha = true } } });
        editor.AddRigNode("car", "Shell", "Body", image: true);
        editor.AddRigNode("car", "Wheel", "Body");
        editor.UpdateRigNode("car", "Wheel", new RigNodeAsset { X = 30f, Y = -40f, Width = 20f, Height = 20f, Color = "#222222", Alpha = true });
        editor.AddSkin("car_red", "car");
        editor.SetSkinPart("car_red", "Shell", "art/car_red.png");
        editor.AddSkin("car_blue", "car");
        editor.SetSkinPart("car_blue", "Shell", "art/car_blue.png");

        editor.AddRig(new RigAsset { Kind = "sun", Nodes = { new RigNodeAsset { Id = "Disc", Width = 100f, Height = 100f, Image = true, Alpha = true } } });
        editor.AddSkin("sun", "sun");
        editor.SetSkinPart("sun", "Disc", "art/sun.png");

        editor.AddRig(new RigAsset { Kind = "city", Nodes = { new RigNodeAsset { Id = "Ground", Width = 1920f, Height = 200f, Color = "#334455", Alpha = true } } });
        return editor;
    }

    [Fact]
    public void 세트를_품으면_그_오브젝트들이_경로_이름으로_펼쳐져_한_덩어리로_선다()
    {
        MotionEditor editor = NestedEditor();
        long revision = editor.Revision;

        // 품는다 — 한 칸. 인스턴스 오브젝트는 통째로 옮기는 손잡이다(자리 · 배율).
        RigNodeAsset car = editor.AddRigInstance("city", "car", "car");
        Assert.Equal(revision + 1, editor.Revision);
        Assert.Equal(("car", true), (car.Set, car.Alpha));
        editor.UpdateRigNode("city", "car", new RigNodeAsset { X = 300f, Y = 50f, ScaleX = 2f, ScaleY = 2f, Alpha = true });
        Assert.Equal("car", editor.Document.FindRig("city")!.Find("car")!.Set); // 값을 고쳐도 품은 것은 그대로다
        editor.AddRigInstance("city", "sun", "sun");

        // 세운 리그: 품은 세트가 인스턴스 밑에 "인스턴스.이름"으로 펼쳐진다. 부모·색·그림 표시가 따라온다.
        RigSchemaFile composed = RigAssets.Compose(Dump, editor.Document);
        Assert.True(composed.TryGetRig("city", out RigSchemaRigDto dto));
        Assert.Equal(new[] { "__root", "Ground", "car", "car.Body", "car.Shell", "car.Wheel", "sun", "sun.Disc" }, dto.nodes.Select(n => n.id));
        Assert.Equal(("car", "car.Body", "car.Body"), (dto.nodes.Single(n => n.id == "car.Body").parent, dto.nodes.Single(n => n.id == "car.Shell").parent, dto.nodes.Single(n => n.id == "car.Wheel").parent));
        Assert.Equal(new[] { "car.Shell", "sun.Disc" }, RigParts.Of(composed, "city"));
        Assert.Equal("#222222", RigParts.ColorsOf(composed, "city")["car.Wheel"]);
        Assert.Equal("car", RigAssetCodec.OwnerOf("car.Wheel"));

        // 선 자리: 인스턴스의 자리·배율이 안쪽에 곱해진다 — 바퀴는 (300 + 30×2, 50 − 40×2).
        var scene = new SceneAsset { Name = "s", Objects = { new SceneObject { Id = "c", Rig = "city" } } };
        RectNodeTree tree = ScenePreview.Build(composed, scene);
        Vec2 root = tree.TransformPoint("c/__root", Vec3.Zero).XY;
        Vec2 wheel = tree.TransformPoint("c/car.Wheel", Vec3.Zero).XY;
        Assert.Equal((360f, -30f), (MathF.Round(wheel.X - root.X, 1), MathF.Round(wheel.Y - root.Y, 1)));

        // 실제로 입는 스킨: 품은 세트의 스킨이 경로 이름으로 — 인스턴스가 고른 것, 없으면 그 세트의 첫 스킨.
        SkinAsset worn = RigAssets.EffectiveSkin(editor.Document, "city", null)!;
        Assert.Equal(("art/car_red.png", "art/sun.png"), (worn.Parts["car.Shell"], worn.Parts["sun.Disc"]));
        editor.SetRigInstanceSkin("city", "car", "car_blue");
        Assert.Equal("art/car_blue.png", RigAssets.EffectiveSkin(editor.Document, "city", null)!.Parts["car.Shell"]);
        Assert.Throws<InvalidOperationException>(() => editor.SetRigInstanceSkin("city", "car", "sun"));     // 다른 세트의 스킨
        Assert.Throws<InvalidOperationException>(() => editor.SetRigInstanceSkin("city", "Ground", "sun"));  // 품은 오브젝트가 아니다
        Assert.Same(editor.Document.FindSkin("sun"), RigAssets.EffectiveSkin(editor.Document, "sun", "sun")); // 품은 것이 없으면 제 스킨 그대로

        // 참조다 — 원본 세트를 고치면 품은 곳이 따라온다.
        editor.AddRigNode("car", "Light", "Body");
        Assert.True(RigAssets.Compose(Dump, editor.Document).TryGetRig("city", out RigSchemaRigDto after));
        Assert.Contains(after.nodes, n => n.id == "car.Light");

        // 품은 것의 애니메이션: 인스턴스를 통째로 움직이는 키도, 안쪽의 것을 경로 이름으로 짚는 키도 놓인다 — 검증을 지난다.
        editor.AddMotion("city_in", "city", 1f, MotionEnd.Commit);
        editor.AddChannel("city_in", "car", MotionChannelKind.Position);
        editor.SetKeys("city_in", "car", MotionChannelKind.Position, Axis.X, new[] { new KeyAsset { T = 0f, V = -500f, Tangent = TangentMode.Linear }, new KeyAsset { T = 1f, V = 0f, Tangent = TangentMode.Linear } });
        editor.AddChannel("city_in", "car.Wheel", MotionChannelKind.Euler);
        editor.SetKeys("city_in", "car.Wheel", MotionChannelKind.Euler, Axis.Z, new[] { new KeyAsset { T = 0f, V = 0f, Tangent = TangentMode.Linear }, new KeyAsset { T = 1f, V = -360f, Tangent = TangentMode.Linear } });
        RigSchemaFile rig = RigAssets.Compose(Dump, editor.Document);
        Assert.Empty(MotionValidator.Validate(editor.Document, rig.NodeIds, null, rig.AlphaNodeIds));

        // 산출물: 펼친 한 덩어리로 나간다(런타임은 품는다는 것을 모른다). 저작본은 참조만 적는다.
        string schemas = RigAssets.WriteSchemas(editor.Document, new Vec2(1920f, 1080f));
        Assert.Contains("\"id\": \"car.Wheel\"", schemas, StringComparison.Ordinal);
        string json = MotionDocumentJson.Write(editor.Document);
        Assert.Contains("\"set\": \"car\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("car.Wheel\",\n          \"parent", json, StringComparison.Ordinal);
        Assert.Equal(json, MotionDocumentJson.Write(MotionDocumentJson.Read(json)));
    }

    [Fact]
    public void 서로_품는_것과_품긴_세트를_지우는_것은_막는다()
    {
        MotionEditor editor = NestedEditor();
        editor.AddRigInstance("city", "car", "car");
        long revision = editor.Revision;

        Assert.Throws<InvalidOperationException>(() => editor.AddRigInstance("city", "self", "city"));  // 자기 자신
        Assert.Throws<InvalidOperationException>(() => editor.AddRigInstance("car", "back", "city"));   // city가 car를 품고 있다
        editor.AddRigInstance("sun", "inner", "car");                                                     // sun → car는 된다
        Assert.Throws<InvalidOperationException>(() => editor.AddRigInstance("car", "far", "sun"));     // 두 다리 건너도 막는다
        Assert.Throws<KeyNotFoundException>(() => editor.AddRigInstance("city", "ghost", "nope"));
        Assert.Throws<InvalidOperationException>(() => editor.RemoveRig("car"));                          // 품긴 세트는 못 지운다
        Assert.Equal(revision + 1, editor.Revision);

        // 품은 것이 또 품은 것 — 경로가 이어진다.
        editor.AddRigInstance("city", "sun", "sun");
        Assert.True(RigAssets.Compose(Dump, editor.Document).TryGetRig("city", out RigSchemaRigDto dto));
        Assert.Contains(dto.nodes, n => n.id == "sun.inner.Wheel");

        // 인스턴스를 빼면 지울 수 있다.
        editor.RemoveRigNode("city", "car");
        editor.RemoveRigNode("sun", "inner");
        editor.RemoveRig("car");
        Assert.Null(editor.Document.FindRig("car"));

        // 손으로 쓴 문서의 순환·없는 세트는 펼칠 때 건너뛴다(던지지 않는다).
        var broken = new MotionDocument();
        broken.Rigs.Add(new RigAsset { Kind = "a", Nodes = { new RigNodeAsset { Id = "x", Set = "b" }, new RigNodeAsset { Id = "y", Set = "ghost" } } });
        broken.Rigs.Add(new RigAsset { Kind = "b", Nodes = { new RigNodeAsset { Id = "z", Set = "a" } } });
        Assert.True(RigAssets.Compose(Dump, broken).TryGetRig("a", out RigSchemaRigDto looped));
        Assert.Equal(new[] { "__root", "x", "x.z", "y" }, looped.nodes.Select(n => n.id));
    }

    // ── 재생 블록 — 품은 세트의 애니메이션을 튼다(PLAN-set.md S10·S11) ───

    private static KeyAsset Lin(float t, float v) => new() { T = t, V = v, Tangent = TangentMode.Linear };

    /// <summary>
    /// <see cref="NestedEditor"/>에 더해: city가 car를 (300, 50)에 품고,
    /// car의 애니메이션 roll(0.4초 유지 · 상대값: Body x 0 → 100) · hop(0.2초 복원 · 상대값: Body y 0 → 30 → 0, 0.1초에 이벤트 thud) · spin(0.5초 유지 · 절대값: Wheel 회전 0 → 90),
    /// city의 애니메이션 city_in(2초 유지 · 상대값: Ground y −200 → 0).
    /// </summary>
    private static MotionEditor PlayEditor()
    {
        MotionEditor editor = NestedEditor();
        editor.AddRigInstance("city", "car", "car");
        editor.UpdateRigNode("city", "car", new RigNodeAsset { X = 300f, Y = 50f, Alpha = true });

        editor.AddMotion("roll", "car", 0.4f, MotionEnd.Commit);
        editor.AddChannel("roll", "Body", MotionChannelKind.Position);
        editor.SetKeys("roll", "Body", MotionChannelKind.Position, Axis.X, new[] { Lin(0f, 0f), Lin(0.4f, 100f) });

        editor.AddMotion("hop", "car", 0.2f);
        editor.AddChannel("hop", "Body", MotionChannelKind.Position);
        editor.SetKeys("hop", "Body", MotionChannelKind.Position, Axis.Y, new[] { Lin(0f, 0f), Lin(0.1f, 30f), Lin(0.2f, 0f) });
        editor.AddMotionEvent("hop", 0.1f, "thud");

        editor.AddMotion("spin", "car", 0.5f, MotionEnd.Commit, MotionChannelMode.Absolute);
        editor.AddChannel("spin", "Wheel", MotionChannelKind.Euler);
        editor.SetKeys("spin", "Wheel", MotionChannelKind.Euler, Axis.Z, new[] { Lin(0f, 0f), Lin(0.5f, 90f) });

        editor.AddMotion("city_in", "city", 2f, MotionEnd.Commit);
        editor.AddChannel("city_in", "Ground", MotionChannelKind.Position);
        editor.SetKeys("city_in", "Ground", MotionChannelKind.Position, Axis.Y, new[] { Lin(0f, -200f), Lin(2f, 0f) });
        return editor;
    }

    private static Vec2 PosedAt(MotionEditor editor, string node, double t)
    {
        RigSchemaFile rig = RigAssets.Compose(Dump, editor.Document);
        var scene = new SceneAsset { Name = "s", Objects = { new SceneObject { Id = "c", Rig = "city" } } };
        RectNodeTree template = ScenePreview.Build(rig, scene);
        RectNodeTree posed = MotionPreview.Pose(template, editor.Document.Find("city_in")!, t, MotionChannelMode.Relative, null, "c/");
        return posed.GetState("c/" + node).AnchoredPosition - template.GetState("c/" + node).AnchoredPosition;
    }

    [Fact]
    public void 재생_블록은_품은_세트의_애니메이션을_그_시각에_틀고_유지는_남고_복원은_돌아간다()
    {
        MotionEditor editor = PlayEditor();
        long revision = editor.Revision;

        // 0.5초에 roll(유지)을 두 번, 1.5초에 hop(복원). 각각 한 칸, 시작 시각 순으로 선다.
        Assert.Equal(0, editor.AddMotionPlay("city_in", "car", "hop", 1.5f));
        Assert.Equal(0, editor.AddMotionPlay("city_in", "car", "roll", 0.5f, repeat: 2));
        Assert.Equal(revision + 2, editor.Revision);
        MotionAsset motion = editor.Document.Find("city_in")!;
        Assert.Equal(new[] { ("roll", 0.5f), ("hop", 1.5f) }, motion.Plays.Select(p => (p.Motion, p.At)));

        // 구운 것: 제 채널(Ground) + 품은 것의 채널이 경로 이름으로. 저작본에는 재생 블록만 적힌다.
        Assert.NotNull(motion.Flattened);
        Assert.Equal(new[] { "Ground", "car.Body" }, motion.Flattened!.Channels.Select(c => c.Node).Distinct());
        string json = MotionDocumentJson.Write(editor.Document);
        Assert.Contains("\"plays\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("car.Body", json, StringComparison.Ordinal);
        Assert.NotNull(MotionDocumentJson.Read(json).Find("city_in")!.Flattened); // 읽으면 다시 굽는다

        // 미리보기: 블록 전에는 제자리 → roll 첫 바퀴(0.5~0.9) → 둘째 바퀴가 쌓인다(0.9~1.3: 100 → 200) → 그 자세가 남는다.
        Assert.Equal(0f, PosedAt(editor, "car.Body", 0.3).X, 1);
        Assert.Equal(50f, PosedAt(editor, "car.Body", 0.7).X, 1);
        Assert.Equal(150f, PosedAt(editor, "car.Body", 1.1).X, 1);
        Assert.Equal(200f, PosedAt(editor, "car.Body", 1.4).X, 1);
        Assert.Equal(200f, PosedAt(editor, "car.Body", 2.0).X, 1);

        // hop(복원): 1.5~1.7초에 뛰고 돌아온다. 제 채널(Ground)은 그대로 돈다.
        Assert.Equal(30f, PosedAt(editor, "car.Body", 1.6).Y, 1);
        Assert.Equal(0f, PosedAt(editor, "car.Body", 1.9).Y, 1);
        Assert.Equal(-100f, PosedAt(editor, "Ground", 1.0).Y, 1);

        // 품은 애니메이션의 이벤트가 그 시각에 울린다(1.5 + 0.1).
        MotionEventAsset thud = Assert.Single(MotionEvents.Between(motion, 0, 2));
        Assert.Equal(("thud", 1.6f), (thud.Name, MathF.Round(thud.T, 3)));

        // 산출물: 한 덩어리 모션으로 나간다 — 규칙을 지나고, 런타임은 재생 블록을 모른다.
        RigSchemaFile rig = RigAssets.Compose(Dump, editor.Document);
        Assert.Empty(MotionValidator.Validate(editor.Document, rig.NodeIds, null, rig.AlphaNodeIds));
        string exported = MotionsJson.Write(MotionExport.ToClips(editor.Document));
        Assert.Contains("car.Body", exported, StringComparison.Ordinal);
        Assert.DoesNotContain("plays", exported, StringComparison.Ordinal);

        // 참조다 — 품은 애니메이션을 고치면 구운 것이 따라온다.
        editor.SetKeys("roll", "Body", MotionChannelKind.Position, Axis.X, new[] { Lin(0f, 0f), Lin(0.4f, 10f) });
        Assert.Equal(20f, PosedAt(editor, "car.Body", 1.4).X, 1);

        // 되돌리면 구운 것도 돌아간다. 블록을 다 지우면 구운 것이 없다.
        editor.Undo();
        Assert.Equal(200f, PosedAt(editor, "car.Body", 1.4).X, 1);
        editor.RemoveMotionPlay("city_in", 1);
        editor.RemoveMotionPlay("city_in", 0);
        Assert.Null(editor.Document.Find("city_in")!.Flattened);
        Assert.Equal(0f, PosedAt(editor, "car.Body", 1.4).X, 1);
    }

    [Fact]
    public void 재생_블록은_절대값_애니메이션도_바깥의_기준으로_옮겨_굽고_바깥의_제_키가_이긴다()
    {
        MotionEditor editor = PlayEditor();
        // 바퀴의 가만히 선 회전을 20도로 — spin(절대값 0 → 90)은 "20도에서 −20 … +70 떨어진 것"으로 구워진다(바깥은 상대값).
        editor.UpdateRigNode("car", "Wheel", new RigNodeAsset { X = 30f, Y = -40f, Width = 20f, Height = 20f, Rotation = 20f, Color = "#222222", Alpha = true });
        editor.AddMotionPlay("city_in", "car", "spin", 1f);
        ChannelAsset baked = editor.Document.Find("city_in")!.Flattened!.FindChannel("car.Wheel", MotionChannelKind.Euler)!;
        float At(float t) => (float)Mt.Authoring.Graph.CurveSampler.Sample(baked.Z!.Keys, t, t + 0.0001, 1)[0].V;
        Assert.Equal(0f, At(0.5f), 1);    // 블록 전: 제자리(20도 그대로 = 떨어진 것 0)
        Assert.Equal(25f, At(1.25f), 1);  // 절대값 45도 = 20 + 25
        Assert.Equal(70f, At(1.8f), 1);   // 유지 — 90도에 남는다

        // 바깥이 같은 경로에 제 키를 놓으면 그 성분은 제 키가 이긴다(여기서만 덮어쓴다).
        editor.AddChannel("city_in", "car.Wheel", MotionChannelKind.Euler);
        editor.SetKeys("city_in", "car.Wheel", MotionChannelKind.Euler, Axis.Z, new[] { Lin(0f, 0f), Lin(2f, 5f) });
        Assert.Equal(2, editor.Document.Find("city_in")!.Flattened!.FindChannel("car.Wheel", MotionChannelKind.Euler)!.Z!.Keys.Count);
    }

    [Fact]
    public void 재생_블록은_품은_세트의_것만_길이_안에서_겹치지_않게_놓인다()
    {
        MotionEditor editor = PlayEditor();
        editor.AddMotionPlay("city_in", "car", "roll", 0.5f);
        long revision = editor.Revision;

        Assert.Throws<InvalidOperationException>(() => editor.AddMotionPlay("city_in", "Ground", "roll", 0f));       // 품은 오브젝트가 아니다
        Assert.Throws<InvalidOperationException>(() => editor.AddMotionPlay("city_in", "car", "city_in", 0f));       // 그 세트의 애니메이션이 아니다
        Assert.Throws<InvalidOperationException>(() => editor.AddMotionPlay("city_in", "car", "roll", 1.8f));        // 길이를 넘는다
        Assert.Throws<InvalidOperationException>(() => editor.AddMotionPlay("city_in", "car", "hop", 0.6f));         // 같은 인스턴스의 블록과 겹친다
        Assert.Throws<InvalidOperationException>(() => editor.AddMotionPlay("city_in", "car", "roll", 1f, repeat: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.RemoveMotionPlay("city_in", 5));
        Assert.Equal(revision, editor.Revision);

        // 고치기 — 같은 값은 무기록, 시각이 바뀌면 자리가 옮겨진다.
        editor.AddMotionPlay("city_in", "car", "hop", 1.5f);
        revision = editor.Revision;
        Assert.Equal(0, editor.UpdateMotionPlay("city_in", 0, "car", "roll", 0.5f, 1));
        Assert.Equal(revision, editor.Revision);
        Assert.Equal(0, editor.UpdateMotionPlay("city_in", 1, "car", "hop", 0.1f, 1)); // roll 앞으로
        Assert.Equal(new[] { "hop", "roll" }, editor.Document.Find("city_in")!.Plays.Select(p => p.Motion));
    }

    [Fact]
    public void 품은_애니메이션이_길어져_재생_블록이_넘치면_검증이_알리고_뒤쪽이_잘린_채_나간다()
    {
        MotionEditor editor = PlayEditor();
        editor.AddMotionPlay("city_in", "car", "roll", 1.5f); // 1.5 + 0.4 ≤ 2
        RigSchemaFile rig = RigAssets.Compose(Dump, editor.Document);
        Assert.Empty(MotionValidator.Validate(editor.Document, rig.NodeIds, null, rig.AlphaNodeIds));

        // 품은 애니메이션을 길게(1초) — 바깥에서 막을 길이 없으니 검증이 알린다(막지는 않는다).
        editor.SetDuration("roll", 1f);
        editor.SetKeys("roll", "Body", MotionChannelKind.Position, Axis.X, new[] { Lin(0f, 0f), Lin(1f, 100f) });
        MotionProblem problem = Assert.Single(MotionValidator.Validate(editor.Document, rig.NodeIds, null, rig.AlphaNodeIds));
        Assert.False(problem.Blocking);
        Assert.Equal("city_in", problem.MotionName);
        Assert.Contains("길이", problem.Message, StringComparison.Ordinal);

        // 구운 것은 길이 안에서 끝난다 — 산출물이 그대로 나간다.
        ChannelAsset baked = editor.Document.Find("city_in")!.Flattened!.FindChannel("car.Body", MotionChannelKind.Position)!;
        Assert.Equal(2f, baked.X!.Keys[^1].T);
        Assert.All(baked.X.Keys.Zip(baked.X.Keys.Skip(1)), pair => Assert.True(pair.Second.T > pair.First.T));
        Assert.Contains("car.Body", MotionsJson.Write(MotionExport.ToClips(editor.Document)), StringComparison.Ordinal);
    }

    [Fact]
    public void 재생_블록을_따로_떼면_구운_키가_제_키로_굳고_재생은_그대로이며_원본을_더는_안_따른다()
    {
        MotionEditor editor = PlayEditor();
        editor.AddMotionPlay("city_in", "car", "roll", 0.5f, repeat: 2);
        editor.AddMotionPlay("city_in", "car", "hop", 1.5f);
        double[] times = { 0.3, 0.7, 1.1, 1.4, 1.6, 1.9 };
        List<Vec2> before = times.Select(t => PosedAt(editor, "car.Body", t)).ToList();

        // 그 인스턴스의 블록을 다 뗀다 — 한 칸. 블록이 사라지고 경로 이름의 채널이 제 것으로 선다. 이벤트도 옮겨 온다.
        long revision = editor.Revision;
        Assert.Equal(1, editor.DetachMotionPlays("city_in", "car"));
        Assert.Equal(revision + 1, editor.Revision);
        MotionAsset motion = editor.Document.Find("city_in")!;
        Assert.Empty(motion.Plays);
        Assert.Null(motion.Flattened);
        Assert.NotNull(motion.FindChannel("car.Body", MotionChannelKind.Position));
        Assert.Equal(("thud", 1.6f), (Assert.Single(motion.Events).Name, MathF.Round(motion.Events[0].T, 3)));

        // 재생은 그대로다.
        for (int i = 0; i < times.Length; i++)
        {
            Vec2 after = PosedAt(editor, "car.Body", times[i]);
            Assert.Equal(before[i].X, after.X, 1);
            Assert.Equal(before[i].Y, after.Y, 1);
        }

        RigSchemaFile rig = RigAssets.Compose(Dump, editor.Document);
        Assert.Empty(MotionValidator.Validate(editor.Document, rig.NodeIds, null, rig.AlphaNodeIds));

        // 원본을 고쳐도 더는 안 따라온다.
        editor.SetKeys("roll", "Body", MotionChannelKind.Position, Axis.X, new[] { Lin(0f, 0f), Lin(0.4f, 10f) });
        Assert.Equal(before[3].X, PosedAt(editor, "car.Body", 1.4).X, 1);

        // 블록이 없으면 막힌다. 되돌리면 블록이 돌아온다.
        Assert.Throws<InvalidOperationException>(() => editor.DetachMotionPlays("city_in", "car"));
        editor.Undo();
        editor.Undo();
        Assert.Equal(2, editor.Document.Find("city_in")!.Plays.Count);
    }

    // ── 새 세트로 빼기 · 세트 이름 바꾸기 ─────────────────────────────

    [Fact]
    public void 고른_오브젝트들을_새_세트로_빼면_자리와_그림과_키를_데리고_가고_보이는_것은_그대로다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddRig(new RigAsset { Kind = "town", Nodes = { new RigNodeAsset { Id = "Ground", Width = 1920f, Height = 200f, Color = "#334455", Alpha = true } } });
        editor.AddRigNode("town", "Sun", null, image: true);
        editor.UpdateRigNode("town", "Sun", new RigNodeAsset { X = 300f, Y = 200f, Width = 100f, Height = 100f, Image = true, Alpha = true });
        editor.AddRigNode("town", "Ray", "Sun");
        editor.UpdateRigNode("town", "Ray", new RigNodeAsset { X = 10f, Width = 20f, Height = 20f, Color = "#FFFFFF", Alpha = true });
        editor.AddRigNode("town", "Glow", null, image: true);
        editor.UpdateRigNode("town", "Glow", new RigNodeAsset { X = 310f, Y = 190f, Width = 300f, Height = 300f, Image = true, Alpha = true });
        editor.AddRigNode("town", "Tree", null, image: true);
        editor.AddSkin("town", "town");
        editor.SetSkinPart("town", "Sun", "art/sun.png");
        editor.SetSkinPart("town", "Glow", "art/glow.png");
        editor.SetSkinPart("town", "Tree", "art/tree.png");

        // 상대값 애니메이션(해가 떠오르고 빛살이 돈다, 나무가 민다) + 절대값 애니메이션(해가 자리 300 → 400).
        editor.AddMotion("town_in", "town", 1f, MotionEnd.Commit);
        editor.AddChannel("town_in", "Sun", MotionChannelKind.Position);
        editor.SetKeys("town_in", "Sun", MotionChannelKind.Position, Axis.Y, new[] { Lin(0f, -100f), Lin(1f, 0f) });
        editor.AddChannel("town_in", "Ray", MotionChannelKind.Euler);
        editor.SetKeys("town_in", "Ray", MotionChannelKind.Euler, Axis.Z, new[] { Lin(0f, 0f), Lin(1f, 90f) });
        editor.AddChannel("town_in", "Tree", MotionChannelKind.Position);
        editor.SetKeys("town_in", "Tree", MotionChannelKind.Position, Axis.X, new[] { Lin(0f, 50f), Lin(1f, 0f) });
        editor.AddMotion("slide", "town", 1f, MotionEnd.Commit, MotionChannelMode.Absolute);
        editor.AddChannel("slide", "Sun", MotionChannelKind.Position);
        editor.SetKeys("slide", "Sun", MotionChannelKind.Position, Axis.X, new[] { Lin(0f, 300f), Lin(1f, 400f) });
        editor.SetKeys("slide", "Sun", MotionChannelKind.Position, Axis.Y, new[] { Lin(0f, 200f), Lin(1f, 200f) });

        Vec2 World(string motion, string node, double t)
        {
            RigSchemaFile rig = RigAssets.Compose(Dump, editor.Document);
            var scene = new SceneAsset { Name = "s", Objects = { new SceneObject { Id = "c", Rig = "town" } } };
            MotionAsset asset = editor.Document.Find(motion)!;
            return MotionPreview.Pose(ScenePreview.Build(rig, scene), asset, t, asset.PreviewMode, null, "c/").TransformPoint("c/" + node, Vec3.Zero).XY;
        }

        double[] times = { 0, 0.3, 0.7, 1 };
        var before = times.Select(t => (World("town_in", "Sun", t), World("town_in", "Ray", t), World("town_in", "Glow", t), World("slide", "Sun", t))).ToList();

        // 해와 빛을 새 세트 star로 — 한 칸. 그 자리에 인스턴스가, 고른 것들의 가운데에 선다.
        long revision = editor.Revision;
        Assert.Equal("star", editor.ExtractRigNodes("town", new[] { "Sun", "Glow" }, "star"));
        Assert.Equal(revision + 1, editor.Revision);
        RigAsset town = editor.Document.FindRig("town")!;
        Assert.Equal(new[] { "Ground", "star", "Tree" }, town.Nodes.Select(n => n.Id));
        Assert.Equal(("star", 310f, 190f), (town.Find("star")!.Set, town.Find("star")!.X, town.Find("star")!.Y));
        RigAsset star = editor.Document.FindRig("star")!;
        Assert.Equal(new[] { ("Sun", "", -10f, 10f), ("Ray", "Sun", 10f, 0f), ("Glow", "", 0f, 0f) }, star.Nodes.Select(n => (n.Id, n.Parent, n.X, n.Y)));

        // 그림이 새 세트의 스킨으로, 키가 새 세트의 애니메이션으로 — 원래 애니메이션에는 그것을 트는 재생 블록이 선다.
        Assert.Equal(new[] { "Tree" }, editor.Document.FindSkin("town")!.Parts.Keys);
        Assert.Equal("star", editor.Document.FindSkin("star")!.Rig);
        Assert.Equal(new[] { "Glow", "Sun" }, editor.Document.FindSkin("star")!.Parts.Keys);
        Assert.Equal(new[] { "Tree" }, editor.Document.Find("town_in")!.Channels.Select(c => c.Node));
        Assert.Equal(new[] { ("star", "star_in", 0f) }, editor.Document.Find("town_in")!.Plays.Select(p => (p.Node, p.Motion, p.At)));
        Assert.Equal("star", editor.Document.Find("star_in")!.Rig);
        Assert.Equal(new[] { "Sun", "Ray" }, editor.Document.Find("star_in")!.Channels.Select(c => c.Node));
        Assert.Equal(MotionChannelMode.Absolute, editor.Document.Find("star_slide")!.PreviewMode);

        // 보이는 것은 그대로다 — 상대값도 절대값도.
        var after = times.Select(t => (World("town_in", "star.Sun", t), World("town_in", "star.Ray", t), World("town_in", "star.Glow", t), World("slide", "star.Sun", t))).ToList();

        for (int i = 0; i < times.Length; i++)
        {
            Assert.Equal(before[i].Item1.X, after[i].Item1.X, 1);
            Assert.Equal(before[i].Item1.Y, after[i].Item1.Y, 1);
            Assert.Equal(before[i].Item2.X, after[i].Item2.X, 1);
            Assert.Equal(before[i].Item2.Y, after[i].Item2.Y, 1);
            Assert.Equal(before[i].Item3.X, after[i].Item3.X, 1);
            Assert.Equal(before[i].Item4.X, after[i].Item4.X, 1);
            Assert.Equal(before[i].Item4.Y, after[i].Item4.Y, 1);
        }

        RigSchemaFile composed = RigAssets.Compose(Dump, editor.Document);
        Assert.Empty(MotionValidator.Validate(editor.Document, composed.NodeIds, null, composed.AlphaNodeIds));

        // 막는 것: 있는 이름 · 부모가 다른 것들. 문서는 그대로다.
        revision = editor.Revision;
        Assert.Throws<InvalidOperationException>(() => editor.ExtractRigNodes("town", new[] { "Tree" }, "star"));
        Assert.Throws<InvalidOperationException>(() => editor.ExtractRigNodes("star", new[] { "Ray", "Glow" }, "bits"));
        Assert.Equal(revision, editor.Revision);

        // 세트 이름 바꾸기 — 한 칸. 품은 곳 · 애니메이션 · 스킨이 따라오고, 세트의 이름을 딴 이름들(애니메이션 star_x · 스킨 star · 인스턴스 star)도 함께 바뀐다.
        editor.RenameRig("star", "sol");
        Assert.Equal(revision + 1, editor.Revision);
        Assert.Equal("sol", town.Find("sol")!.Set);
        Assert.Null(town.Find("star"));
        Assert.Equal(("sol", "sol"), (editor.Document.Find("sol_in")!.Rig, editor.Document.FindSkin("sol")!.Rig));
        Assert.NotNull(editor.Document.Find("sol_slide"));
        Assert.Equal(new[] { ("sol", "sol_in", 0f) }, editor.Document.Find("town_in")!.Plays.Select(p => (p.Node, p.Motion, p.At)));
        Assert.Equal(before[2].Item1.Y, World("town_in", "sol.Sun", 0.7).Y, 1); // 재생은 그대로다
        Assert.Throws<InvalidOperationException>(() => editor.RenameRig("sol", "town"));

        // 되돌리면 통째로 돌아간다.
        editor.Undo();
        editor.Undo();
        Assert.Null(editor.Document.FindRig("star"));
        Assert.Equal(new[] { "Ground", "Sun", "Ray", "Glow", "Tree" }, editor.Document.FindRig("town")!.Nodes.Select(n => n.Id));
        Assert.Empty(editor.Document.Find("town_in")!.Plays);
    }

    // ── 화면 밖에서 들어온다 · 떨어진 오브젝트를 함께 뺀다 ───────────────

    [Fact]
    public void 화면_밖에서_들어오는_거리는_오브젝트의_사각형이_무대_밖으로_다_나가는_만큼이다()
    {
        var stage = new Vec2(1920f, 1080f);
        var editor = new MotionEditor(new MotionDocument());
        editor.AddRig(new RigAsset { Kind = "town", Nodes = { new RigNodeAsset { Id = "Group", X = 100f, Y = -50f, Alpha = true } } });
        editor.AddRigNode("town", "Sun", null, image: true);
        editor.UpdateRigNode("town", "Sun", new RigNodeAsset { X = 300f, Y = 200f, Width = 100f, Height = 100f, Image = true, Alpha = true });
        editor.AddRigNode("town", "Leaf", "Group", image: true);
        editor.UpdateRigNode("town", "Leaf", new RigNodeAsset { X = 10f, Y = 20f, Width = 60f, Height = 80f, Rotation = 30f, Image = true, Alpha = true });
        editor.AddMotion("town_in", "town", 1f, MotionEnd.Commit);

        // 왼쪽에서: 무대의 반(960) + 자리(300) + 너비의 반(50) = 1310만큼 왼쪽에서 출발한다. 0.2초부터 0.5초 동안 — 앞뒤로는 가만히. 한 칸.
        long revision = editor.Revision;
        Assert.Equal(-1310f, editor.SlideFromOutside("town_in", "Sun", StageSide.Left, 0.2f, 0.5f, stage));
        Assert.Equal(revision + 1, editor.Revision);
        Assert.Equal(new[] { (0f, -1310f), (0.2f, -1310f), (0.7f, 0f), (1f, 0f) }, editor.Document.Find("town_in")!.FindChannel("Sun", MotionChannelKind.Position)!.X!.Keys.Select(k => (k.T, k.V)));

        // 위에서: 540 − 200 + 50 = 390. 나가는 것은 거꾸로(제자리 → 밖), 끝까지면 끝 키 하나로.
        Assert.Equal(390f, editor.SlideFromOutside("town_in", "Sun", StageSide.Top, 0.6f, 0.5f, stage, leaving: true));
        Assert.Equal(new[] { (0f, 0f), (0.6f, 0f), (1f, 390f) }, editor.Document.Find("town_in")!.FindChannel("Sun", MotionChannelKind.Position)!.Y!.Keys.Select(k => (k.T, k.V)));

        // 부모의 자리를 더하고, 돈 오브젝트는 감싸는 원으로 넉넉히: 960 − (100 + 10) + √(30² + 40²) = 900.
        Assert.Equal(900f, editor.SlideFromOutside("town_in", "Leaf", StageSide.Right, 0f, 0.5f, stage));

        // 절대값 애니메이션이면 가만히 선 자리에 얹는다(300 − 1310 → 300).
        editor.AddMotion("abs_in", "town", 1f, MotionEnd.Commit, MotionChannelMode.Absolute);
        editor.SlideFromOutside("abs_in", "Sun", StageSide.Left, 0f, 1f, stage);
        Assert.Equal(new[] { -1010f, 300f }, editor.Document.Find("abs_in")!.FindChannel("Sun", MotionChannelKind.Position)!.X!.Keys.Select(k => k.V));

        // 길이 밖의 시작 · 없는 오브젝트는 막힌다.
        revision = editor.Revision;
        Assert.Throws<InvalidOperationException>(() => editor.SlideFromOutside("town_in", "Sun", StageSide.Left, 1f, 0.5f, stage));
        Assert.Throws<KeyNotFoundException>(() => editor.SlideFromOutside("town_in", "Nope", StageSide.Left, 0f, 0.5f, stage));
        Assert.Equal(revision, editor.Revision);
        RigSchemaFile rig = RigAssets.Compose(Dump, editor.Document);
        Assert.Empty(MotionValidator.Validate(editor.Document, rig.NodeIds, null, rig.AlphaNodeIds));
    }

    [Fact]
    public void 떨어져_있는_오브젝트도_같은_부모_밑이면_한_세트로_빼진다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddRig(new RigAsset { Kind = "town" });

        foreach (string id in new[] { "A", "B", "C", "D" })
        {
            editor.AddRigNode("town", id, null, image: true);
        }

        // A와 C(사이에 B) — 첫 것의 자리에 인스턴스가 서고, 새 세트 안의 순서는 원래 순서다.
        Assert.Equal("pair", editor.ExtractRigNodes("town", new[] { "C", "A" }, "pair"));
        Assert.Equal(new[] { "pair", "B", "D" }, editor.Document.FindRig("town")!.Nodes.Select(n => n.Id));
        Assert.Equal(new[] { "A", "C" }, editor.Document.FindRig("pair")!.Nodes.Select(n => n.Id));
    }
}
