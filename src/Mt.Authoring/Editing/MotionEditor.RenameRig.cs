using Mt.Authoring.Model;

namespace Mt.Authoring.Editing;

// 세트의 이름을 바꾼다.
public sealed partial class MotionEditor
{
    /// <summary>세트의 이름을 바꾼다 — 타임라인의 이름은 세트의 것이라 함께 바뀐다. 한 칸.</summary>
    public void RenameRig(string kind, string newKind)
    {
        RigAsset rig = RequireRig(kind);

        if (string.Equals(kind, newKind?.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        string name = RequireNewRigKind(newKind);
        Mutate(MotionChangeKind.Document, () => rig.Kind = name);
    }

    private string RequireNewRigKind(string? kind)
    {
        string name = (kind ?? "").Trim();

        if (!RigKindRule.IsMatch(name))
        {
            throw new InvalidOperationException($"세트 이름은 영문 소문자로 시작하는 소문자·숫자·_ 다 ('{kind}')");
        }

        if (Document.FindRig(name) is not null)
        {
            throw new InvalidOperationException($"세트 '{name}'가 이미 있다.");
        }

        return name;
    }
}
