using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Editing;

// 제 차례 전에는 숨긴다. 가만히 선 자세는 "다 보이는" 것이라,
// 늦게 나오는 층은 제 차례 전까지 불투명도로 숨겨 둬야 한다 — 그 키를 손으로 놓지 않게 한다.
public sealed partial class MotionEditor
{
    /// <summary>나타나고 사라지는 데 걸리는 시간(초) — 딱 끊기지 않게.</summary>
    public const float TurnFade = 0.1f;

    /// <summary>
    /// 그 오브젝트가 **움직이기 시작할 때까지** 숨긴다 — 그 애니메이션의 불투명도 커브를 "0 … (제 차례) 0 → 1 … 1"로 놓는다(있던 불투명도 커브는 갈아 치운다).
    /// 제 차례 = 그 오브젝트의 다른 커브들이 처음으로 값이 바뀌기 시작하는 시각.
    /// 처음부터 움직이거나 움직임이 없으면 할 일이 없다(false).
    /// </summary>
    public bool HideUntilTurn(string motionName, string node)
    {
        TimelineAsset motion = Require(motionName);

        if (FirstChange(motion, node) is not { } start || start <= MotionRules.Tolerance)
        {
            return false;
        }

        float shown = Math.Min(start + TurnFade, motion.Duration);
        var keys = new List<KeyAsset> { Flat(0f, 0f), Flat(start, 0f), Flat(shown, 1f) };

        if (shown < motion.Duration - MotionRules.Tolerance)
        {
            keys.Add(Flat(motion.Duration, 1f));
        }

        SetAlphaCurve(motion, node, keys);
        return true;
    }

    /// <summary>
    /// 그 오브젝트가 **움직임을 마치면** 숨긴다 — 불투명도 커브를 "1 … (끝나기 직전) 1 → 0 … 0"으로. 퇴장에서 화면 밖으로 못 나가는 층을 치운다.
    /// 끝까지 움직이거나 움직임이 없으면 할 일이 없다(false).
    /// </summary>
    public bool HideAfterTurn(string motionName, string node)
    {
        TimelineAsset motion = Require(motionName);

        if (LastChange(motion, node) is not { } end || end >= motion.Duration - MotionRules.Tolerance)
        {
            return false;
        }

        float from = Math.Max(0f, end - TurnFade);
        var keys = new List<KeyAsset>();

        if (from > MotionRules.Tolerance)
        {
            keys.Add(Flat(0f, 1f));
        }

        keys.Add(Flat(from, 1f));
        keys.Add(Flat(end, 0f));
        keys.Add(Flat(motion.Duration, 0f));
        SetAlphaCurve(motion, node, keys);
        return true;
    }

    /// <summary>그 애니메이션이 움직이는 오브젝트 전부에 <see cref="HideUntilTurn"/> — 한 칸. 숨긴 오브젝트의 수.</summary>
    public int HideAllUntilTurn(string motionName)
    {
        TimelineAsset motion = Require(motionName);
        int count = 0;
        Batch(MotionChangeKind.Keys, () =>
        {
            foreach (string node in motion.Channels.Where(c => c.Kind != MotionChannelKind.Alpha).Select(c => c.Node).Distinct().ToList())
            {
                // 불투명도를 받을 수 있는 오브젝트만(세트의 표시를 본다).
                if (Document.FindTransition(motion.Name)?.Find(node) is { Alpha: false })
                {
                    continue;
                }

                if (HideUntilTurn(motionName, node))
                {
                    count++;
                }
            }
        });
        return count;
    }

    private static KeyAsset Flat(float t, float v) => new() { T = t, V = v, Tangent = TangentMode.Flat };

    private void SetAlphaCurve(TimelineAsset motion, string node, List<KeyAsset> keys)
    {
        Batch(MotionChangeKind.Keys, () =>
        {
            if (motion.FindChannel(node, MotionChannelKind.Alpha) is null)
            {
                AddChannel(motion.Name, node, MotionChannelKind.Alpha);
            }

            SetKeys(motion.Name, node, MotionChannelKind.Alpha, Axis.X, keys);
        });
    }

    private static IEnumerable<List<KeyAsset>> MovingCurves(TimelineAsset motion, string node)
        => motion.Channels.Where(c => c.Node == node && c.Kind != MotionChannelKind.Alpha)
            .SelectMany(c => new[] { c.Get(Axis.X), c.Get(Axis.Y), c.Get(Axis.Z) })
            .Where(curve => curve is { Keys.Count: > 1 })
            .Select(curve => curve!.Keys);

    /// <summary>그 오브젝트의 커브들이 처음으로 값이 바뀌기 시작하는 시각 — 움직임이 없으면 null.</summary>
    private static float? FirstChange(TimelineAsset motion, string node)
    {
        float? first = null;

        foreach (List<KeyAsset> keys in MovingCurves(motion, node))
        {
            for (int i = 0; i + 1 < keys.Count; i++)
            {
                if (Math.Abs(keys[i + 1].V - keys[i].V) > 1e-4f)
                {
                    first = first is { } f ? Math.Min(f, keys[i].T) : keys[i].T;
                    break;
                }
            }
        }

        return first;
    }

    /// <summary>그 오브젝트의 커브들이 마지막으로 값이 바뀌기를 마치는 시각 — 움직임이 없으면 null.</summary>
    private float? LastChange(TimelineAsset motion, string node)
    {
        float? last = null;

        foreach (List<KeyAsset> keys in MovingCurves(motion, node))
        {
            for (int i = keys.Count - 1; i > 0; i--)
            {
                if (Math.Abs(keys[i].V - keys[i - 1].V) > 1e-4f)
                {
                    last = last is { } l ? Math.Max(l, keys[i].T) : keys[i].T;
                    break;
                }
            }
        }

        return last;
    }
}
