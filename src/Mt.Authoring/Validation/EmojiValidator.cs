using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mt.Authoring.Model;

namespace Mt.Authoring.Validation;

/// <summary>이모지 정의의 문제 하나. <see cref="Blocking"/>이 false면 알림이다.</summary>
public sealed record EmojiProblem(string EmojiName, string Message, bool Blocking = true)
{
    public override string ToString() => $"이모지 '{EmojiName}': {Message}";
}

/// <summary>
/// 이모지 정의의 검증(<c>PLAN-parts.md</c> §8). 막는 것: 키·참조(스킨·모션)의 어긋남, 리그가 안 맞는 스킨·모션. 알리는 것: 그림 없음, 없는 자리 노드.
/// </summary>
public static class EmojiValidator
{
    public static IReadOnlyList<EmojiProblem> Validate(MotionDocument document, IReadOnlyDictionary<string, ICollection<string>>? rigNodeIds = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var problems = new List<EmojiProblem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (EmojiAsset emoji in document.Emojis)
        {
            void Block(string message) => problems.Add(new EmojiProblem(emoji.Name, message));
            void Notice(string message) => problems.Add(new EmojiProblem(emoji.Name, message, Blocking: false));

            if (string.IsNullOrEmpty(emoji.Name) || emoji.Name.Any(char.IsWhiteSpace))
            {
                Block("키는 빈칸 없는 글이어야 한다");
            }
            else if (!seen.Add(emoji.Name))
            {
                Block("같은 키가 둘이다");
            }

            ICollection<string>? nodes = null;

            if (rigNodeIds is not null && !rigNodeIds.TryGetValue(emoji.Rig ?? "", out nodes))
            {
                Block($"세트 '{emoji.Rig}'가 없다. 있는 것: [{string.Join(", ", rigNodeIds.Keys)}]");
            }

            if (emoji.Skin is null)
            {
                // 색 오브젝트가 있는 세트는 스킨 없이도 보인다.
                if (document.FindRig(emoji.Rig)?.Nodes.Any(n => n.Color is not null) != true)
                {
                    Notice("그림(스킨)이 없다 — 아무것도 안 보인다");
                }
            }
            else if (document.FindSkin(emoji.Skin) is not { } skin)
            {
                Block($"스킨 '{emoji.Skin}'이 없다");
            }
            else if (!string.Equals(skin.Rig, emoji.Rig, StringComparison.Ordinal))
            {
                Block($"스킨 '{skin.Name}'은 리그 '{skin.Rig}'의 것이다(이모지는 '{emoji.Rig}')");
            }
            else if (skin.Parts.Count == 0)
            {
                Notice($"스킨 '{skin.Name}'에 그림을 준 조각이 없다");
            }

            foreach ((string label, string? name) in new[] { ("등장", emoji.Enter), ("머묾", emoji.Hold), ("퇴장", emoji.Exit) })
            {
                if (name is null)
                {
                    continue;
                }

                if (document.Find(name) is not { } motion)
                {
                    Block($"{label} 모션 '{name}'이 없다");
                }
                else if (!Wears(emoji.Rig ?? "", motion, rigNodeIds))
                {
                    Block($"{label} 모션 '{name}'의 리그 '{motion.Rig}'가 이모지의 리그 '{emoji.Rig}'에 안 맞는다");
                }
            }

            if (emoji.HoldSeconds is { } hold && (!float.IsFinite(hold) || hold < 0f))
            {
                Block($"머무는 시간은 0 이상의 수다 ({hold})");
            }

            // 자리 노드를 따로 적었는데 그 리그에 없을 때만 알린다 — 기본값(Emoji_Anchor)이 없는 세트는 루트에 오프셋이 들어가는 것이 뜻이다.
            if (nodes is not null && !nodes.Contains(emoji.AnchorNode) && emoji.AnchorNode != EmojiAsset.DefaultAnchorNode)
            {
                Notice($"자리 노드 '{emoji.AnchorNode}'가 리그에 없다 — 오프셋이 루트에 들어간다");
            }
        }

        return problems;
    }

    /// <summary>그 리그가 모션을 입을 수 있나 — rigKind가 같거나, 채널의 노드가 전부 그 리그에 있다.</summary>
    private static bool Wears(string rig, MotionAsset motion, IReadOnlyDictionary<string, ICollection<string>>? rigNodeIds)
    {
        if (string.Equals(rig, motion.Rig, StringComparison.Ordinal))
        {
            return true;
        }

        return motion.Channels.Count > 0
            && rigNodeIds is not null
            && rigNodeIds.TryGetValue(rig, out ICollection<string>? nodes)
            && motion.Channels.All(c => nodes.Contains(c.Node));
    }
}
