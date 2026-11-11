using Mt.Authoring.Model;

namespace Mt.Authoring.Preview;

/// <summary>이벤트 키가 울리는 구간 계산.</summary>
public static class MotionEvents
{
    /// <summary>구간 끝의 여유 — 이벤트 시각의 float 잡음(1.3이 1.3000001로 나오는 것)이 구간을 넘나들지 않게.</summary>
    private const double Epsilon = 1e-6;

    /// <summary>모션 하나에서 그 구간 (after, until]에 울리는 이벤트 키들.</summary>
    public static List<MotionEventAsset> Between(TimelineAsset motion, double after, double until)
    {
        ArgumentNullException.ThrowIfNull(motion);
        return motion.Events.Where(e => e.T > after + Epsilon && e.T <= until + Epsilon).ToList();
    }
}
