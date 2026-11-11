using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Preview;

/// <summary>
/// 시각 t의 포즈 — 세트 트리(세트로 세운 시작 상태)에 모션 채널을 얹은 것.
///
/// 수학은 전부 코어다: 시작값 <see cref="MotionMath.StartOf"/>, 합성 <see cref="MotionMath.Compose"/>,
/// 종점 <see cref="MotionMath.EndValue"/>. 그래서 뷰포트가 보여 주는 포즈 = 재생하는 포즈다.
/// 키는 가만히 선 자세에 대한 값이고, 길이를 지나면 마지막 키가 남는다.
/// </summary>
public static class MotionPreview
{
    /// <param name="isMuted">뮤트된 성분(재생에서 뺀다 — 시작값 그대로). null이면 없음.</param>
    /// <param name="alphas">
    /// 노드 키 → 불투명도(없는 키는 1). 알파 채널은 트리가 아니라 이 표에 얹힌다 — 불투명도는 <c>RectNodeState</c>에 없다(코어도 따로 든다).
    /// 받은 표를 **그 자리에서 고친다**. null이면 알파 채널은 건너뛴다.
    /// </param>
    public static RectNodeTree Pose(RectNodeTree template, TimelineAsset motion, double seconds, Func<Graph.CurveRef, bool>? isMuted = null, IDictionary<string, float>? alphas = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(motion);

        RectNodeTree tree = template.Clone();
        MotionClip clip = TimelineCodec.ToClip(motion);

        MotionChannel[] channels = clip.Channels ?? Array.Empty<MotionChannel>();

        if (isMuted is not null)
        {
            for (int i = 0; i < channels.Length; i++)
            {
                MotionChannel ch = channels[i];

                if (isMuted(new Graph.CurveRef(ch.Node, ch.Kind, Axis.X)))
                {
                    ch.X = null;
                }

                if (isMuted(new Graph.CurveRef(ch.Node, ch.Kind, Axis.Y)))
                {
                    ch.Y = null;
                }

                if (isMuted(new Graph.CurveRef(ch.Node, ch.Kind, Axis.Z)))
                {
                    ch.Z = null;
                }

                channels[i] = ch;
            }
        }
        // 길이를 지나면 마지막 키가 남는다(키에서 바로 읽는다 — 커브의 끝 평가와 같다).
        bool ended = seconds > motion.Duration + 1e-6;
        float t = (float)Math.Max(0, seconds);

        foreach (MotionChannel channel in channels)
        {
            // 세트에 없는 노드는 건너뛴다 — 검증이 이미 소리를 낸다. 여기서 던지면 편집 중 화면이 죽는다.
            string key = channel.Node;

            if (string.IsNullOrEmpty(channel.Node) || !tree.Contains(key))
            {
                continue;
            }

            if (!channel.HasX && !channel.HasY && !channel.HasZ)
            {
                continue;
            }

            RectNodeState state = tree.GetState(key);

            if (channel.Kind == MotionChannelKind.Alpha)
            {
                if (alphas is not null)
                {
                    float from = alphas.TryGetValue(key, out float current) ? current : 1f;
                    Vec3 a0 = MotionMath.StartOf(channel, state, from);
                    Vec3 a = ended ? MotionMath.EndValue(channel, a0) : MotionMath.Compose(channel, a0, t);
                    alphas[key] = Math.Clamp(a.X, 0f, 1f);
                }

                continue;
            }

            Vec3 start = MotionMath.StartOf(channel, state);
            Vec3 value = ended ? MotionMath.EndValue(channel, start) : MotionMath.Compose(channel, start, t);
            tree.SetState(key, Apply(channel.Kind, state, value));
        }

        return tree;
    }

    /// <summary>채널 종류가 노드 상태의 어느 자리를 쓰는가.</summary>
    public static RectNodeState Apply(MotionChannelKind kind, in RectNodeState state, Vec3 value) => kind switch
    {
        MotionChannelKind.Position => state.WithAnchoredPosition(value.XY),
        MotionChannelKind.Scale => state.WithLocalScale(new Vec3(value.X, value.Y, state.LocalScale.Z)),
        MotionChannelKind.Euler => state.WithLocalEuler(value),
        MotionChannelKind.Alpha => state, // 불투명도는 노드 상태에 없다 — Pose의 alphas 표로 간다
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "모르는 채널 종류"),
    };
}
