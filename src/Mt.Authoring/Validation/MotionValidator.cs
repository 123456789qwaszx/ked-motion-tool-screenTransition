using Ked.Presentation.Core;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;

namespace Mt.Authoring.Validation;

/// <summary>
/// 검증 문제 하나. 모션 이름이 비어 있으면 문서 수준의 문제다.
/// <see cref="Blocking"/>이 false면 **경고**다 — 내보내기를 막지 않는다(규칙이 아니라 "튄다"는 알림).
/// </summary>
public sealed record MotionProblem(string MotionName, string Message, bool Blocking = true)
{
    public override string ToString()
        => string.IsNullOrEmpty(MotionName) ? Message : $"'{MotionName}': {Message}";
}

/// <summary>
/// 편집 중 계속 돌리는 검증. 규칙은 코어 <see cref="MotionRules"/> 한 벌뿐이다 — 런타임 로더와 Unity 내보내기가
/// 같은 규칙을 쓰므로 "툴에서는 되는데 재생에서 사라지는" 모션이 안 생긴다. 문구도 코어의 것을 그대로 보인다.
///
/// 여기서 더 보는 것은 알파 채널이 불투명도를 받는 노드에 놓였는가다.
/// 키의 **값**은 알리지 않는다 — 첫 키가 중립값이 아닌 것("제자리 +140에서 시작")도 작가의 뜻이다.
/// </summary>
public static class MotionValidator
{
    public static IReadOnlyList<MotionProblem> Validate(MotionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Rigs.SelectMany(ValidateTimeline).ToList();
    }

    /// <summary>세트의 타임라인 — 채널의 노드는 그 세트에 있어야 하고, 알파 채널은 불투명도를 받는 노드에만 놓인다.</summary>
    public static IReadOnlyList<MotionProblem> ValidateTimeline(RigAsset set)
    {
        ArgumentNullException.ThrowIfNull(set);
        MotionAsset motion = set.Timeline;
        var problems = new List<MotionProblem>();
        var nodeIds = new HashSet<string>(set.Nodes.Select(n => n.Id), StringComparer.Ordinal);
        var alphaNodes = set.Nodes.Where(n => n.Alpha).Select(n => n.Id).ToList();

        MotionClip clip = MotionAssetCodec.ToClip(motion);

        if (!MotionRules.TryValidate(clip, nodeIds, out string reason))
        {
            problems.Add(new MotionProblem(motion.Name, reason));
        }

        // 알파 채널은 불투명도를 받을 수 있는 노드에만 — 호스트가 쓸 CanvasGroup이 없으면 재생에서 아무 일도 안 일어난다.
        for (int i = 0; i < motion.Channels.Count; i++)
        {
            if (motion.Channels[i].Kind == MotionChannelKind.Alpha && nodeIds.Contains(motion.Channels[i].Node) && !alphaNodes.Contains(motion.Channels[i].Node))
            {
                problems.Add(new MotionProblem(
                    motion.Name,
                    $"채널 {i}: '{motion.Channels[i].Node}'는 불투명도를 받을 수 없는 노드다. 받을 수 있는 노드: [{string.Join(", ", alphaNodes)}]"));
            }
        }

        return problems;
    }

    /// <summary>내보내기를 막는 것만.</summary>
    public static IEnumerable<MotionProblem> Blocking(IEnumerable<MotionProblem> problems)
        => problems.Where(p => p.Blocking);
}
