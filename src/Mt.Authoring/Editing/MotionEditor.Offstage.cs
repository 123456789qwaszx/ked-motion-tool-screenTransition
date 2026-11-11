using Ked.Presentation.Core;
using Mt.Authoring.Model;

namespace Mt.Authoring.Editing;

/// <summary>무대의 어느 쪽 밖인가.</summary>
public enum StageSide
{
    Left,
    Right,
    Top,
    Bottom,
}

// 화면 밖에서 들어오고 나간다. "얼마나 옮겨야 화면 밖인가"(−760 같은 수)를 손으로 적지 않게 한다.
public sealed partial class MotionEditor
{
    /// <summary>
    /// 그 오브젝트가 무대의 <paramref name="side"/> 밖에서 제자리로 **들어오게**(또는 <paramref name="leaving"/>이면 제자리에서 밖으로 **나가게**) 자리 커브를 놓는다 —
    /// <paramref name="start"/>초부터 <paramref name="seconds"/>초 동안, 그 앞뒤로는 가만히. 그 축의 자리 커브를 갈아 치운다(한 칸). 옮기는 거리를 돌려준다.
    /// 거리는 가만히 선 자세의 사각형이 무대 밖으로 다 나가는 만큼이다(돌았거나 커진 오브젝트는 넉넉히 — 감싸는 원으로 친다).
    /// 부모들이 돌거나 커지지 않았다고 본다(자리는 부모 안의 값이다).
    /// </summary>
    public float SlideFromOutside(string motionName, string node, StageSide side, float start, float seconds, Vec2 stageSize, bool leaving = false)
    {
        MotionAsset motion = Require(motionName);
        RigAsset rig = Document.FindRig(motion.Rig) ?? throw new InvalidOperationException($"'{motion.Rig}'는 툴에서 정의한 세트가 아니다 — 가만히 선 자리를 알 수 없다.");
        RigNodeAsset target = rig.Find(node) ?? throw new KeyNotFoundException($"오브젝트 '{node}'가 세트 '{rig.Kind}'에 없다.");

        if (!float.IsFinite(start) || !float.IsFinite(seconds) || seconds <= 0f || start < 0f || start >= motion.Duration - MotionRules.Tolerance)
        {
            throw new InvalidOperationException($"시작은 이 애니메이션의 길이({motion.Duration:0.###}초) 안, 걸리는 시간은 0보다 큰 수다.");
        }

        // 무대 가운데에서 본 자리 — 부모들의 자리를 더한다.
        float x = 0f;
        float y = 0f;

        for (RigNodeAsset? at = target; at is not null; at = at.Parent.Length > 0 ? rig.Find(at.Parent) : null)
        {
            x += at.X;
            y += at.Y;
        }

        // 사각형의 반 — 돌았으면 감싸는 원의 반지름으로(어느 쪽으로 나가도 다 가려지게).
        float halfW = target.Width * Math.Abs(target.ScaleX) / 2f;
        float halfH = target.Height * Math.Abs(target.ScaleY) / 2f;

        if (Math.Abs(target.Rotation) > 1e-3f)
        {
            halfW = halfH = MathF.Sqrt(halfW * halfW + halfH * halfH);
        }

        bool horizontal = side is StageSide.Left or StageSide.Right;
        float distance = side switch
        {
            StageSide.Left => -(stageSize.X / 2f + x + halfW),
            StageSide.Right => stageSize.X / 2f - x + halfW,
            StageSide.Bottom => -(stageSize.Y / 2f + y + halfH),
            _ => stageSize.Y / 2f - y + halfH,
        };
        distance = MathF.Round(distance);

        // 키는 "제자리에서 얼마나".
        float end = Math.Min(start + seconds, motion.Duration);
        float from = leaving ? 0f : distance;
        float to = leaving ? distance : 0f;
        var keys = new List<KeyAsset>();

        if (start > MotionRules.Tolerance)
        {
            keys.Add(Flat(0f, from));
        }

        keys.Add(Flat(start, from));
        keys.Add(Flat(end, to));

        if (end < motion.Duration - MotionRules.Tolerance)
        {
            keys.Add(Flat(motion.Duration, to));
        }

        Batch(MotionChangeKind.Keys, () =>
        {
            if (motion.FindChannel(node, MotionChannelKind.Position) is null)
            {
                AddChannel(motionName, node, MotionChannelKind.Position);
            }

            SetKeys(motionName, node, MotionChannelKind.Position, horizontal ? Axis.X : Axis.Y, keys);
        });

        return distance;
    }
}
