using Ked.Presentation.Core;

namespace Mt.Authoring.Model;

/// <summary>
/// 저작본 ↔ 코어 클립. 수학·검증은 코어 것이므로 여기서는 모양만 바꾼다.
///
/// 키가 있는 성분은 전부 옮긴다 — 전 구간 중립값인 성분도 빼지 않는다(저작본과 클립이 같은 모양이게).
/// </summary>
public static class MotionAssetCodec
{
    public static MotionClip ToClip(MotionAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        var channels = new MotionChannel[asset.Channels.Count];

        for (int i = 0; i < channels.Length; i++)
        {
            ChannelAsset c = asset.Channels[i];

            channels[i] = new MotionChannel
            {
                Node = c.Node,
                Kind = c.Kind,
                X = ToKeys(c.X),
                Y = ToKeys(c.Y),
                Z = ToKeys(c.Z),
            };
        }

        return new MotionClip
        {
            Name = asset.Name,
            Rig = asset.Rig,
            Duration = asset.Duration,
            Channels = channels,
            Events = asset.Events.Count == 0
                ? null
                : asset.Events.Select(e => new MotionEvent { Time = e.T, Name = e.Name, Value = string.IsNullOrEmpty(e.Value) ? null : e.Value }).ToArray(),
        };
    }

    private static CurveKey[]? ToKeys(ComponentCurve? curve)
    {
        if (curve is null || curve.Keys.Count == 0)
        {
            return null;
        }

        var keys = new CurveKey[curve.Keys.Count];

        for (int i = 0; i < keys.Length; i++)
        {
            keys[i] = curve.Keys[i].ToCurveKey();
        }

        return keys;
    }
}
