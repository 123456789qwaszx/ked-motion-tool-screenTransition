using Ked.Presentation.Core;
using Mt.Authoring.Transition;
using Xunit;

namespace Mt.Authoring.Tests;

/// <summary>화면 전환 툴의 방향을 보이는 코드(<c>docs/PLAN-screenTransition.md</c> §4 · §5) — 화면 비율 맞춤과 덮임 검사.</summary>
public class TransitionDirectionTests
{
    private static readonly Vec2 Reference = new(1920f, 1080f);

    [Fact]
    public void 왼쪽_끝에_붙은_층은_더_넓은_화면에서도_왼쪽_끝에_붙는다()
    {
        // 기준 화면의 왼쪽 끝에 붙은 300×1080 기둥(가운데 x = −960 + 150).
        var pillar = new ScreenRect(-810f, 0f, 300f, 1080f);
        var wide = new Vec2(2560f, 1080f); // 21:9쯤

        ScreenRect placed = ScreenLayout.Place(pillar, Reference, wide, ScreenAnchor.Left, ScreenFit.None);
        Assert.Equal(-1280f, placed.Left, 3);          // 여전히 왼쪽 끝
        Assert.Equal((300f, 1080f), (placed.Width, placed.Height));

        // 가운데에 붙은 층은 그대로다. 오른쪽 아래에 붙은 층은 그 모서리에서의 거리를 지킨다.
        Assert.Equal(new ScreenRect(40f, 10f, 200f, 100f), ScreenLayout.Place(new ScreenRect(40f, 10f, 200f, 100f), Reference, wide, ScreenAnchor.Center, ScreenFit.None));
        ScreenRect corner = ScreenLayout.Place(new ScreenRect(900f, -500f, 100f, 60f), Reference, wide, ScreenAnchor.BottomRight, ScreenFit.None);
        Assert.Equal((1280f - 10f, -540f + 10f), (corner.Right, corner.Bottom));
    }

    [Fact]
    public void 화면_전체_바탕은_늘어나서_어느_비율에서도_빈틈이_없다()
    {
        var backdrop = new ScreenRect(0f, 0f, 1920f, 1080f);

        foreach (Vec2 screen in new[] { new Vec2(2560f, 1080f), new Vec2(1440f, 1080f), new Vec2(1080f, 1920f) })
        {
            // Stretch — 가로·세로를 따로 늘려 딱 맞는다. Cover — 비율을 지키고 넘치게 덮는다.
            ScreenRect stretched = ScreenLayout.Place(backdrop, Reference, screen, ScreenAnchor.Center, ScreenFit.Stretch);
            Assert.Equal((screen.X, screen.Y), (stretched.Width, stretched.Height));
            ScreenRect covering = ScreenLayout.Place(backdrop, Reference, screen, ScreenAnchor.Center, ScreenFit.Cover);
            Assert.True(covering.Width >= screen.X - 0.01f && covering.Height >= screen.Y - 0.01f);
            Assert.Equal(1920f / 1080f, covering.Width / covering.Height, 3);
            Assert.True(CoverCheck.Check(screen, new[] { CoverCheck.Corners(covering) }).IsCovered);

            // 안 늘린 바탕은 화면이 넓어지거나 높아지면 빈다.
            ScreenRect asIs = ScreenLayout.Place(backdrop, Reference, screen, ScreenAnchor.Center, ScreenFit.None);
            Assert.Equal(screen.X <= 1920f && screen.Y <= 1080f, CoverCheck.Check(screen, new[] { CoverCheck.Corners(asIs) }).IsCovered);
        }
    }

    [Fact]
    public void 덮임_검사는_안_덮인_곳을_돌려주고_층이_맞물려_덮으면_덮였다고_한다()
    {
        // 왼쪽 반 + 오른쪽 반 — 맞닿아 다 덮는다.
        Vec2[] leftHalf = CoverCheck.Corners(new ScreenRect(-480f, 0f, 960f, 1080f));
        Vec2[] rightHalf = CoverCheck.Corners(new ScreenRect(480f, 0f, 960f, 1080f));
        CoverResult whole = CoverCheck.Check(Reference, new[] { leftHalf, rightHalf });
        Assert.True(whole.IsCovered);
        Assert.Equal(1f, whole.Covered, 4);

        // 오른쪽 반을 100px 덜 덮으면 — 가운데에 100px 띠가 빈다. 그 자리와 넓이가 나온다.
        Vec2[] shortRight = CoverCheck.Corners(new ScreenRect(530f, 0f, 860f, 1080f));
        CoverResult gap = CoverCheck.Check(Reference, new[] { leftHalf, shortRight });
        Assert.False(gap.IsCovered);
        Assert.Equal(1f - 100f / 1920f, gap.Covered, 3);
        Assert.All(gap.Holes, hole => Assert.True(hole.Left >= -0.01f && hole.Right <= 100.01f));

        // 아무 층도 없으면 전부 빈다. 돌아간 층 하나가 화면을 넉넉히 품으면 덮인다.
        Assert.Equal(0f, CoverCheck.Check(Reference, Array.Empty<Vec2[]>()).Covered, 4);
        Vec2[] tilted = { new(-3000f, 0f), new(0f, -3000f), new(3000f, 0f), new(0f, 3000f) }; // 마름모
        Assert.True(CoverCheck.Check(Reference, new[] { tilted }).IsCovered);

        // 돌아간 층이 모서리를 못 덮으면 그 모서리가 빈다 — 덮였다고 잘못 말하지 않는다.
        Vec2[] small = { new(-1100f, 0f), new(0f, -1100f), new(1100f, 0f), new(0f, 1100f) };
        CoverResult corners = CoverCheck.Check(Reference, new[] { small });
        Assert.False(corners.IsCovered);
        Assert.Contains(corners.Holes, hole => hole.Left <= -900f && hole.Top >= 500f);
    }
}
