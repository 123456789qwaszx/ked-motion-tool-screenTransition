using Mt.Authoring.Model;

namespace Mt.Authoring.Editing;

// 세트의 이름을 바꾼다.
public sealed partial class MotionEditor
{
    /// <summary>
    /// 세트의 이름을 바꾼다 — 그 이름을 가리키던 것이 같은 칸에서 따라온다: 그 세트의 애니메이션.
    /// **세트의 이름을 딴 이름들도 따라온다**: 애니메이션 <c>옛이름</c> · <c>옛이름_x</c> → <c>새이름</c> · <c>새이름_x</c> — 새 이름이 비어 있을 때만.
    /// </summary>
    public void RenameRig(string kind, string newKind)
    {
        RigAsset rig = RequireRig(kind);

        if (string.Equals(kind, newKind?.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        string name = RequireNewRigKind(newKind);

        Batch(MotionChangeKind.Document, () =>
        {
            RenameRigCore(rig, kind, name);

            foreach (MotionAsset motion in Document.Motions.Where(m => m.Rig == name && (m.Name == kind || m.Name.StartsWith(kind + "_", StringComparison.Ordinal))).ToList())
            {
                string renamed = name + motion.Name[kind.Length..];

                if (Document.Find(renamed) is null)
                {
                    RenameMotion(motion.Name, renamed);
                }
            }
        });
    }

    private void RenameRigCore(RigAsset rig, string kind, string name)
    {
        Mutate(MotionChangeKind.Document, () =>
        {
            rig.Kind = name;

            foreach (MotionAsset motion in Document.Motions.Where(m => m.Rig == kind))
            {
                motion.Rig = name;
            }
        });
    }

    private string RequireNewRigKind(string? kind)
    {
        string name = (kind ?? "").Trim();

        if (!RigKindRule.IsMatch(name))
        {
            throw new InvalidOperationException($"세트 이름은 영문자로 시작하는 영문·숫자·_ 다 ('{kind}')");
        }

        if (Document.FindRig(name) is not null)
        {
            throw new InvalidOperationException($"세트 '{name}'가 이미 있다.");
        }

        return name;
    }
}
