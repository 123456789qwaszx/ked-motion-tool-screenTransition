using Ked.Presentation.Core;

namespace Mt.Authoring.Model;

/// <summary>
/// 무대 뼈대(<c>PresentataionUIRoot</c>)의 이름 규약 — 툴의 stage/layer 라벨(<c>stage00</c>·<c>mid</c>, 런타임 slot 커맨드와 같은 어휘)을
/// 유니티 노드 이름(<c>Stage00Depth_Mid_Content</c>)으로 잇는다. 뼈대 노드는 오브젝트가 아니다 — 만들기·드롭의 자리일 뿐이다.
/// </summary>
public static class StageNodes
{
    public const string RigKind = "stage";

    /// <summary>뼈대 노드의 키 접두사 — 오브젝트 키(<c>s1/…</c>)와 겹치지 않는다.</summary>
    public const string Prefix = "stage/";

    public const string ShotRoot = "StageShot_Root";

    public static readonly string[] Stages = { "stage00", "stage01", "stage02" };

    public static bool IsKnownStage(string? stage) => stage is not null && Stages.Contains(stage);

    /// <summary>무대의 차례(0·1·2) — 그리는 순서. 모르는 무대는 −1(맨 뒤에 그린다).</summary>
    public static int IndexOf(string? stage) => stage is null ? -1 : Array.IndexOf(Stages, stage);

    /// <summary>"stage00" → "Stage00". 모르는 꼴은 그대로 머리글자만 올린다.</summary>
    public static string StageName(string stage)
        => stage.Length == 0 ? stage : char.ToUpperInvariant(stage[0]) + stage[1..];

    /// <summary>"mid" → "Mid".</summary>
    public static string LayerName(string layer)
        => layer.Length == 0 ? layer : char.ToUpperInvariant(layer[0]) + layer[1..];

    public static string StageRootOf(string stage) => $"{StageName(stage)}_Root";

    public static string LayerRootOf(string stage, string layer) => $"{StageName(stage)}Depth_{LayerName(layer)}_Root";

    /// <summary>오브젝트가 서는 자리 — 런타임 <c>GetDepthContent(stage, layer)</c>와 같은 노드.</summary>
    public static string ContentOf(string stage, string layer) => $"{StageName(stage)}Depth_{LayerName(layer)}_Content";

    /// <summary>뼈대 트리 키.</summary>
    public static string KeyOf(string nodeId) => Prefix + nodeId;

    /// <summary>Content 노드 이름 → (stage, layer). 아니면 false.</summary>
    public static bool TryParseContent(string nodeId, out string stage, out string layer)
    {
        stage = "";
        layer = "";

        foreach (string s in Stages)
        {
            foreach (string l in SceneLayers.Order)
            {
                if (string.Equals(ContentOf(s, l), nodeId, StringComparison.Ordinal))
                {
                    stage = s;
                    layer = l;
                    return true;
                }
            }
        }

        return false;
    }
}
