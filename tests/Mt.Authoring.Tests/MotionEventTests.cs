using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Tests;

/// <summary>이벤트 키(<c>PLAN-parts.md</c> §2.5, <c>motions/3</c>) — "이 프레임에 이모지·소리".</summary>
public class MotionEventTests
{
    private static readonly RigSchemaFile RigFile = RigSchemaFile.Load(Fixtures.PathOf("rig-schemas.json"));

    private static MotionEditor NewEditor() => new(Fixtures.NodBowDocument()); // nod 0.6초 · bow 0.4초

    [Fact]
    public void 이벤트는_시각_순으로_서고_명령_하나가_한_칸이다()
    {
        MotionEditor editor = NewEditor();
        Assert.DoesNotContain("events", MotionDocumentJson.Write(editor.Document), StringComparison.Ordinal);

        Assert.Equal(0, editor.AddMotionEvent("nod", 0.3f, "emoji", "땀"));
        Assert.Equal(0, editor.AddMotionEvent("nod", 0.1f, " sfx ", "pop")); // 앞에 끼어든다, 이름의 빈칸은 버린다
        Assert.Equal(2, editor.AddMotionEvent("nod", 0.3f, "emoji_off"));     // 같은 시각이면 먼저 있던 것 뒤
        Assert.Equal(0, editor.AddMotionEvent("nod", -5f, "start"));          // 0 아래로 못 간다
        List<MotionEventAsset> events = editor.Document.Find("nod")!.Events;
        Assert.Equal(new[] { ("start", 0f), ("sfx", 0.1f), ("emoji", 0.3f), ("emoji_off", 0.3f) }, events.Select(e => (e.Name, e.T)));
        Assert.Null(events[3].Value);

        // 고치기: 시각이 바뀌면 자리가 옮겨지고 새 번호가 온다.
        long revision = editor.Revision;
        Assert.Equal(3, editor.UpdateMotionEvent("nod", 1, 0.5f, "sfx", "ding"));
        Assert.Equal(revision + 1, editor.Revision);
        Assert.Equal(("sfx", 0.5f, "ding"), (events[3].Name, events[3].T, events[3].Value));
        editor.Undo();
        Assert.Equal(("sfx", 0.1f, "pop"), (editor.Document.Find("nod")!.Events[1].Name, editor.Document.Find("nod")!.Events[1].T, editor.Document.Find("nod")!.Events[1].Value));

        editor.RemoveMotionEvent("nod", 0);
        Assert.Equal(3, editor.Document.Find("nod")!.Events.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.RemoveMotionEvent("nod", 9));
        Assert.Throws<KeyNotFoundException>(() => editor.AddMotionEvent("nope", 0f, "x"));

        // 저작본 왕복.
        string json = MotionDocumentJson.Write(editor.Document);
        Assert.Equal(json, MotionDocumentJson.Write(MotionDocumentJson.Read(json)));
        Assert.Equal(1, json.Split("\"events\"").Length - 1); // bow에는 없다
    }

    [Fact]
    public void 이벤트가_있으면_motions_3으로_나가고_한글_값도_왕복한다()
    {
        MotionEditor editor = NewEditor();
        string plain = MotionsJson.Write(MotionExport.ToClips(editor.Document));
        Assert.Equal(Fixtures.TextOf("motions.nod-bow.json"), plain); // 이벤트가 없으면 전과 같은 글자

        editor.AddMotionEvent("nod", 0.3f, "emoji", "땀 \"큰\"");
        editor.AddMotionEvent("nod", 0.6f, "done");
        string json = MotionsJson.Write(MotionExport.ToClips(editor.Document));
        Assert.Contains("\"schema\": \"motions/3\"", json, StringComparison.Ordinal);
        Assert.Contains("{ \"t\": 0.3, \"name\": \"emoji\", \"value\": \"땀 \\\"큰\\\"\" }", json, StringComparison.Ordinal);
        Assert.Contains("{ \"t\": 0.6, \"name\": \"done\" }", json, StringComparison.Ordinal);

        Assert.True(MotionsJson.TryRead(json, out List<MotionClip> clips, out List<string> problems), string.Join("\n", problems));
        Assert.Empty(problems);
        MotionClip nod = clips.Single(c => c.Name == "nod");
        Assert.Equal(new[] { ("emoji", "땀 \"큰\""), ("done", (string?)null) }, nod.Events.Select(e => (e.Name, (string?)e.Value)));
        Assert.Null(clips.Single(c => c.Name == "bow").Events);
        Assert.Equal(json, MotionsJson.Write(clips));

        // 가져오면 저작본의 이벤트가 된다.
        MotionAsset back = MotionAssetCodec.FromClip(nod);
        Assert.Equal(new[] { (0.3f, "emoji"), (0.6f, "done") }, back.Events.Select(e => (e.T, e.Name)));
    }

    [Fact]
    public void 규칙은_이름과_시각을_본다()
    {
        MotionEditor editor = NewEditor();
        editor.AddMotionEvent("nod", 0.3f, "emoji", "땀");
        Assert.Empty(MotionValidator.Validate(editor.Document, RigFile.NodeIds));

        editor.AddMotionEvent("nod", 0.9f, "late"); // 길이(0.6) 밖
        Assert.Contains(MotionValidator.Validate(editor.Document, RigFile.NodeIds), p => p.Blocking && p.Message.Contains("0~길이 밖", StringComparison.Ordinal));
        editor.Undo();

        editor.AddMotionEvent("nod", 0.2f, "Bad Name");
        Assert.Contains(MotionValidator.Validate(editor.Document, RigFile.NodeIds), p => p.Blocking && p.Message.Contains("이벤트 0: 이름", StringComparison.Ordinal));
        Assert.False(MotionExport.TryExport(editor.Document, RigFile.NodeIds, out _, out _));
    }

    [Fact]
    public void 구간은_열린_앞과_닫힌_뒤라_같은_이벤트가_두_번_잡히지_않는다()
    {
        MotionEditor editor = NewEditor();
        editor.AddMotionEvent("nod", 0.3f, "emoji", "땀");
        MotionAsset nod = editor.Document.Find("nod")!;

        Assert.Single(MotionEvents.Between(nod, 0.0, 0.3));
        Assert.Empty(MotionEvents.Between(nod, 0.3, 0.6));
        Assert.Empty(MotionEvents.Between(nod, -1, 0.2));
    }
}
