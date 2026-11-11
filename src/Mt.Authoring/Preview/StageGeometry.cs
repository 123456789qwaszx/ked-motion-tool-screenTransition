using Ked.Presentation.Core;

namespace Mt.Authoring.Preview;

/// <summary>루트 공간(가운데 원점·y 위)의 네 모서리. 회전·배율이 섞여도 그대로 든다 — 축 정렬 경계가 아니다.</summary>
public readonly record struct Quad(Vec2 BottomLeft, Vec2 BottomRight, Vec2 TopRight, Vec2 TopLeft)
{
    public Vec2 Center => (BottomLeft + TopRight) * 0.5f;
}

/// <summary>무대 위 층의 기하 — 층 rect의 네 모서리, 축, 루트 공간 ↔ 무대 캔버스.</summary>
public static class StageGeometry
{
    /// <summary>층 rect의 네 모서리(루트 공간). 원점은 pivot.</summary>
    public static Quad RectQuad(RectNodeTree tree, string key)
    {
        ArgumentNullException.ThrowIfNull(tree);
        RectNodeState state = tree.GetState(key);
        Vec2 size = tree.GetRectSize(key);
        float left = -state.Pivot.X * size.X;
        float bottom = -state.Pivot.Y * size.Y;
        return Transform(tree, key, left, bottom, size.X, size.Y);
    }

    /// <summary>층의 pivot(루트 공간)과 그 로컬 x·y 축 끝 — 선택 층의 축 표시에 쓴다.</summary>
    public static (Vec2 Origin, Vec2 XEnd, Vec2 YEnd) Axes(RectNodeTree tree, string key, float length)
    {
        ArgumentNullException.ThrowIfNull(tree);
        return (
            tree.TransformPoint(key, Vec3.Zero).XY,
            tree.TransformPoint(key, new Vec3(length, 0f, 0f)).XY,
            tree.TransformPoint(key, new Vec3(0f, length, 0f)).XY);
    }

    /// <summary>루트 공간(가운데 원점·y 위) → 무대 캔버스(좌상 원점·y 아래, 무대 px).</summary>
    public static Vec2 ToCanvas(Vec2 root, Vec2 stageSize)
        => new(stageSize.X / 2f + root.X, stageSize.Y / 2f - root.Y);

    private static Quad Transform(RectNodeTree tree, string key, float left, float bottom, float width, float height)
    {
        return new Quad(
            tree.TransformPoint(key, new Vec3(left, bottom, 0f)).XY,
            tree.TransformPoint(key, new Vec3(left + width, bottom, 0f)).XY,
            tree.TransformPoint(key, new Vec3(left + width, bottom + height, 0f)).XY,
            tree.TransformPoint(key, new Vec3(left, bottom + height, 0f)).XY);
    }
}
