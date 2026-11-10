using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Preview;

/// <summary>
/// 재생 블록을 펼쳐 굽는다(<c>PLAN-set.md</c> S10·S11) — 세트의 애니메이션이 "품은 세트의 애니메이션을 언제 튼다"를 담으면,
/// 그 애니메이션의 채널들을 경로 이름(<c>인스턴스.오브젝트</c>)으로 이 애니메이션에 들여 **한 덩어리 모션**으로 만든다.
/// 런타임은 지금 그대로다(모션 하나 재생) — 조립은 툴 안의 일이다.
///
/// 블록이 시작하기 전에는 앞에서 남은 상태 그대로, 유지(commit) 모션이 끝나면 그 자세가 남고 복원(restore) 모션이 끝나면 시작 전으로 돌아간다.
/// 되풀이는 같은 블록을 이어 놓은 것 — 유지 모션은 바퀴마다 쌓인다. 품은 애니메이션의 재생 기준(상대값/절대값)은 그 애니메이션의 것이고,
/// 구운 값은 바깥 애니메이션의 재생 기준으로 옮겨 적는다(가만히 선 자세를 더하거나 뺀다).
///
/// 커브는 끊길 수 없어(계단 키가 없다) 블록의 처음과 끝에서 값이 튀는 자리는 <see cref="Seam"/>초짜리 짧은 선으로 잇는다.
/// 바깥 애니메이션이 같은 경로에 제 키를 놓았으면 그 성분은 제 키가 이긴다(품은 것을 여기서만 덮어쓴다).
/// </summary>
public static class MotionFlatten
{
    /// <summary>값이 튀는 자리를 잇는 선의 길이(초).</summary>
    public const float Seam = 1f / 240f;

    private const int MaxDepth = 8;

    /// <summary>문서의 모든 애니메이션의 구운 것을 다시 만든다 — 재생 블록이 없는 것은 null. 편집기가 바뀔 때마다, 문서를 읽은 뒤 부른다.</summary>
    public static void Refresh(MotionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Motions.All(m => m.Plays.Count == 0))
        {
            foreach (MotionAsset motion in document.Motions)
            {
                motion.Flattened = null;
            }

            return;
        }

        var done = new Dictionary<string, MotionAsset?>(StringComparer.Ordinal);

        foreach (MotionAsset motion in document.Motions)
        {
            motion.Flattened = motion.Plays.Count == 0 ? null : Flatten(document, motion, done, new HashSet<string>(StringComparer.Ordinal), 0);
        }
    }

    /// <summary>그 애니메이션을 펼쳐 구운 것 — 재생 블록이 없으면 그 애니메이션 그대로.</summary>
    public static MotionAsset Flatten(MotionDocument document, MotionAsset motion)
        => motion.Plays.Count == 0 ? motion : Flatten(document, motion, new Dictionary<string, MotionAsset?>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal), 0) ?? motion;

    private static MotionAsset? Flatten(MotionDocument document, MotionAsset motion, Dictionary<string, MotionAsset?> done, HashSet<string> open, int depth)
    {
        if (done.TryGetValue(motion.Name, out MotionAsset? cached))
        {
            return cached;
        }

        if (depth > MaxDepth || !open.Add(motion.Name))
        {
            return null; // 순환 — 건너뛴다
        }

        var result = new MotionAsset
        {
            Name = motion.Name,
            Rig = motion.Rig,
            Duration = motion.Duration,
            End = motion.End,
            PreviewMode = motion.PreviewMode,
            Channels = motion.Channels.Select(CloneChannel).ToList(),
            Events = motion.Events.Select(e => new MotionEventAsset { T = e.T, Name = e.Name, Value = e.Value }).ToList(),
        };

        RigAsset? rig = document.FindRig(motion.Rig);
        // 대상 커브(경로 이름의 노드 · 종류 · 성분) → 그 위에 차례로 놓이는 조각들.
        var pieces = new Dictionary<(string Node, MotionChannelKind Kind, Axis Axis), List<Piece>>();

        foreach (MotionPlayAsset play in motion.Plays.OrderBy(p => p.At))
        {
            if (rig?.Find(play.Node) is not { Set: { Length: > 0 } inner } || document.Find(play.Motion) is not { } source || source.Rig != inner || source.Duration <= 0f)
            {
                continue; // 없는 인스턴스·애니메이션 — 검증이 알린다
            }

            MotionAsset child = source.Plays.Count == 0 ? source : Flatten(document, source, done, open, depth + 1) ?? source;
            string path = play.Node + RigAssetCodec.PathSeparator;

            for (int cycle = 0; cycle < Math.Max(1, play.Repeat); cycle++)
            {
                float start = play.At + cycle * child.Duration;

                foreach (ChannelAsset channel in child.Channels)
                {
                    foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
                    {
                        if (channel.Get(axis) is not { Keys.Count: > 0 } curve)
                        {
                            continue;
                        }

                        var key = (path + channel.Node, channel.Kind, axis);

                        if (!pieces.TryGetValue(key, out List<Piece>? list))
                        {
                            list = new List<Piece>();
                            pieces[key] = list;
                        }

                        list.Add(new Piece(start, child.Duration, curve.Keys, child.PreviewMode, child.End, RestOf(document, inner, channel.Node, channel.Kind, axis)));
                    }
                }

                foreach (MotionEventAsset e in child.Events)
                {
                    if (start + e.T <= motion.Duration + MotionRules.Tolerance)
                    {
                        result.Events.Add(new MotionEventAsset { T = Math.Min(start + e.T, motion.Duration), Name = e.Name, Value = e.Value });
                    }
                }
            }
        }

        foreach (((string node, MotionChannelKind kind, Axis axis), List<Piece> list) in pieces)
        {
            ChannelAsset? target = result.FindChannel(node, kind);

            if (target?.Get(axis) is not null)
            {
                continue; // 바깥이 이 성분에 제 키를 놓았다 — 그것이 이긴다
            }

            if (target is null)
            {
                target = new ChannelAsset { Node = node, Kind = kind };
                result.Channels.Add(target);
            }

            Set(target, axis, new ComponentCurve { Keys = Bake(list, kind, motion.Duration, motion.PreviewMode) });
        }

        result.Events = result.Events.OrderBy(e => e.T).ToList();
        open.Remove(motion.Name);
        done[motion.Name] = result;
        return result;
    }

    /// <summary>조각 하나 — 품은 애니메이션의 커브 하나가 놓인 자리.</summary>
    private sealed record Piece(float Start, float Duration, List<KeyAsset> Keys, MotionChannelMode Mode, MotionEnd End, float Rest);

    private static bool Multiplies(MotionChannelKind kind) => kind is MotionChannelKind.Scale or MotionChannelKind.Alpha;

    /// <summary>
    /// 조각들을 한 커브로. 안에서는 "가만히 선 자세에서 얼마나 떨어져 있는가"(상대값)로 잇는다 — 위치·회전은 더하고 배율·불투명도는 곱한다.
    /// 끝에서 바깥의 재생 기준으로 옮겨 적는다(절대값이면 가만히 선 자세를 다시 입힌다).
    /// </summary>
    private static List<KeyAsset> Bake(List<Piece> pieces, MotionChannelKind kind, float duration, MotionChannelMode outer)
    {
        bool multiply = Multiplies(kind);
        float neutral = multiply ? 1f : 0f;
        float state = neutral; // 앞에서 남은 상태(상대값)
        float rest = pieces[0].Rest;
        var keys = new List<KeyAsset>();

        // like가 없으면 "그대로 머무는" 키, 있으면 품은 애니메이션의 키. first는 조각의 첫 키 — 앞과는 평평하게 이어진다.
        void Add(float t, float v, KeyAsset? like = null, float factor = 1f, bool first = false)
        {
            t = Math.Clamp(t, 0f, duration);
            KeyAsset? previous = keys.Count > 0 ? keys[^1] : null;

            // 앞 키와 같은 자리·같은 값이면 한 키로 합친다(이어 붙은 바퀴·블록) — 들어오는 기울기는 앞의 것, 나가는 기울기는 뒤의 것.
            if (previous is not null && Math.Abs(t - previous.T) <= MotionRules.Tolerance && Math.Abs(v - previous.V) <= 1e-5f)
            {
                previous.OutTangent = like is null ? 0f : like.OutTangent * factor;
                previous.Tangent = TangentMode.Free;
                previous.Broken = true;
                return;
            }

            // 시각이 겹치거나 거꾸로 가면 조금 뒤로 민다(커브는 시각 오름차순이어야 한다).
            if (previous is not null && t <= previous.T + MotionRules.Tolerance)
            {
                t = previous.T + Seam;
            }

            if (t > duration + MotionRules.Tolerance)
            {
                return;
            }

            bool flatIn = like is null || first;

            if (flatIn && previous is not null)
            {
                // 머무는 구간·잇는 선은 곧게 — 앞 키가 나가는 기울기를 끊는다.
                previous.OutTangent = 0f;
                previous.Tangent = TangentMode.Free;
                previous.Broken = true;
            }

            keys.Add(like is null
                ? new KeyAsset { T = Math.Min(t, duration), V = v, Tangent = TangentMode.Free, Broken = true }
                : new KeyAsset { T = Math.Min(t, duration), V = v, InTangent = flatIn ? 0f : like.InTangent * factor, OutTangent = like.OutTangent * factor, Tangent = TangentMode.Free, Broken = true });
        }

        // 0초에서 시작하는 블록은 0초의 값이 그 블록의 것이다 — 남은 상태의 키를 먼저 놓으면 첫 프레임에 튄다.
        if (pieces.Min(p => p.Start) > MotionRules.Tolerance)
        {
            Add(0f, state);
        }

        foreach (Piece piece in pieces.OrderBy(p => p.Start))
        {
            float last = state;
            bool first = true;

            foreach (KeyAsset key in piece.Keys)
            {
                // 품은 애니메이션의 값을 상대값으로: 상대값이면 남은 상태에 얹고, 절대값이면 가만히 선 자세를 뺀다.
                float value;
                float factor;

                if (piece.Mode == MotionChannelMode.Relative)
                {
                    value = multiply ? state * key.V : state + key.V;
                    factor = multiply ? state : 1f;
                }
                else
                {
                    float r = multiply && Math.Abs(piece.Rest) < 1e-6f ? 1f : piece.Rest;
                    value = multiply ? key.V / r : key.V - r;
                    factor = multiply ? 1f / r : 1f;
                }

                // 블록이 남은 상태와 다른 값에서 시작하면, 직전까지는 남은 상태 그대로 두고 짧은 선으로 잇는다.
                if (first && keys.Count > 0 && Math.Abs(value - state) > 1e-5f && piece.Start - Seam > keys[^1].T + MotionRules.Tolerance)
                {
                    Add(piece.Start - Seam, state);
                }

                Add(piece.Start + key.T, value, key, factor, first);
                first = false;
                last = value;
            }

            // 유지면 그 자세가 남고, 복원이면 시작 전으로 돌아간다.
            if (piece.End == MotionEnd.Commit)
            {
                state = last;
            }
            else if (Math.Abs(last - state) > 1e-6f)
            {
                Add(piece.Start + piece.Duration + Seam, state);
            }
        }

        if (keys[^1].T < duration - MotionRules.Tolerance)
        {
            Add(duration, keys[^1].V);
        }
        else
        {
            keys[^1].T = duration;
        }

        // 바깥의 재생 기준으로 — 절대값이면 가만히 선 자세를 다시 입힌다.
        if (outer == MotionChannelMode.Absolute)
        {
            foreach (KeyAsset key in keys)
            {
                key.V = multiply ? key.V * rest : key.V + rest;

                if (multiply)
                {
                    key.InTangent *= rest;
                    key.OutTangent *= rest;
                }
            }
        }

        return keys;
    }

    /// <summary>품은 세트 안의 그 오브젝트의 가만히 선 값 — 경로(<c>a.b</c>)면 품은 세트를 따라 들어간다. 못 찾으면 중립값.</summary>
    private static float RestOf(MotionDocument document, string kind, string node, MotionChannelKind channel, Axis axis)
    {
        RigAsset? rig = document.FindRig(kind);
        string[] parts = node.Split(RigAssetCodec.PathSeparator);
        RigNodeAsset? found = null;

        for (int i = 0; i < parts.Length && rig is not null; i++)
        {
            found = rig.Find(parts[i]);
            rig = i < parts.Length - 1 && found?.Set is { Length: > 0 } inner ? document.FindRig(inner) : null;

            if (i < parts.Length - 1 && rig is null)
            {
                found = null;
            }
        }

        if (found is null)
        {
            return Multiplies(channel) ? 1f : 0f;
        }

        return channel switch
        {
            MotionChannelKind.Position => axis == Axis.X ? found.X : axis == Axis.Y ? found.Y : 0f,
            MotionChannelKind.Scale => axis == Axis.X ? found.ScaleX : axis == Axis.Y ? found.ScaleY : 1f,
            MotionChannelKind.Euler => axis == Axis.Z ? found.Rotation : 0f,
            _ => 1f,
        };
    }

    private static ChannelAsset CloneChannel(ChannelAsset c)
    {
        var copy = new ChannelAsset { Node = c.Node, Kind = c.Kind };

        foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
        {
            if (c.Get(axis) is { } curve)
            {
                Set(copy, axis, new ComponentCurve
                {
                    Keys = curve.Keys.Select(k => new KeyAsset { T = k.T, V = k.V, InTangent = k.InTangent, OutTangent = k.OutTangent, Tangent = k.Tangent, Broken = k.Broken }).ToList(),
                });
            }
        }

        return copy;
    }

    private static void Set(ChannelAsset channel, Axis axis, ComponentCurve curve)
    {
        switch (axis)
        {
            case Axis.X: channel.X = curve; break;
            case Axis.Y: channel.Y = curve; break;
            default: channel.Z = curve; break;
        }
    }
}
