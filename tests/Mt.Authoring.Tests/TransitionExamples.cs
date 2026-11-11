using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Import;

namespace Mt.Authoring.Tests;

/// <summary>
/// 예시 문서 <c>examples/transitions/station/station.transition-authoring.json</c> — 화면 전환 하나를 세트와 타임라인 하나로 지은 것.
/// 층 표(<c>art/layers.tsv</c>)를 세트로 들이고, 등장·머묾·퇴장을 사본 세트에서 따로 짓고(<see cref="Section"/>) 한 타임라인으로 잇는다(<see cref="Join"/>). 편집기의 명령으로만 짓는다.
/// </summary>
internal static class TransitionExamples
{
    public const string Station = "station";
    public static readonly Vec2 Stage = new(1920f, 1080f);

    private static KeyAsset K(float t, float v, TangentMode tangent = TangentMode.Auto) => new() { T = t, V = v, Tangent = tangent };

    private static KeyAsset F(float t, float v) => K(t, v, TangentMode.Flat);

    private static KeyAsset L(float t, float v) => K(t, v, TangentMode.Linear);

    private static void Curve(MotionEditor editor, string motion, string node, MotionChannelKind kind, Axis axis, params KeyAsset[] keys)
    {
        if (editor.Document.Find(motion)!.Channels.All(c => c.Node != node || c.Kind != kind))
        {
            editor.AddChannel(motion, node, kind);
        }

        editor.SetKeys(motion, node, kind, axis, keys);
    }

    private static void Scale(MotionEditor editor, string motion, string node, params KeyAsset[] keys)
    {
        Curve(editor, motion, node, MotionChannelKind.Scale, Axis.X, keys);
        Curve(editor, motion, node, MotionChannelKind.Scale, Axis.Y, keys.Select(k => K(k.T, k.V, k.Tangent)).ToArray());
    }

    /// <summary>길이 안에서 [from, to] 동안 a → b, 그 앞뒤로는 가만히 — 층마다 시차를 두고 들어오게.</summary>
    private static KeyAsset[] Ramp(float duration, float from, float to, float a, float b)
    {
        var keys = new List<KeyAsset>();

        if (from > 0f)
        {
            keys.Add(F(0f, a));
        }

        keys.Add(F(from, a));
        keys.Add(F(to, b));

        if (to < duration)
        {
            keys.Add(F(duration, b));
        }

        return keys.ToArray();
    }

    /// <summary>
    /// 작은 움직임(0초부터 적은 키)을 길이 <paramref name="duration"/>짜리 애니메이션의 <paramref name="at"/>초에 놓는다 —
    /// 그 앞에는 첫 값이, 뒤에는 끝 값이 이어진다(모든 커브는 0초와 길이의 끝에 키가 있어야 한다).
    /// </summary>
    private static KeyAsset[] At(float duration, float at, params KeyAsset[] keys)
    {
        var placed = new List<KeyAsset>();

        if (at > 0f)
        {
            placed.Add(F(0f, keys[0].V));
        }

        placed.AddRange(keys.Select(k => K(k.T + at, k.V, k.Tangent)));

        if (placed[^1].T < duration - 1e-4f)
        {
            placed.Add(F(duration, keys[^1].V));
        }

        return placed.ToArray();
    }

    /// <summary>한 바퀴의 키(처음과 끝의 값이 같다)를 <paramref name="times"/>번 이어 놓는다.</summary>
    private static KeyAsset[] Loop(int times, params KeyAsset[] cycle)
    {
        float period = cycle[^1].T;
        var keys = new List<KeyAsset> { cycle[0] };

        for (int i = 0; i < times; i++)
        {
            keys.AddRange(cycle.Skip(1).Select(k => K(k.T + i * period, k.V, k.Tangent)));
        }

        return keys.ToArray();
    }

    public static MotionEditor BuildStation(string layersPath)
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddTransition(new TransitionAsset { Name = Station });
        editor.ImportLayers(Station, LayerSheet.Load(layersPath), "art", Stage);


        // ── 등장(1.2초) — 바탕색이 먼저 화면을 덮고("covered"에서 대본이 장면을 바꾼다), 층들이 시차를 두고 제자리로 들어온다.
        // 전부 상대값이다: 가만히 선 자세(배치 모드에서 놓은 자리)에서 얼마나 떨어져 있는가. 끝에서 0이라 제자리에 선다.
        const string In = "station_in";
        const float D = 1.2f;
        Section(editor, Station, In, D);
        Curve(editor, In, MotionEditor.BackdropNode, MotionChannelKind.Alpha, Axis.X, L(0f, 0f), L(0.25f, 1f), L(D, 1f));
        editor.AddMotionEvent(In, 0.25f, "covered");

        Scale(editor, In, "arches", Ramp(D, 0.15f, 0.75f, 0.55f, 1f));
        Curve(editor, In, "arches", MotionChannelKind.Alpha, Axis.X, Ramp(D, 0.15f, 0.45f, 0f, 1f));
        Curve(editor, In, "floor", MotionChannelKind.Position, Axis.Y, Ramp(D, 0.2f, 0.7f, -520f, 0f));
        Curve(editor, In, "pillars_left", MotionChannelKind.Position, Axis.X, Ramp(D, 0.3f, 0.9f, -820f, 0f));
        Curve(editor, In, "pillars_right", MotionChannelKind.Position, Axis.X, Ramp(D, 0.3f, 0.9f, 820f, 0f));
        Curve(editor, In, "train", MotionChannelKind.Position, Axis.X, Ramp(D, 0.5f, 1.1f, -900f, 0f));
        Curve(editor, In, "sign", MotionChannelKind.Position, Axis.Y, F(0f, 360f), F(0.6f, 360f), K(0.95f, -18f), F(1.1f, 0f), F(D, 0f)); // 내려와 살짝 출렁
        Scale(editor, In, "light", Ramp(D, 0.5f, 1.15f, 0.3f, 1f));
        Curve(editor, In, "light", MotionChannelKind.Alpha, Axis.X, Ramp(D, 0.5f, 0.9f, 0f, 1f));

        (string Node, float At)[] microbes = { ("microbe_sun", 0.8f), ("microbe_leaf", 0.9f), ("microbe_cell", 1.0f) };

        foreach ((string node, float at) in microbes)
        {
            // 모든 커브는 길이의 끝에 키가 있어야 한다(코어 규칙) — 일찍 끝나는 층도 끝까지 값을 든다.
            var pop = new List<KeyAsset> { F(0f, 0f), F(at, 0f), K(at + 0.12f, 1.2f), F(at + 0.2f, 1f) };

            if (at + 0.2f < D - 1e-4f)
            {
                pop.Add(F(D, 1f));
            }

            Scale(editor, In, node, pop.ToArray());
            Curve(editor, In, node, MotionChannelKind.Alpha, Axis.X, Ramp(D, at, at + 0.1f, 0f, 1f));
        }

        // ── 머묾(2초, 되풀이) — 미생물이 떠다니고 끝의 빛이 숨쉰다.
        const string Hold = "station_hold";
        Section(editor, Station, Hold, 2f);
        Curve(editor, Hold, "microbe_sun", MotionChannelKind.Position, Axis.Y, K(0f, 0f), K(1f, 16f), K(2f, 0f));
        Curve(editor, Hold, "microbe_sun", MotionChannelKind.Euler, Axis.Z, K(0f, 0f), K(1f, 20f), K(2f, 0f));
        Curve(editor, Hold, "microbe_leaf", MotionChannelKind.Position, Axis.Y, K(0f, 0f), K(0.7f, -12f), K(1.4f, 8f), K(2f, 0f));
        Curve(editor, Hold, "microbe_leaf", MotionChannelKind.Euler, Axis.Z, K(0f, 0f), K(1f, -7f), K(2f, 0f));
        Curve(editor, Hold, "microbe_cell", MotionChannelKind.Position, Axis.X, K(0f, 0f), K(1f, 26f), K(2f, 0f));
        Scale(editor, Hold, "light", K(0f, 1f), K(1f, 1.07f), K(2f, 1f));

        // ── 퇴장(0.9초) — 층들이 들어온 길로 물러나고 바탕색이 걷힌다.
        const string Out = "station_out";
        const float E = 0.9f;
        Section(editor, Station, Out, E);

        foreach ((string node, _) in microbes)
        {
            Curve(editor, Out, node, MotionChannelKind.Alpha, Axis.X, Ramp(E, 0f, 0.2f, 1f, 0f));
        }

        Curve(editor, Out, "sign", MotionChannelKind.Position, Axis.Y, Ramp(E, 0f, 0.35f, 0f, 360f));
        Curve(editor, Out, "train", MotionChannelKind.Position, Axis.X, Ramp(E, 0.05f, 0.5f, 0f, -900f));
        Curve(editor, Out, "pillars_left", MotionChannelKind.Position, Axis.X, Ramp(E, 0.15f, 0.65f, 0f, -820f));
        Curve(editor, Out, "pillars_right", MotionChannelKind.Position, Axis.X, Ramp(E, 0.15f, 0.65f, 0f, 820f));
        Curve(editor, Out, "floor", MotionChannelKind.Position, Axis.Y, Ramp(E, 0.25f, 0.7f, 0f, -520f));
        Scale(editor, Out, "arches", Ramp(E, 0.2f, 0.8f, 1f, 1.7f));
        Curve(editor, Out, "arches", MotionChannelKind.Alpha, Axis.X, Ramp(E, 0.45f, 0.8f, 1f, 0f));
        Scale(editor, Out, "light", Ramp(E, 0.2f, 0.8f, 1f, 2.4f));
        Curve(editor, Out, "light", MotionChannelKind.Alpha, Axis.X, Ramp(E, 0.5f, 0.85f, 1f, 0f));
        Curve(editor, Out, MotionEditor.BackdropNode, MotionChannelKind.Alpha, Axis.X, L(0f, 1f), L(0.5f, 1f), L(E, 0f));

        // ── 연출 — 화면에 선다. 1초 머물고 걷힌다.
        Join(editor, Station, In, Hold, Out);
        return editor;
    }

    public const string Pillar = "pillar";

    /// <summary>
    /// 기울어진 기둥과 노을 — station과 다른 결로 짓는다: 층들을 **같은 타이밍으로 놓은 뒤 시차(<see cref="MotionEditor.StaggerNodes"/>)로 민다**.
    /// 지평선의 것들(노을 · 도시 둘 · 언덕)이 아래에서 차례로 솟고, 해가 떠오르고, 기둥이 넘어지듯 들어와 선다.
    /// </summary>
    public static MotionEditor BuildPillar(string layersPath)
    {
        MotionEditor editor = Begin(Pillar, layersPath);

        string[] horizon = { "haze", "city_far", "city_near", "hill" };
        string[] petals = { "petal_1", "petal_2", "petal_3" };

        // ── 등장(1.5초) ──
        const string In = "pillar_in";
        const float D = 1.5f;
        Section(editor, Pillar, In, D);
        Curve(editor, In, MotionEditor.BackdropNode, MotionChannelKind.Alpha, Axis.X, L(0f, 0f), L(0.25f, 1f), L(D, 1f));
        editor.AddMotionEvent(In, 0.25f, "covered");

        // 지평선의 층 넷 — 전부 0.2~0.7초에 아래에서 솟게 놓고, 시차로 0.12초씩 민다(하나씩 키를 옮기지 않는다).
        foreach (string layer in horizon)
        {
            Curve(editor, In, layer, MotionChannelKind.Position, Axis.Y, Ramp(D, 0.2f, 0.7f, -620f, 0f));
        }

        editor.StaggerNodes(In, horizon, 0.12f);

        Curve(editor, In, "clouds_far", MotionChannelKind.Position, Axis.X, Ramp(D, 0.15f, 1.0f, 520f, 0f));
        Curve(editor, In, "clouds_far", MotionChannelKind.Alpha, Axis.X, Ramp(D, 0.15f, 0.5f, 0f, 1f));
        Curve(editor, In, "clouds_near", MotionChannelKind.Position, Axis.X, Ramp(D, 0.25f, 1.1f, -640f, 0f));
        Curve(editor, In, "clouds_near", MotionChannelKind.Alpha, Axis.X, Ramp(D, 0.25f, 0.6f, 0f, 1f));

        // 해 — 0.6초에 지평선 아래에서 떠오르고 빛이 번진다. 제 차례 전에는 숨긴다(가만히 선 자세는 "다 보이는" 것이라).
        Curve(editor, In, "sun", MotionChannelKind.Position, Axis.Y, Ramp(D, 0.6f, 1.3f, -300f, 0f));
        editor.HideUntilTurn(In, "sun");
        Curve(editor, In, "sun_glow", MotionChannelKind.Alpha, Axis.X, Ramp(D, 0.7f, 1.4f, 0f, 1f));
        Scale(editor, In, "sun_glow", Ramp(D, 0.7f, 1.4f, 0.5f, 1f));

        // 기둥 — 왼쪽 밖에서 넘어지듯 들어와 살짝 넘쳤다가 선다.
        Curve(editor, In, "pillar", MotionChannelKind.Position, Axis.X, Ramp(D, 0.55f, 1.05f, -640f, 0f));
        Curve(editor, In, "pillar", MotionChannelKind.Euler, Axis.Z, F(0f, 28f), F(0.55f, 28f), K(1.05f, -4f), F(1.25f, 0f), F(D, 0f));

        // 꽃잎 — 같은 타이밍으로 놓고 시차로.
        foreach (string petal in petals)
        {
            Curve(editor, In, petal, MotionChannelKind.Alpha, Axis.X, Ramp(D, 1.0f, 1.2f, 0f, 1f));
            Curve(editor, In, petal, MotionChannelKind.Position, Axis.X, Ramp(D, 1.0f, 1.3f, 180f, 0f));
        }

        editor.StaggerNodes(In, petals, 0.08f);

        // ── 머묾(3초, 되풀이) — 꽃잎이 흔들리고 구름이 천천히 숨쉰다.
        const string Hold = "pillar_hold";
        Section(editor, Pillar, Hold, 3f);

        for (int i = 0; i < petals.Length; i++)
        {
            float sway = 26f + i * 10f;
            Curve(editor, Hold, petals[i], MotionChannelKind.Position, Axis.X, K(0f, 0f), K(1.5f, -sway), K(3f, 0f));
            Curve(editor, Hold, petals[i], MotionChannelKind.Position, Axis.Y, K(0f, 0f), K(0.75f, 14f), K(2.25f, -14f), K(3f, 0f));
            Curve(editor, Hold, petals[i], MotionChannelKind.Euler, Axis.Z, K(0f, 0f), K(1.5f, i % 2 == 0 ? 40f : -40f), K(3f, 0f));
        }

        Curve(editor, Hold, "clouds_near", MotionChannelKind.Position, Axis.X, K(0f, 0f), K(1.5f, 24f), K(3f, 0f));
        Scale(editor, Hold, "sun_glow", Loop(2, K(0f, 1f), K(0.75f, 1.08f), K(1.5f, 1f))); // 해의 빛이 두 번 숨쉰다

        // ── 퇴장(1초) — 해가 지고, 지평선이 가라앉고(시차 — 앞의 것부터), 기둥이 기울어 나가고, 바탕색이 걷힌다.
        const string Out = "pillar_out";
        const float E = 1f;
        Section(editor, Pillar, Out, E);

        foreach (string petal in petals)
        {
            Curve(editor, Out, petal, MotionChannelKind.Alpha, Axis.X, Ramp(E, 0f, 0.2f, 1f, 0f));
        }

        // 해가 진다 — 빛이 꺼지고 해가 가라앉으며 사라진다.
        Curve(editor, Out, "sun", MotionChannelKind.Position, Axis.Y, Ramp(E, 0f, 0.5f, 0f, -300f));
        Curve(editor, Out, "sun", MotionChannelKind.Alpha, Axis.X, Ramp(E, 0.3f, 0.5f, 1f, 0f));
        Curve(editor, Out, "sun_glow", MotionChannelKind.Alpha, Axis.X, Ramp(E, 0f, 0.4f, 1f, 0f));
        Curve(editor, Out, "pillar", MotionChannelKind.Position, Axis.X, Ramp(E, 0.1f, 0.6f, 0f, -640f));
        Curve(editor, Out, "pillar", MotionChannelKind.Euler, Axis.Z, Ramp(E, 0.1f, 0.6f, 0f, 28f));
        string[] sinking = { "hill", "city_near", "city_far", "haze" };

        foreach (string layer in sinking)
        {
            Curve(editor, Out, layer, MotionChannelKind.Position, Axis.Y, Ramp(E, 0.15f, 0.55f, 0f, -620f));
        }

        editor.StaggerNodes(Out, sinking, 0.08f);
        Curve(editor, Out, "clouds_far", MotionChannelKind.Alpha, Axis.X, Ramp(E, 0.3f, 0.7f, 1f, 0f));
        Curve(editor, Out, "clouds_near", MotionChannelKind.Alpha, Axis.X, Ramp(E, 0.3f, 0.7f, 1f, 0f));
        Curve(editor, Out, MotionEditor.BackdropNode, MotionChannelKind.Alpha, Axis.X, L(0f, 1f), L(0.55f, 1f), L(E, 0f));

        Join(editor, Pillar, In, Hold, Out);
        return editor;
    }

    // ── 나머지 세 장면 ──

    public const string Lapis = "lapis";
    public const string Heredity = "heredity";
    public const string Mercury = "mercury";

    private static MotionEditor Begin(string kind, string layersPath)
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddTransition(new TransitionAsset { Name = kind });
        editor.ImportLayers(kind, LayerSheet.Load(layersPath), "art", Stage);
        return editor;
    }

    /// <summary>등장의 머리 — 바탕색이 0.25초에 화면을 덮고 거기서 <c>covered</c>가 울린다.</summary>
    private static void Cover(MotionEditor editor, string motion, float duration)
    {
        Curve(editor, motion, MotionEditor.BackdropNode, MotionChannelKind.Alpha, Axis.X, L(0f, 0f), L(0.25f, 1f), L(duration, 1f));
        editor.AddMotionEvent(motion, 0.25f, "covered");
    }

    /// <summary>퇴장의 꼬리 — 바탕색이 끝에서 걷힌다.</summary>
    private static void Uncover(MotionEditor editor, string motion, float duration)
        => Curve(editor, motion, MotionEditor.BackdropNode, MotionChannelKind.Alpha, Axis.X, L(0f, 1f), L(duration * 0.55f, 1f), L(duration, 0f));

    private static void Fade(MotionEditor editor, string motion, string node, float duration, float from, float to, float a, float b)
        => Curve(editor, motion, node, MotionChannelKind.Alpha, Axis.X, Ramp(duration, from, to, a, b));

    private static void Slide(MotionEditor editor, string motion, string node, Axis axis, float duration, float from, float to, float a, float b)
        => Curve(editor, motion, node, MotionChannelKind.Position, axis, Ramp(duration, from, to, a, b));

    /// <summary>
    /// 구간 하나(등장 · 머묾 · 퇴장)를 지을 자리 — 그 세트의 사본(<paramref name="section"/>)을 만들어 거기 타임라인에 짓는다.
    /// 세트마다 타임라인은 하나라, 구간은 사본에서 짓고 <see cref="Join"/>이 본 세트의 타임라인으로 잇고 사본을 지운다.
    /// </summary>
    private static void Section(MotionEditor editor, string kind, string section, float duration)
    {
        editor.AddTransition(new TransitionAsset { Name = section, Layers = editor.Document.FindTransition(kind)!.Layers });
        editor.SetDuration(section, duration);
    }

    /// <summary>
    /// 등장 › 머묾(한 바퀴) › 퇴장을 그 세트의 타임라인 하나로 잇는다 — 셋 다 상대값이고, 머묾은 등장이 끝난 자세에서, 퇴장은 머묾이 끝난 자세에서 시작한다.
    /// 키를 굽지 않는다: 구간마다 시작값을 얹는다(위치·회전은 더하고 배율·불투명도는 곱한다 — 탄젠트도 같이). 구간이 만나는 키는 양쪽 탄젠트를 그대로 지닌다.
    /// 표지 — 등장의 <c>covered</c>(덮였다)는 그대로, 머묾의 끝에 <c>release</c>(걷기 시작)를 둔다.
    /// </summary>
    private static void Join(MotionEditor editor, string kind, string enter, string hold, string exit)
    {
        TimelineAsset[] parts = { editor.Document.Find(enter)!, editor.Document.Find(hold)!, editor.Document.Find(exit)! };
        editor.SetDuration(kind, parts.Sum(p => p.Duration));

        foreach (MotionEventAsset e in parts[0].Events)
        {
            editor.AddMotionEvent(kind, e.T, e.Name, e.Value);
        }

        editor.AddMotionEvent(kind, parts[0].Duration + parts[1].Duration, "release");

        foreach ((string node, MotionChannelKind channelKind) in parts.SelectMany(p => p.Channels).Select(c => (c.Node, c.Kind)).Distinct())
        {
            editor.AddChannel(kind, node, channelKind);
            bool multiply = channelKind is MotionChannelKind.Scale or MotionChannelKind.Alpha;
            float neutral = MotionMath.Neutral(channelKind);

            foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
            {
                if (parts.All(p => p.FindChannel(node, channelKind)?.Get(axis) is null))
                {
                    continue;
                }

                var keys = new List<KeyAsset>();
                float start = neutral;
                float offset = 0f;

                foreach (TimelineAsset part in parts)
                {
                    IReadOnlyList<KeyAsset> source = (IReadOnlyList<KeyAsset>?)part.FindChannel(node, channelKind)?.Get(axis)?.Keys
                        ?? new[] { F(0f, neutral), F(part.Duration, neutral) };
                    float factor = multiply ? start : 1f;
                    List<KeyAsset> placed = source.Select(k => new KeyAsset
                    {
                        T = k.T + offset,
                        V = multiply ? start * k.V : start + k.V,
                        InTangent = k.InTangent * factor,
                        OutTangent = k.OutTangent * factor,
                        Tangent = k.Tangent,
                        Broken = k.Broken,
                    }).ToList();

                    if (keys.Count > 0)
                    {
                        KeyAsset last = keys[^1];
                        KeyAsset first = placed[0];

                        if (Math.Abs(last.T - first.T) > 1e-4f || Math.Abs(last.V - first.V) > 1e-3f)
                        {
                            throw new InvalidOperationException($"{kind}: {node} {channelKind} {axis}가 {first.T}초에서 끊긴다");
                        }

                        keys[^1] = new KeyAsset { T = last.T, V = last.V, InTangent = last.InTangent, OutTangent = first.OutTangent, Tangent = TangentMode.Free, Broken = true };
                        placed.RemoveAt(0);
                    }

                    keys.AddRange(placed);
                    start = keys[^1].V;
                    offset += part.Duration;
                }

                editor.SetKeys(kind, node, channelKind, axis, keys);
            }
        }

        foreach (TimelineAsset part in parts)
        {
            editor.RemoveTransition(part.Name);
        }
    }

    /// <summary>
    /// 바닷속 기둥들 — 바닥과 기둥이 아래에서 차례로 솟고, 양옆의 나무줄기가 닫히듯 들어오고, 물고기 떼와 가오리가 헤엄쳐 들어온다.
    /// 빛나는 구슬이 톡 나타나 둥실거리다 사그라든다.
    /// </summary>
    public static MotionEditor BuildLapis(string layersPath)
    {
        MotionEditor editor = Begin(Lapis, layersPath);

        // ── 등장(1.4초)
        const string In = "lapis_in";
        const float D = 1.4f;
        Section(editor, Lapis, In, D);
        Cover(editor, In, D);
        Fade(editor, In, "light_band", D, 0.2f, 0.9f, 0f, 1f);
        string[] rising = { "seabed_far", "pillars_far", "pillars_mid", "seabed_near" };

        // "화면 밖에서 들어온다" — 얼마나 내려야 화면 밖인지는 편집기가 센다(층의 자리와 크기에서).
        foreach (string layer in rising)
        {
            editor.SlideFromOutside(In, layer, StageSide.Bottom, 0.2f, 0.55f, Stage);
        }

        editor.StaggerNodes(In, rising, 0.1f);
        editor.SlideFromOutside(In, "trunk_left", StageSide.Left, 0.5f, 0.5f, Stage);
        editor.SlideFromOutside(In, "trunk_right", StageSide.Right, 0.55f, 0.5f, Stage);
        editor.SlideFromOutside(In, "fish_school", StageSide.Right, 0.6f, 0.7f, Stage);
        editor.SlideFromOutside(In, "ray", StageSide.Left, 0.7f, 0.65f, Stage);
        Fade(editor, In, "stars", D, 0.8f, 1.3f, 0f, 1f);
        Scale(editor, In, "orb", At(D, 0.85f, K(0f, 0.2f), K(0.32f, 1.15f), F(0.5f, 1f))); // 톡 나타난다
        editor.HideUntilTurn(In, "orb");

        // ── 머묾(2초, 되풀이)
        const string Hold = "lapis_hold";
        Section(editor, Lapis, Hold, 2f);
        Curve(editor, Hold, "fish_school", MotionChannelKind.Position, Axis.X, K(0f, 0f), K(1f, -46f), K(2f, 0f));
        Curve(editor, Hold, "ray", MotionChannelKind.Position, Axis.Y, K(0f, 0f), K(1f, 20f), K(2f, 0f));
        Curve(editor, Hold, "stars", MotionChannelKind.Alpha, Axis.X, K(0f, 1f), K(1f, 0.55f), K(2f, 1f));
        Curve(editor, Hold, "orb", MotionChannelKind.Position, Axis.Y, K(0f, 0f), K(1f, 22f), K(2f, 0f)); // 둥실거린다
        Scale(editor, Hold, "orb", K(0f, 1f), K(1f, 1.06f), K(2f, 1f));

        // ── 퇴장(1초)
        const string Out = "lapis_out";
        const float E = 1f;
        Section(editor, Lapis, Out, E);
        Scale(editor, Out, "orb", At(E, 0f, F(0f, 1f), F(0.3f, 0.5f))); // 사그라든다
        Fade(editor, Out, "orb", E, 0f, 0.3f, 1f, 0f);
        Fade(editor, Out, "stars", E, 0f, 0.3f, 1f, 0f);
        Slide(editor, Out, "fish_school", Axis.X, E, 0f, 0.5f, 0f, -1900f);
        Slide(editor, Out, "ray", Axis.X, E, 0f, 0.5f, 0f, 1300f);
        Slide(editor, Out, "trunk_left", Axis.X, E, 0.1f, 0.55f, 0f, -420f);
        Slide(editor, Out, "trunk_right", Axis.X, E, 0.1f, 0.55f, 0f, 420f);
        string[] sinking = { "seabed_near", "pillars_mid", "pillars_far", "seabed_far" };

        foreach (string layer in sinking)
        {
            Slide(editor, Out, layer, Axis.Y, E, 0.15f, 0.55f, 0f, -760f);
        }

        editor.StaggerNodes(Out, sinking, 0.07f);
        Fade(editor, Out, "light_band", E, 0.3f, 0.7f, 1f, 0f);
        Uncover(editor, Out, E);
        Join(editor, Lapis, In, Hold, Out);
        return editor;
    }

    /// <summary>
    /// 달과 떠 있는 섬 — 달이 커지며 떠오르고 소용돌이가 돌아 들어오고, 섬들이 솟고 나뭇가지가 위에서 내려온다.
    /// 씨앗(구슬 + 그 안의 소용돌이)이 돌고 수정(수정 + 빛)이 반짝인다.
    /// </summary>
    public static MotionEditor BuildHeredity(string layersPath)
    {
        MotionEditor editor = Begin(Heredity, layersPath);

        // ── 등장(1.5초)
        const string In = "heredity_in";
        const float D = 1.5f;
        Section(editor, Heredity, In, D);
        Cover(editor, In, D);
        Fade(editor, In, "moon", D, 0.2f, 0.8f, 0f, 1f);
        Scale(editor, In, "moon", Ramp(D, 0.2f, 1.0f, 0.6f, 1f));
        Fade(editor, In, "spiral", D, 0.3f, 1.0f, 0f, 1f);
        Curve(editor, In, "spiral", MotionChannelKind.Euler, Axis.Z, Ramp(D, 0.3f, 1.2f, -40f, 0f));
        string[] islands = { "island", "islets" };

        foreach (string layer in islands)
        {
            Slide(editor, In, layer, Axis.Y, D, 0.35f, 0.9f, -760f, 0f);
        }

        editor.StaggerNodes(In, islands, 0.15f);
        Slide(editor, In, "canopy", Axis.Y, D, 0.5f, 1.0f, 520f, 0f);
        // 수정 — 0.65초에 아래에서 솟고 빛이 켜진다.
        Slide(editor, In, "crystals", Axis.Y, D, 0.65f, 1.1f, -260f, 0f);
        editor.HideUntilTurn(In, "crystals");
        Slide(editor, In, "crystals_light", Axis.Y, D, 0.65f, 1.1f, -260f, 0f);
        Fade(editor, In, "crystals_light", D, 0.95f, 1.25f, 0f, 1f);

        // 씨앗 — 0.8초에 구슬이 부풀고 소용돌이가 돌아 들어온다.
        string[] seed = { "orb", "orb_swirl" };
        Scale(editor, In, "orb", At(D, 0.8f, K(0f, 0.3f), K(0.4f, 1.1f), F(0.6f, 1f)));
        Scale(editor, In, "orb_swirl", At(D, 0.8f, F(0f, 0.3f), F(0.6f, 1f)));
        Curve(editor, In, "orb_swirl", MotionChannelKind.Euler, Axis.Z, At(D, 0.8f, F(0f, -160f), F(0.6f, 0f)));

        foreach (string part in seed)
        {
            editor.HideUntilTurn(In, part);
        }

        string[] leaves = { "leaf_1", "leaf_2", "leaf_3" };

        foreach (string leaf in leaves)
        {
            Fade(editor, In, leaf, D, 1.0f, 1.2f, 0f, 1f);
            Slide(editor, In, leaf, Axis.Y, D, 1.0f, 1.3f, 120f, 0f);
        }

        editor.StaggerNodes(In, leaves, 0.07f);

        // ── 머묾(3초, 되풀이) — 씨앗의 소용돌이가 두 바퀴 돌고 수정이 두 번 반짝인다.
        const string Hold = "heredity_hold";
        Section(editor, Heredity, Hold, 3f);
        // 한 방향으로 계속 돈다 — 되풀이 머묾(복원)의 끝에서 처음으로 돌아가도 한 바퀴의 배수라 이어져 보인다.
        Curve(editor, Hold, "orb_swirl", MotionChannelKind.Euler, Axis.Z, L(0f, 0f), L(3f, -720f));
        Scale(editor, Hold, "orb", Loop(2, K(0f, 1f), K(0.75f, 1.05f), K(1.5f, 1f)));
        Curve(editor, Hold, "crystals_light", MotionChannelKind.Alpha, Axis.X, Loop(2, K(0f, 1f), K(0.75f, 0.45f), K(1.5f, 1f)));

        for (int i = 0; i < leaves.Length; i++)
        {
            Curve(editor, Hold, leaves[i], MotionChannelKind.Position, Axis.X, K(0f, 0f), K(1.5f, 24f + i * 8f), K(3f, 0f));
            Curve(editor, Hold, leaves[i], MotionChannelKind.Euler, Axis.Z, K(0f, 0f), K(1.5f, i % 2 == 0 ? -30f : 30f), K(3f, 0f));
        }

        Curve(editor, Hold, "spiral", MotionChannelKind.Euler, Axis.Z, K(0f, 0f), K(1.5f, 6f), K(3f, 0f));

        // ── 퇴장(1초)
        const string Out = "heredity_out";
        const float E = 1f;
        Section(editor, Heredity, Out, E);

        foreach (string leaf in leaves)
        {
            Fade(editor, Out, leaf, E, 0f, 0.2f, 1f, 0f);
        }

        foreach (string part in seed)
        {
            Fade(editor, Out, part, E, 0f, 0.3f, 1f, 0f);
            Scale(editor, Out, part, Ramp(E, 0f, 0.3f, 1f, 0.5f));
        }

        Fade(editor, Out, "crystals", E, 0.05f, 0.35f, 1f, 0f);
        Fade(editor, Out, "crystals_light", E, 0.05f, 0.35f, 1f, 0f);
        Slide(editor, Out, "canopy", Axis.Y, E, 0.1f, 0.5f, 0f, 520f);
        Slide(editor, Out, "islets", Axis.Y, E, 0.15f, 0.55f, 0f, -760f);
        Slide(editor, Out, "island", Axis.Y, E, 0.22f, 0.62f, 0f, -760f);
        Fade(editor, Out, "spiral", E, 0.2f, 0.6f, 1f, 0f);
        Fade(editor, Out, "moon", E, 0.3f, 0.7f, 1f, 0f);
        Uncover(editor, Out, E);
        Join(editor, Heredity, In, Hold, Out);
        return editor;
    }

    /// <summary>
    /// 행성과 고래 — 대기가 번지고 행성이 아래에서 솟고, 고래 둘과 물고기 떼가 가로질러 들어온다.
    /// 별(빛 + 별)이 켜지고 깜빡이다 꺼진다.
    /// </summary>
    public static MotionEditor BuildMercury(string layersPath)
    {
        MotionEditor editor = Begin(Mercury, layersPath);

        // ── 등장(1.5초)
        const string In = "mercury_in";
        const float D = 1.5f;
        Section(editor, Mercury, In, D);
        Cover(editor, In, D);
        string[] air = { "atmosphere", "atmosphere_inner" };

        foreach (string layer in air)
        {
            Fade(editor, In, layer, D, 0.2f, 0.8f, 0f, 1f);
            Scale(editor, In, layer, Ramp(D, 0.2f, 0.9f, 1.2f, 1f));
        }

        editor.StaggerNodes(In, air, 0.12f);
        Slide(editor, In, "planet", Axis.Y, D, 0.3f, 0.95f, -820f, 0f);
        Slide(editor, In, "planet_bands", Axis.Y, D, 0.3f, 0.95f, -820f, 0f);
        Slide(editor, In, "clouds", Axis.X, D, 0.45f, 1.15f, -620f, 0f);
        Fade(editor, In, "clouds", D, 0.45f, 0.8f, 0f, 1f);
        Fade(editor, In, "stars", D, 0.6f, 1.2f, 0f, 1f);
        // 별 — 0.7초에 톡 켜지고 빛이 번진다.
        Scale(editor, In, "star", At(D, 0.7f, K(0f, 0.1f), K(0.35f, 1.25f), F(0.6f, 1f)));
        editor.HideUntilTurn(In, "star");
        Scale(editor, In, "star_glow", Ramp(D, 0.8f, 1.3f, 0.4f, 1f));
        Fade(editor, In, "star_glow", D, 0.8f, 1.3f, 0f, 1f);
        Slide(editor, In, "whale_big", Axis.X, D, 0.5f, 1.4f, 1700f, 0f);
        Slide(editor, In, "whale_small", Axis.X, D, 0.7f, 1.45f, 900f, 0f);
        Slide(editor, In, "fish_school", Axis.X, D, 0.8f, 1.45f, 900f, 0f);

        // ── 머묾(2.5초, 되풀이) — 고래가 느리게 헤엄치고 별이 두 번 깜빡인다.
        const string Hold = "mercury_hold";
        Section(editor, Mercury, Hold, 2.5f);
        Curve(editor, Hold, "whale_big", MotionChannelKind.Position, Axis.Y, K(0f, 0f), K(1.25f, 26f), K(2.5f, 0f));
        Curve(editor, Hold, "whale_big", MotionChannelKind.Euler, Axis.Z, K(0f, 0f), K(1.25f, -3f), K(2.5f, 0f));
        Curve(editor, Hold, "whale_small", MotionChannelKind.Position, Axis.Y, K(0f, 0f), K(1.25f, -18f), K(2.5f, 0f));
        Curve(editor, Hold, "fish_school", MotionChannelKind.Position, Axis.X, K(0f, 0f), K(1.25f, -36f), K(2.5f, 0f));
        Scale(editor, Hold, "star_glow", Loop(2, K(0f, 1f), K(0.6f, 1.12f), K(1.25f, 1f)));
        Curve(editor, Hold, "star", MotionChannelKind.Euler, Axis.Z, Loop(2, K(0f, 0f), K(0.6f, 20f), K(1.25f, 0f)));

        // ── 퇴장(1초)
        const string Out = "mercury_out";
        const float E = 1f;
        Section(editor, Mercury, Out, E);
        Scale(editor, Out, "star", At(E, 0f, F(0f, 1f), F(0.4f, 0.1f))); // 꺼진다
        Fade(editor, Out, "star_glow", E, 0f, 0.3f, 1f, 0f);
        Fade(editor, Out, "star", E, 0.2f, 0.4f, 1f, 0f);
        Slide(editor, Out, "whale_big", Axis.X, E, 0f, 0.6f, 0f, -1500f);
        Slide(editor, Out, "whale_small", Axis.X, E, 0f, 0.5f, 0f, -2000f);
        Slide(editor, Out, "fish_school", Axis.X, E, 0f, 0.45f, 0f, -1900f);
        Fade(editor, Out, "stars", E, 0.1f, 0.4f, 1f, 0f);
        Fade(editor, Out, "clouds", E, 0.15f, 0.5f, 1f, 0f);
        Slide(editor, Out, "planet", Axis.Y, E, 0.2f, 0.65f, 0f, -820f);
        Slide(editor, Out, "planet_bands", Axis.Y, E, 0.2f, 0.65f, 0f, -820f);
        Fade(editor, Out, "atmosphere_inner", E, 0.3f, 0.65f, 1f, 0f);
        Fade(editor, Out, "atmosphere", E, 0.35f, 0.7f, 1f, 0f);
        Uncover(editor, Out, E);
        Join(editor, Mercury, In, Hold, Out);
        return editor;
    }
}
