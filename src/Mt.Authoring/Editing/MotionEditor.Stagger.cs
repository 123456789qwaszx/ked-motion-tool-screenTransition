using Ked.Presentation.Core;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;

namespace Mt.Authoring.Editing;

// 시차 — 층의 움직임을 통째로 늦추거나 당긴다. 층 여럿이 차례로 들어오게 할 때 키를 하나씩 옮기지 않는다.
public sealed partial class MotionEditor
{
    /// <summary>
    /// 그 층의 커브들을 <paramref name="delta"/>초만큼 민다(+ = 늦게, − = 일찍). 길이는 그대로다:
    /// 밀려서 비는 쪽은 그 끝의 값으로 가만히 있고(처음 값 · 마지막 값), 길이 밖으로 나가는 쪽은 잘린다(잘린 자리의 값이 끝의 키가 된다).
    /// 그래서 "처음에 키 · 끝에 키" 규칙이 그대로 선다. 그 층의 채널이 없거나 민 것이 없으면 기록이 없다.
    /// </summary>
    public bool ShiftLayerKeys(string motionName, string node, float delta)
    {
        ArgumentNullException.ThrowIfNull(node);
        TimelineAsset motion = Require(motionName);

        if (!float.IsFinite(delta))
        {
            throw new InvalidOperationException("미는 시간은 수여야 한다.");
        }

        var changes = new List<(ChannelAsset Channel, Axis Axis, List<KeyAsset> Keys)>();

        foreach (ChannelAsset channel in motion.Channels.Where(c => c.Node == node))
        {
            foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
            {
                if (channel.Get(axis) is { Keys.Count: > 0 } curve && Shifted(curve.Keys, delta, motion.Duration) is { } keys)
                {
                    changes.Add((channel, axis, keys));
                }
            }
        }

        if (changes.Count == 0)
        {
            return false;
        }

        Batch(MotionChangeKind.Keys, () =>
        {
            foreach ((ChannelAsset channel, Axis axis, List<KeyAsset> keys) in changes)
            {
                SetKeys(motionName, channel.Node, channel.Kind, axis, keys);
            }
        });
        return true;
    }

    /// <summary>
    /// 시차 — 층들을 순서대로 <paramref name="step"/>초씩 더 민다: 첫째는 그대로, 둘째는 step, 셋째는 2·step… 한 칸.
    /// </summary>
    public bool StaggerLayers(string motionName, IReadOnlyList<string> nodes, float step)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        bool any = false;
        Batch(MotionChangeKind.Keys, () =>
        {
            for (int i = 1; i < nodes.Count; i++)
            {
                any |= ShiftLayerKeys(motionName, nodes[i], step * i);
            }
        });
        return any;
    }

    /// <summary>커브 하나를 민 키들. 바뀌는 것이 없으면(모든 값이 같아 밀어도 같다 등) null.</summary>
    private static List<KeyAsset>? Shifted(List<KeyAsset> keys, float delta, float duration)
    {
        if (Math.Abs(delta) < 1e-6f || keys.Count == 0 || duration <= 0f)
        {
            return null;
        }

        CurveKey[] core = TangentMath.ToCurveKeys(keys);
        float At(float t) => CurveFunctions.Evaluate(core, Math.Clamp(t, 0f, duration));
        const float eps = 1e-4f;
        var result = new List<KeyAsset>();

        // 처음: 늦추면 원래의 처음 값으로 가만히, 당기면 잘린 자리(원래의 −delta 초)의 값.
        result.Add(new KeyAsset { T = 0f, V = delta > 0 ? keys[0].V : At(-delta), Tangent = delta > 0 ? TangentMode.Flat : TangentMode.Auto });

        for (int i = 0; i < keys.Count; i++)
        {
            KeyAsset key = keys[i];

            // 이미 가만히 있던 구간의 끝 키는 옮기지 않는다 — 새 처음(끝) 키가 그 값을 들고 있어 군더더기가 된다.
            if ((delta > 0 && i == 0 && keys.Count > 1 && Math.Abs(keys[1].V - key.V) < eps)
                || (delta < 0 && i == keys.Count - 1 && keys.Count > 1 && Math.Abs(keys[^2].V - key.V) < eps))
            {
                continue;
            }

            float t = key.T + delta;

            if (t > eps && t < duration - eps)
            {
                result.Add(new KeyAsset { T = t, V = key.V, InTangent = key.InTangent, OutTangent = key.OutTangent, Tangent = key.Tangent, Broken = key.Broken });
            }
        }

        // 끝: 당기면 원래의 마지막 값으로 가만히, 늦추면 잘린 자리(원래의 길이 − delta 초)의 값.
        result.Add(new KeyAsset { T = duration, V = delta < 0 ? keys[^1].V : At(duration - delta), Tangent = delta < 0 ? TangentMode.Flat : TangentMode.Auto });

        bool same = result.Count == keys.Count && result.Zip(keys).All(p => Math.Abs(p.First.T - p.Second.T) < eps && Math.Abs(p.First.V - p.Second.V) < eps);
        return same ? null : result;
    }
}
