using Mt.Authoring.Model;

namespace Mt.Authoring.Editing;

// 전환의 이름을 바꾼다.
public sealed partial class MotionEditor
{
    /// <summary>전환의 이름을 바꾼다 — 타임라인의 이름은 전환의 것이라 함께 바뀐다. 한 칸.</summary>
    public void RenameTransition(string transition, string newName)
    {
        TransitionAsset asset = RequireTransition(transition);

        if (string.Equals(transition, newName?.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        string name = RequireNewTransitionName(newName);
        Mutate(MotionChangeKind.Document, () => asset.Name = name);
    }

    private string RequireNewTransitionName(string? transition)
    {
        string name = (transition ?? "").Trim();

        if (!TransitionNameRule.IsMatch(name))
        {
            throw new InvalidOperationException($"전환 이름은 영문 소문자로 시작하는 소문자·숫자·_ 다 ('{transition}')");
        }

        if (Document.FindTransition(name) is not null)
        {
            throw new InvalidOperationException($"전환 '{name}'가 이미 있다.");
        }

        return name;
    }
}
