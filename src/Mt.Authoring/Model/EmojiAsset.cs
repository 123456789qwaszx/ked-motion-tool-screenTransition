namespace Mt.Authoring.Model;

/// <summary>
/// 연출 정의 — 키 하나가 "세트 + 그림(스킨) + 등장·머묾·퇴장 모션 + 머무는 시간 + 자리"를 가리킨다. 화면에 선다(캐릭터에 붙지 않는다).
/// 런타임은 이 정의를 불러 한 번의 수명을 돌린다: 등장 모션 → 머무는 동안 머묾 모션을 되풀이 → 퇴장 모션 → 사라짐.
/// 머무는 시간이 없으면(null) 끄라고 할 때까지 머문다.
///
/// 모션과 스킨은 **참조**한다.
/// </summary>
public sealed class EmojiAsset
{
    /// <summary>문서에서 하나뿐인 키. 빈칸 없는 글(한글도 된다) — 대본이 이 이름으로 부른다.</summary>
    public string Name { get; set; } = "";

    /// <summary>이 이모지가 서는 리그(rigKind).</summary>
    public string Rig { get; set; } = "";

    /// <summary>그림 — 스킨의 이름. null이면 그림이 없다(검증이 알린다).</summary>
    public string? Skin { get; set; }

    /// <summary>등장 모션(한 번). null이면 바로 선다.</summary>
    public string? Enter { get; set; }

    /// <summary>머묾 모션 — 머무는 동안 처음부터 되풀이한다(바퀴마다 등장이 끝난 자세에서 다시). null이면 가만히 머문다.</summary>
    public string? Hold { get; set; }

    /// <summary>퇴장 모션(한 번). null이면 바로 사라진다.</summary>
    public string? Exit { get; set; }

    /// <summary>
    /// 머무는 시간(초) — 등장이 끝난 뒤 퇴장이 시작하기까지. null이면 **끄라고 할 때까지** 머문다(머무는 이모지: 상태다 — 되감기에서 살아 있어야 한다).
    /// 값이 있으면 잠깐 뜨고 사라지는 이모지다(이벤트 — 끝 상태가 없다).
    /// </summary>
    public float? HoldSeconds { get; set; }

    /// <summary>자리를 주는 노드 — 이 노드의 위치에 <see cref="OffsetX"/>·<see cref="OffsetY"/>가 들어간다. 리그에 없으면 루트.</summary>
    public string AnchorNode { get; set; } = DefaultAnchorNode;

    /// <summary>자리 노드에 넣는 오프셋(px).</summary>
    public float OffsetX { get; set; }

    public float OffsetY { get; set; }

    public const string DefaultAnchorNode = "Emoji_Anchor";

    public EmojiAsset Clone() => (EmojiAsset)MemberwiseClone();
}
