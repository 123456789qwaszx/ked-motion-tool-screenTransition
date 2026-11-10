using Ked.Presentation.Core;

namespace Mt.Authoring.Transition;

/// <summary>층이 화면의 어디에 붙는가 — 화면 비율이 달라져도 그 자리(끝·모서리·가운데)에서의 거리를 지킨다.</summary>
public enum ScreenAnchor
{
    Center,
    Left,
    Right,
    Top,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>층의 크기가 화면을 어떻게 따라가는가.</summary>
public enum ScreenFit
{
    /// <summary>지은 크기 그대로(px).</summary>
    None,

    /// <summary>화면이 늘어난 만큼 가로·세로를 따로 늘린다 — 화면 전체짜리 바탕.</summary>
    Stretch,

    /// <summary>비율을 지킨 채, 가로·세로 중 더 많이 늘어난 쪽만큼 키운다 — 그림이 찌그러지지 않고 화면을 덮는다.</summary>
    Cover,
}

/// <summary>가운데가 원점이고 y가 위인 사각형(px) — 세트의 오브젝트와 같은 좌표다.</summary>
public readonly record struct ScreenRect(float X, float Y, float Width, float Height)
{
    public float Left => X - Width / 2f;
    public float Right => X + Width / 2f;
    public float Bottom => Y - Height / 2f;
    public float Top => Y + Height / 2f;
}

/// <summary>
/// 화면 비율 맞춤(<c>docs/PLAN-screenTransition.md</c> §4) — **방향을 보이는 코드**다(아직 툴에 물리지 않았다).
/// 층은 기준 화면(1920×1080)에서 짓는다. 실제 화면이 다른 크기·비율이면 층마다의 <see cref="ScreenAnchor"/> · <see cref="ScreenFit"/>로 자리를 다시 잡는다 —
/// "왼쪽 끝에 붙은 기둥"은 더 넓은 화면에서도 왼쪽 끝에 붙고, "화면 전체 바탕"은 늘어나 빈틈이 없다.
/// 같은 함수를 게임의 재생기가 써야 한다(툴의 미리보기와 한 벌) — 그래서 순수 함수로 둔다.
/// </summary>
public static class ScreenLayout
{
    /// <summary>기준 화면에서 지은 사각형을 실제 화면의 것으로.</summary>
    public static ScreenRect Place(ScreenRect authored, Vec2 reference, Vec2 screen, ScreenAnchor anchor, ScreenFit fit)
    {
        Vec2 from = AnchorPoint(reference, anchor);
        Vec2 to = AnchorPoint(screen, anchor);
        float sx = reference.X > 0f ? screen.X / reference.X : 1f;
        float sy = reference.Y > 0f ? screen.Y / reference.Y : 1f;

        (float scaleX, float scaleY) = fit switch
        {
            ScreenFit.Stretch => (sx, sy),
            ScreenFit.Cover => (MathF.Max(sx, sy), MathF.Max(sx, sy)),
            _ => (1f, 1f),
        };

        // 붙는 자리에서의 거리를 지킨다 — 크기를 키우면 그 거리도 같은 비율로 는다(붙는 자리를 중심으로 키운다).
        return new ScreenRect(
            to.X + (authored.X - from.X) * scaleX,
            to.Y + (authored.Y - from.Y) * scaleY,
            authored.Width * scaleX,
            authored.Height * scaleY);
    }

    /// <summary>화면의 그 자리(가운데 원점).</summary>
    public static Vec2 AnchorPoint(Vec2 screen, ScreenAnchor anchor)
    {
        float x = anchor switch
        {
            ScreenAnchor.Left or ScreenAnchor.TopLeft or ScreenAnchor.BottomLeft => -screen.X / 2f,
            ScreenAnchor.Right or ScreenAnchor.TopRight or ScreenAnchor.BottomRight => screen.X / 2f,
            _ => 0f,
        };
        float y = anchor switch
        {
            ScreenAnchor.Top or ScreenAnchor.TopLeft or ScreenAnchor.TopRight => screen.Y / 2f,
            ScreenAnchor.Bottom or ScreenAnchor.BottomLeft or ScreenAnchor.BottomRight => -screen.Y / 2f,
            _ => 0f,
        };
        return new Vec2(x, y);
    }
}
