using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Tests;

public class PoseEditTests
{
    private static readonly RigSchemaFile RigFile = RigTemplates.File();

    [Fact]
    public void 회전은_반시계_양수이고_감긴다_배율은_팔_방향_투영이라_가운데를_넘어도_뒤집히지_않는다()
    {
        var pivot = new Vec2(0f, 0f);
        Assert.Equal(90f, PoseEdit.AngleDelta(pivot, new Vec2(1f, 0f), new Vec2(0f, 1f)), 3);
        Assert.Equal(-90f, PoseEdit.AngleDelta(pivot, new Vec2(0f, 1f), new Vec2(1f, 0f)), 3);
        Assert.Equal(-170f, PoseEdit.AngleDelta(pivot, new Vec2(1f, 0f), new Vec2(-0.9848f, -0.1736f)), 1);

        var alongX = new Vec2(3f, 0f); // 방향만 본다(길이 무관)
        Assert.Equal(2f, PoseEdit.ScaleAlong(alongX, new Vec2(60f, 0f), new Vec2(120f, 0f), 60f), 4); // 팔 하나만큼 밖 = ×2
        Assert.Equal(0.5f, PoseEdit.ScaleAlong(alongX, new Vec2(60f, 0f), new Vec2(30f, 0f), 60f), 4); // 반 팔 안 = ×0.5
        Assert.Equal(0.01f, PoseEdit.ScaleAlong(alongX, new Vec2(60f, 0f), new Vec2(-60f, 0f), 60f), 4); // 가운데를 지나도 바닥에서 멈춘다
        Assert.Equal(1f, PoseEdit.ScaleAlong(alongX, new Vec2(60f, 0f), new Vec2(60f, 40f), 60f), 4); // 직각은 1
        Assert.Equal(1f, PoseEdit.ScaleAlong(Vec2.Zero, new Vec2(60f, 0f), new Vec2(0f, 0f), 60f), 4);
    }

    [Fact]
    public void 키_값은_가만히_선_자세에_대한_값이다()
    {
        Assert.Equal(-18f, PoseEdit.KeyValueFor(MotionChannelKind.Position, 0f, -18f));
        Assert.Equal(-23f, PoseEdit.KeyValueFor(MotionChannelKind.Position, 5f, -18f));
        Assert.Equal(1.5f, PoseEdit.KeyValueFor(MotionChannelKind.Scale, 2f, 3f));
        Assert.Equal(3f, PoseEdit.KeyValueFor(MotionChannelKind.Scale, 0f, 3f));
        Assert.Equal(8f, PoseEdit.KeyValueFor(MotionChannelKind.Euler, 2f, 10f));
        Assert.Equal(0.5f, PoseEdit.KeyValueFor(MotionChannelKind.Alpha, 0.8f, 0.4f));

        var state = new RectNodeState(new Vec2(1f, 2f), Vec2.Zero, Vec2.One, Vec2.Half, Vec2.Zero, new Vec3(3f, 4f, 5f), new Vec3(6f, 7f, 8f));
        Assert.Equal(2f, PoseEdit.Component(MotionChannelKind.Position, state, Axis.Y));
        Assert.Equal(4f, PoseEdit.Component(MotionChannelKind.Scale, state, Axis.Y));
        Assert.Equal(8f, PoseEdit.Component(MotionChannelKind.Euler, state, Axis.Z));
    }

    [Fact]
    public void 포즈_키는_채널과_성분을_만들고_같은_시각을_바꾸며_한_번의_조작이다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddMotion("a", "character", 1f);
        long before = editor.Revision;

        List<KeyRef> keys = editor.KeyComponents("a", "N", MotionChannelKind.Position, 0.5f, new[] { (Axis.X, 10f), (Axis.Y, 20f) }, TangentMode.Auto);

        Assert.Equal(before + 1, editor.Revision);
        Assert.Equal(2, keys.Count);
        ChannelAsset channel = editor.Document.Find("a")!.FindChannel("N", MotionChannelKind.Position)!;
        Assert.Equal(20f, channel.Y!.Keys[0].V);

        editor.KeyComponents("a", "N", MotionChannelKind.Position, 0.5f, new[] { (Axis.Y, 25f) }, TangentMode.Auto);
        Assert.Single(channel.Y.Keys);
        Assert.Equal(25f, editor.Document.Find("a")!.FindChannel("N", MotionChannelKind.Position)!.Y!.Keys[0].V);

        editor.Undo();
        editor.Undo();
        Assert.Null(editor.Document.Find("a")!.FindChannel("N", MotionChannelKind.Position));
    }
}
