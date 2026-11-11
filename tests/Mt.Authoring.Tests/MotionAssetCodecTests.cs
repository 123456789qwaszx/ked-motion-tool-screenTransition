using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Tests;

public class MotionAssetCodecTests
{
    [Fact]
    public void 키가_있는_성분은_전부_옮긴다_전_구간_중립값인_상수도()
    {
        // 클립은 mode를 모른다 — 상수 1이 "무변"(relative)인지 "1로"(absolute)인지 정할 수 없으니 그대로 싣는다.
        var scale = new ChannelAsset
        {
            Node = "N", Kind = MotionChannelKind.Scale,
            X = Curve(Fixtures.Key(0f, 1f), Fixtures.Key(1f, 1f)),
            Y = Curve(Fixtures.Key(0f, 1f), Fixtures.Key(1f, 1.2f)),
        };
        var euler = new ChannelAsset
        {
            Node = "N", Kind = MotionChannelKind.Euler,
            X = Curve(Fixtures.Key(0f, 0f), Fixtures.Key(1f, 0f)),
        };
        TimelineAsset asset = new TransitionAsset { Name = "a" }.Timeline;
        asset.Channels.Add(scale);
        asset.Channels.Add(euler);

        MotionClip clip = TimelineCodec.ToClip(asset);

        Assert.NotNull(clip.Channels[0].X);
        Assert.NotNull(clip.Channels[0].Y);
        Assert.Null(clip.Channels[0].Z);
        Assert.NotNull(clip.Channels[1].X);
        Assert.Equal(1f, clip.Channels[0].X![1].Value);
    }

    private static ComponentCurve Curve(params KeyAsset[] keys) => new() { Keys = keys.ToList() };
}
