using Ked.Presentation.Core;

namespace Mt.Authoring.Transition;

/// <summary>덮임 검사의 결과 — 안 덮인 곳들과, 화면에서 덮인 비율(0~1).</summary>
public sealed record CoverResult(IReadOnlyList<ScreenRect> Holes, float Covered)
{
    public bool IsCovered => Holes.Count == 0;
}

/// <summary>
/// 덮임 검사(<c>docs/PLAN-screenTransition.md</c> §5) — **방향을 보이는 코드**다(아직 툴에 물리지 않았다).
/// 화면 전환의 약속은 "다 덮인 순간 게임이 장면을 바꾼다"이다. 그 프레임에 불투명한 층들이 화면을 빈틈없이 덮는지를 눈으로 보지 않고 센다 —
/// 안 덮인 곳이 있으면 그 자리를 돌려준다(뷰포트가 빨갛게 칠한다).
///
/// 보수적으로 센다: **덮였다고 잘못 말하지 않는다**(안 덮였다고 할 수는 있다). 칸 하나는 "어느 한 층이 그 칸을 통째로 품을 때"만 덮인 것으로 친다 —
/// 돌아간 층 둘이 맞물려 덮는 칸은 못 알아볼 수 있다. 칸을 잘게 나눌수록 그 오차가 준다.
/// </summary>
public static class CoverCheck
{
    /// <summary>화면을 고르게 나누는 칸의 수(가로) — 층의 모서리 자리에 더해 이만큼 더 나눈다.</summary>
    public const int Columns = 64;

    /// <param name="screen">화면 크기(px). 가운데가 원점이다.</param>
    /// <param name="layers">불투명한 층들 — 볼록한 사각형의 네 꼭짓점(돌아가 있어도 된다). 반투명한 층은 넣지 않는다.</param>
    public static CoverResult Check(Vec2 screen, IReadOnlyList<Vec2[]> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        float left = -screen.X / 2f;
        float right = screen.X / 2f;
        float bottom = -screen.Y / 2f;
        float top = screen.Y / 2f;

        // 칸의 경계 — 고른 눈금 + 층의 꼭짓점 자리(곧게 놓인 층은 이것으로 정확히 맞는다).
        var xs = new SortedSet<float> { left, right };
        var ys = new SortedSet<float> { bottom, top };
        int rows = Math.Max(1, (int)MathF.Round(Columns * screen.Y / Math.Max(1f, screen.X)));

        for (int i = 1; i < Columns; i++)
        {
            xs.Add(left + screen.X * i / Columns);
        }

        for (int i = 1; i < rows; i++)
        {
            ys.Add(bottom + screen.Y * i / rows);
        }

        foreach (Vec2 corner in layers.SelectMany(l => l))
        {
            if (corner.X > left && corner.X < right)
            {
                xs.Add(corner.X);
            }

            if (corner.Y > bottom && corner.Y < top)
            {
                ys.Add(corner.Y);
            }
        }

        float[] xEdges = xs.ToArray();
        float[] yEdges = ys.ToArray();
        var holes = new List<ScreenRect>();
        float uncovered = 0f;

        for (int ix = 0; ix + 1 < xEdges.Length; ix++)
        {
            for (int iy = 0; iy + 1 < yEdges.Length; iy++)
            {
                float x0 = xEdges[ix];
                float x1 = xEdges[ix + 1];
                float y0 = yEdges[iy];
                float y1 = yEdges[iy + 1];
                Vec2[] cell = { new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1) };

                if (!layers.Any(layer => cell.All(point => Contains(layer, point))))
                {
                    holes.Add(new ScreenRect((x0 + x1) / 2f, (y0 + y1) / 2f, x1 - x0, y1 - y0));
                    uncovered += (x1 - x0) * (y1 - y0);
                }
            }
        }

        float area = screen.X * screen.Y;
        return new CoverResult(holes, area > 0f ? 1f - uncovered / area : 1f);
    }

    /// <summary>곧게 놓인 사각형의 네 꼭짓점.</summary>
    public static Vec2[] Corners(ScreenRect rect)
        => new[] { new Vec2(rect.Left, rect.Bottom), new Vec2(rect.Right, rect.Bottom), new Vec2(rect.Right, rect.Top), new Vec2(rect.Left, rect.Top) };

    /// <summary>볼록한 다각형이 그 점을 품는가(경계 포함) — 꼭짓점의 도는 방향은 상관없다.</summary>
    private static bool Contains(Vec2[] polygon, Vec2 point)
    {
        const float Slack = 1e-3f;
        bool positive = false;
        bool negative = false;

        for (int i = 0; i < polygon.Length; i++)
        {
            Vec2 a = polygon[i];
            Vec2 b = polygon[(i + 1) % polygon.Length];
            float cross = (b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X);
            positive |= cross > Slack;
            negative |= cross < -Slack;
        }

        return !(positive && negative);
    }
}
