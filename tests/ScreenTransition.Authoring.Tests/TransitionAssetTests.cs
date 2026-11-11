using System.Text.Json.Nodes;
using ScreenTransition.Core;
using ScreenTransition.Authoring.Editing;
using ScreenTransition.Authoring.Model;
using ScreenTransition.Authoring.Preview;
using ScreenTransition.Authoring.Serialization;
using ScreenTransition.Authoring.Validation;

namespace ScreenTransition.Authoring.Tests;

/// <summary>툴에서 정의한 전환 — 미리보기는 이것만으로 선다. Spine처럼 툴이 원본.</summary>
public class TransitionAssetTests
{
    private static MotionEditor NewEditor()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddTransition(TransitionTemplates.Emoji());
        return editor;
    }

    // ── 전환 편집기 ─────────────────────────────────

    private static string[] Ids(MotionEditor editor, string kind = TransitionTemplates.EmojiName) => editor.Document.FindTransition(kind)!.Layers.Select(n => n.Id).ToArray();

    [Fact]
    public void 층을_짓고_지우고_옮기면_부모가_늘_자식보다_먼저_온다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddTransition(TransitionTemplates.Emoji(parts: 2));
        long revision = editor.Revision;

        // 더하기 — 부모의 식구들 맨 뒤. 크기 0의 묶는 층으로 선다(그림 · 크기는 값으로 준다).
        LayerAsset glow = editor.AddLayer(TransitionTemplates.EmojiName, "Emoji_Glow", "Emoji_Anchor");
        Assert.Equal((0f, 0f, null, false), (glow.Width, glow.Height, glow.Image, glow.Alpha));
        editor.AddLayer(TransitionTemplates.EmojiName, "Emoji_Spark", "Emoji_Glow");
        editor.AddLayer(TransitionTemplates.EmojiName, "Extra"); // 루트 밑
        Assert.Equal(revision + 3, editor.Revision);
        Assert.Equal(new[] { "Emoji_Anchor", "Emoji_Body", "Emoji_Part1", "Emoji_Part2", "Emoji_Glow", "Emoji_Spark", "Extra" }, Ids(editor));

        // 막는 것 — 기록이 안 남는다.
        revision = editor.Revision;
        Assert.Throws<InvalidOperationException>(() => editor.AddLayer(TransitionTemplates.EmojiName, "Emoji_Glow"));       // 이미 있다
        Assert.Throws<InvalidOperationException>(() => editor.AddLayer(TransitionTemplates.EmojiName, "1bad"));
        Assert.Throws<InvalidOperationException>(() => editor.AddLayer(TransitionTemplates.EmojiName, "a/b"));
        Assert.Throws<KeyNotFoundException>(() => editor.AddLayer(TransitionTemplates.EmojiName, "X", "Nope"));
        Assert.Throws<KeyNotFoundException>(() => editor.AddLayer("figure", "X"));                                 // 없는 전환
        Assert.Throws<InvalidOperationException>(() => editor.SetLayerParent(TransitionTemplates.EmojiName, "Emoji_Glow", "Emoji_Spark")); // 제 밑으로
        Assert.Throws<InvalidOperationException>(() => editor.UpdateLayer(TransitionTemplates.EmojiName, "Extra", new LayerAsset { Width = -1f }));
        Assert.Equal(revision, editor.Revision);

        // 형제 사이의 순서 — 식구째 옮긴다. 끝에서는 아무 일도 없다.
        editor.MoveLayer(TransitionTemplates.EmojiName, "Emoji_Glow", -1);
        Assert.Equal(new[] { "Emoji_Anchor", "Emoji_Glow", "Emoji_Spark", "Emoji_Body", "Emoji_Part1", "Emoji_Part2", "Extra" }, Ids(editor));
        editor.MoveLayer(TransitionTemplates.EmojiName, "Emoji_Glow", -1);
        editor.MoveLayer(TransitionTemplates.EmojiName, "Emoji_Part2", +1);
        Assert.Equal(revision + 1, editor.Revision);
        editor.MoveLayer(TransitionTemplates.EmojiName, "Emoji_Anchor", +1);
        Assert.Equal(new[] { "Extra", "Emoji_Anchor", "Emoji_Glow", "Emoji_Spark", "Emoji_Body", "Emoji_Part1", "Emoji_Part2" }, Ids(editor));

        // 부모 바꾸기 — 식구째, 새 부모의 맨 뒤로. 같은 부모면 무기록.
        revision = editor.Revision;
        editor.SetLayerParent(TransitionTemplates.EmojiName, "Emoji_Glow", "Emoji_Body");
        editor.SetLayerParent(TransitionTemplates.EmojiName, "Emoji_Glow", "Emoji_Body");
        Assert.Equal(revision + 1, editor.Revision);
        Assert.Equal(new[] { "Extra", "Emoji_Anchor", "Emoji_Body", "Emoji_Part1", "Emoji_Part2", "Emoji_Glow", "Emoji_Spark" }, Ids(editor));
        Assert.Equal("Emoji_Body", editor.Document.FindTransition(TransitionTemplates.EmojiName)!.Find("Emoji_Glow")!.Parent);
        editor.SetLayerParent(TransitionTemplates.EmojiName, "Emoji_Body", null); // 루트로
        Assert.Equal(new[] { "Extra", "Emoji_Anchor", "Emoji_Body", "Emoji_Part1", "Emoji_Part2", "Emoji_Glow", "Emoji_Spark" }, Ids(editor));
        Assert.Equal("", editor.Document.FindTransition(TransitionTemplates.EmojiName)!.Find("Emoji_Body")!.Parent);

        // 값 — 한 칸, 같은 값은 무기록. 세운 나무에 그대로 선다.
        revision = editor.Revision;
        var moved = new LayerAsset { X = 30f, Y = -12f, Width = 80f, Height = 40f, Image = "art/glow.png", Alpha = false };
        editor.UpdateLayer(TransitionTemplates.EmojiName, "Emoji_Glow", moved);
        editor.UpdateLayer(TransitionTemplates.EmojiName, "Emoji_Glow", moved);
        Assert.Equal(revision + 1, editor.Revision);
        RectNodeState placed = StageTree.Build(editor.Document.FindTransition(TransitionTemplates.EmojiName)!).GetState("Emoji_Glow");
        Assert.Equal((30f, -12f, 80f, 40f), (placed.AnchoredPosition.X, placed.AnchoredPosition.Y, placed.SizeDelta.X, placed.SizeDelta.Y));

        // 가만히 선 자세의 회전·배율(배치 모드) — 세운 나무에 그대로 선다. 배율 0은 막는다.
        editor.UpdateLayer(TransitionTemplates.EmojiName, "Emoji_Glow", new LayerAsset { X = 30f, Y = -12f, Width = 80f, Height = 40f, Image = "art/glow.png", Rotation = 30f, ScaleX = 2f, ScaleY = -1f });
        RectNodeState turned = StageTree.Build(editor.Document.FindTransition(TransitionTemplates.EmojiName)!).GetState("Emoji_Glow");
        Assert.Equal((30f, 2f, -1f), (turned.LocalEulerAngles.Z, turned.LocalScale.X, turned.LocalScale.Y));
        Assert.Throws<InvalidOperationException>(() => editor.UpdateLayer(TransitionTemplates.EmojiName, "Emoji_Glow", new LayerAsset { ScaleX = 0f }));
        editor.Undo();

        // 지우기 — 밑의 층도 함께. 되돌리면 그 자리에 돌아온다.
        editor.RemoveLayer(TransitionTemplates.EmojiName, "Emoji_Body");
        Assert.Equal(new[] { "Extra", "Emoji_Anchor" }, Ids(editor));
        editor.Undo();
        Assert.Equal(7, Ids(editor).Length);
        StageTree.Build(editor.Document.FindTransition(TransitionTemplates.EmojiName)!); // 여전히 세울 수 있다
    }

    [Fact]
    public void 층의_이름을_바꾸면_자식과_채널이_따라온다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddTransition(TransitionTemplates.Emoji(parts: 2));
        editor.AddChannel(TransitionTemplates.EmojiName, "Emoji_Part1", MotionChannelKind.Position);
        editor.AddChannel(TransitionTemplates.EmojiName, "Emoji_Body", MotionChannelKind.Position);

        long revision = editor.Revision;
        editor.RenameLayer(TransitionTemplates.EmojiName, "Emoji_Part1", "Drop");
        editor.RenameLayer(TransitionTemplates.EmojiName, "Emoji_Anchor", "Pin");
        editor.RenameLayer(TransitionTemplates.EmojiName, "Pin", "Pin"); // 같은 이름은 무기록
        Assert.Equal(revision + 2, editor.Revision);

        Assert.Equal(new[] { "Pin", "Emoji_Body", "Drop", "Emoji_Part2" }, Ids(editor));
        Assert.Equal("Pin", editor.Document.FindTransition(TransitionTemplates.EmojiName)!.Find("Emoji_Body")!.Parent);
        Assert.Equal(new[] { "Drop", "Emoji_Body" }, editor.Document.Find(TransitionTemplates.EmojiName)!.Channels.Select(c => c.Node));
        Assert.Equal("art/part1.png", editor.Document.FindTransition(TransitionTemplates.EmojiName)!.Find("Drop")!.Image); // 그림은 층의 것이라 함께 간다

        // 이름이 겹치거나 규칙에 안 맞으면 막는다. 되돌리면 전부 함께 돌아온다.
        Assert.Throws<InvalidOperationException>(() => editor.RenameLayer(TransitionTemplates.EmojiName, "Drop", "Emoji_Body"));
        Assert.Throws<InvalidOperationException>(() => editor.RenameLayer(TransitionTemplates.EmojiName, "Drop", "물방울"));
        editor.Undo();
        editor.Undo();
        Assert.Equal("Emoji_Part1", editor.Document.Find(TransitionTemplates.EmojiName)!.Channels[0].Node);
        Assert.Equal("Emoji_Anchor", Ids(editor)[0]);
    }

    // ── 시차 — 층의 움직임을 통째로 민다 ─────────────────────────

    [Fact]
    public void 움직임을_밀면_길이는_그대로이고_비는_쪽은_가만히_넘치는_쪽은_잘린다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddTransition(TransitionTemplates.Emoji(parts: 3));
        editor.AddTimeline("in", 1f, TransitionTemplates.Emoji());
        KeyAsset Lin(float t, float v) => new() { T = t, V = v, Tangent = TangentMode.Linear };

        foreach (string part in new[] { "Emoji_Part1", "Emoji_Part2", "Emoji_Part3" })
        {
            editor.AddChannel("in", part, MotionChannelKind.Position);
            editor.SetKeys("in", part, MotionChannelKind.Position, Axis.X, new[] { Lin(0f, -100f), Lin(0.2f, -100f), Lin(0.6f, 0f), Lin(1f, 0f) }); // 0.2~0.6초에 들어온다
        }

        (float T, float V)[] Keys(string part) => editor.Document.Find("in")!.FindChannel(part, MotionChannelKind.Position)!.X!.Keys.Select(k => (MathF.Round(k.T, 3), MathF.Round(k.V, 1))).ToArray();
        long revision = editor.Revision;

        // 0.1초 늦게 — 안쪽 키가 밀리고 처음과 끝은 제 값으로 선다. 한 칸.
        Assert.True(editor.ShiftLayerKeys("in", "Emoji_Part1", 0.1f));
        Assert.Equal(revision + 1, editor.Revision);
        Assert.Equal(new[] { (0f, -100f), (0.3f, -100f), (0.7f, 0f), (1f, 0f) }, Keys("Emoji_Part1"));

        // 넘치게 늦추면 잘린다 — 끝의 키는 잘린 자리의 값(아직 들어오는 중).
        editor.ShiftLayerKeys("in", "Emoji_Part2", 0.6f);
        Assert.Equal(new[] { (0f, -100f), (0.8f, -100f), (1f, -50f) }, Keys("Emoji_Part2"));

        // 일찍 — 처음의 키는 잘린 자리의 값(이미 들어오는 중), 끝은 마지막 값으로 가만히.
        editor.ShiftLayerKeys("in", "Emoji_Part3", -0.4f);
        Assert.Equal(new[] { (0f, -50f), (0.2f, 0f), (1f, 0f) }, Keys("Emoji_Part3"));

        // 어느 쪽이든 규칙(처음과 끝에 키)이 그대로 선다.
        Assert.Empty(MotionValidator.Validate(editor.Document));

        // 채널이 없는 층·0초는 아무 일도 없다(기록 없음).
        revision = editor.Revision;
        Assert.False(editor.ShiftLayerKeys("in", "Emoji_Body", 0.1f));
        Assert.False(editor.ShiftLayerKeys("in", "Emoji_Part1", 0f));
        Assert.Equal(revision, editor.Revision);

        // 시차 — 순서대로 0.1초씩 더: 첫째는 그대로. 통째로 한 칸.
        editor.Undo();
        editor.Undo();
        editor.Undo();
        revision = editor.Revision;
        Assert.True(editor.StaggerLayers("in", new[] { "Emoji_Part1", "Emoji_Part2", "Emoji_Part3" }, 0.1f));
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
        editor.AddTransition(TransitionTemplates.Emoji(parts: 3));
        editor.AddTimeline("in", 1f, TransitionTemplates.Emoji());
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

        // 처음부터(끝까지) 움직이거나 움직임이 없는 층은 할 일이 없다(기록 없음).
        revision = editor.Revision;
        Assert.False(editor.HideUntilTurn("in", "Emoji_Part2"));
        Assert.False(editor.HideAfterTurn("in", "Emoji_Part2"));
        Assert.False(editor.HideUntilTurn("in", "Emoji_Part3"));
        Assert.Equal(revision, editor.Revision);

        // 전부 — 움직이는 층 가운데 늦게 나오는 것만, 한 칸. 규칙에 맞는다.
        editor.Undo();
        editor.Undo();
        revision = editor.Revision;
        Assert.Equal(1, editor.HideAllUntilTurn("in"));
        Assert.Equal(revision + 1, editor.Revision);
        Assert.Null(editor.Document.Find("in")!.FindChannel("Emoji_Part2", MotionChannelKind.Alpha));
        Assert.Empty(MotionValidator.Validate(editor.Document));
    }

    // ── 전환 이름 바꾸기 ───────────────────────────────────────────────

    private static KeyAsset Lin(float t, float v) => new() { T = t, V = v, Tangent = TangentMode.Linear };

    [Fact]
    public void 전환의_이름을_바꾸면_타임라인의_이름도_함께_바뀐다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddTransition(new TransitionAsset { Name = "star" });
        editor.AddLayer("star", "Sun");
        editor.SetDuration("star", 1f);
        editor.AddChannel("star", "Sun", MotionChannelKind.Position);
        editor.SetKeys("star", "Sun", MotionChannelKind.Position, Axis.Y, new[] { Lin(0f, -100f), Lin(1f, 0f) });
        editor.AddTransition(new TransitionAsset { Name = "town" });

        // 한 칸 — 타임라인은 전환의 것이라 이름이 함께 간다.
        long revision = editor.Revision;
        editor.RenameTransition("star", "sol");
        Assert.Equal(revision + 1, editor.Revision);
        Assert.Null(editor.Document.FindTransition("star"));
        Assert.Null(editor.Document.Find("star"));
        Assert.Equal(("sol", "sol"), (editor.Document.Find("sol")!.Name, editor.Document.Find("sol")!.Name));
        Assert.Single(editor.Document.Find("sol")!.Channels);

        // 막는 것: 있는 이름 · 규칙에 안 맞는 이름(대문자 · 숫자로 시작). 같은 이름은 기록이 없다.
        revision = editor.Revision;
        Assert.Throws<InvalidOperationException>(() => editor.RenameTransition("sol", "town"));
        Assert.Throws<InvalidOperationException>(() => editor.RenameTransition("sol", "1sol"));
        Assert.Throws<InvalidOperationException>(() => editor.RenameTransition("sol", "Sol"));
        editor.RenameTransition("sol", "sol");
        Assert.Equal(revision, editor.Revision);

        // 되돌리면 통째로 돌아간다.
        editor.Undo();
        Assert.Equal("star", editor.Document.Find("star")!.Name);
    }

    // ── 화면 밖에서 들어온다 ───────────────────────────────────────────

    [Fact]
    public void 화면_밖에서_들어오는_거리는_층의_사각형이_무대_밖으로_다_나가는_만큼이다()
    {
        var stage = new Vec2(1920f, 1080f);
        var editor = new MotionEditor(new MotionDocument());
        editor.AddTransition(new TransitionAsset { Name = "town", Layers = { new LayerAsset { Id = "Group", X = 100f, Y = -50f, Alpha = true } } });
        editor.AddLayer("town", "Sun");
        editor.UpdateLayer("town", "Sun", new LayerAsset { X = 300f, Y = 200f, Width = 100f, Height = 100f, Image = "art/sun.png", Alpha = true });
        editor.AddLayer("town", "Leaf", "Group");
        editor.UpdateLayer("town", "Leaf", new LayerAsset { X = 10f, Y = 20f, Width = 60f, Height = 80f, Rotation = 30f, Image = "art/leaf.png", Alpha = true });
        editor.SetDuration("town", 1f);

        // 왼쪽에서: 무대의 반(960) + 자리(300) + 너비의 반(50) = 1310만큼 왼쪽에서 출발한다. 0.2초부터 0.5초 동안 — 앞뒤로는 가만히. 한 칸.
        long revision = editor.Revision;
        Assert.Equal(-1310f, editor.SlideFromOutside("town", "Sun", StageSide.Left, 0.2f, 0.5f, stage));
        Assert.Equal(revision + 1, editor.Revision);
        Assert.Equal(new[] { (0f, -1310f), (0.2f, -1310f), (0.7f, 0f), (1f, 0f) }, editor.Document.Find("town")!.FindChannel("Sun", MotionChannelKind.Position)!.X!.Keys.Select(k => (k.T, k.V)));

        // 위에서: 540 − 200 + 50 = 390. 나가는 것은 거꾸로(제자리 → 밖), 끝까지면 끝 키 하나로.
        Assert.Equal(390f, editor.SlideFromOutside("town", "Sun", StageSide.Top, 0.6f, 0.5f, stage, leaving: true));
        Assert.Equal(new[] { (0f, 0f), (0.6f, 0f), (1f, 390f) }, editor.Document.Find("town")!.FindChannel("Sun", MotionChannelKind.Position)!.Y!.Keys.Select(k => (k.T, k.V)));

        // 부모의 자리를 더하고, 돈 층은 감싸는 원으로 넉넉히: 960 − (100 + 10) + √(30² + 40²) = 900.
        Assert.Equal(900f, editor.SlideFromOutside("town", "Leaf", StageSide.Right, 0f, 0.5f, stage));

        // 길이 밖의 시작 · 없는 층은 막힌다.
        revision = editor.Revision;
        Assert.Throws<InvalidOperationException>(() => editor.SlideFromOutside("town", "Sun", StageSide.Left, 1f, 0.5f, stage));
        Assert.Throws<KeyNotFoundException>(() => editor.SlideFromOutside("town", "Nope", StageSide.Left, 0f, 0.5f, stage));
        Assert.Equal(revision, editor.Revision);
        Assert.Empty(MotionValidator.Validate(editor.Document));
    }
}
