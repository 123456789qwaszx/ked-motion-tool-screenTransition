using System.Globalization;

namespace Mt.Authoring.Import;

/// <summary>층 하나 — 그림 파일과 무대 위의 자리(왼쪽 위 기준 px)·크기.</summary>
public sealed record LayerEntry(string File, float Left, float Top, float Width, float Height);

/// <summary>
/// 층 표(<c>layers.tsv</c>) — 포토샵의 레이어처럼 "어느 그림이 무대의 어디에 놓이는가"를 적은 글.
/// 그림 한 묶음을 전환으로 들일 때 층을 하나씩 놓지 않게 한다. 줄마다 <c>파일 ⇥ 왼쪽 ⇥ 위 ⇥ 너비 ⇥ 높이</c>(뒤에서 앞으로 — 뒤의 줄이 위에 그려진다),
/// <c>#</c>로 시작하는 줄은 설명이고 그 안에 <c>#RRGGBB</c>가 있으면 바탕색이다.
/// </summary>
public sealed class LayerSheet
{
    public string? Background { get; private init; }

    public IReadOnlyList<LayerEntry> Layers { get; private init; } = Array.Empty<LayerEntry>();

    public static LayerSheet Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Parse(File.ReadAllText(path));
    }

    public static LayerSheet Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string? background = null;
        var layers = new List<LayerEntry>();
        int number = 0;

        foreach (string raw in text.Split('\n'))
        {
            number++;
            string line = raw.TrimEnd('\r').Trim();

            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith('#'))
            {
                // 설명 줄의 #RRGGBB — 바탕색(첫 것만).
                background ??= System.Text.RegularExpressions.Regex.Match(line, "#[0-9A-Fa-f]{6}(?![0-9A-Fa-f])") is { Success: true } hit ? hit.Value.ToUpperInvariant() : null;
                continue;
            }

            string[] cells = line.Split('\t');

            if (cells.Length < 5 || !Number(cells[1], out float left) || !Number(cells[2], out float top) || !Number(cells[3], out float width) || !Number(cells[4], out float height)
                || width <= 0f || height <= 0f || cells[0].Trim().Length == 0)
            {
                throw new FormatException($"층 표 {number}번째 줄을 읽을 수 없다 — '파일 ⇥ 왼쪽 ⇥ 위 ⇥ 너비 ⇥ 높이'여야 한다: {line}");
            }

            layers.Add(new LayerEntry(cells[0].Trim(), left, top, width, height));
        }

        return new LayerSheet { Background = background, Layers = layers };
    }

    private static bool Number(string text, out float value)
        => float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
}
