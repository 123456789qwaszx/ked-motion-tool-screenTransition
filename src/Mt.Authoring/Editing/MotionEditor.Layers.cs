using Ked.Presentation.Core;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Editing;

// 층 표를 세트로 들인다 — 그림 한 묶음이 그림 오브젝트 여럿으로, 한 칸에.
public sealed partial class MotionEditor
{
    /// <summary>바탕색 오브젝트의 이름 — 층 표에 바탕색이 있으면 맨 뒤에 무대 크기의 색 사각형으로 선다.</summary>
    public const string BackdropNode = "Backdrop";

    /// <summary>
    /// 층 표의 층들을 세트의 그림 오브젝트로 — 층마다 오브젝트 하나(이름은 파일 이름에서, 앞의 번호는 뗀다), 자리는 무대 가운데 기준(y는 위가 +),
    /// 크기는 표의 것. 바탕색이 있으면 맨 뒤에 <see cref="BackdropNode"/>가 선다. 통째로 되돌리기 한 칸.
    /// </summary>
    /// <param name="imageFolder">그림들이 있는 폴더(문서 기준 상대 경로) — 오브젝트의 그림이 <c>폴더/파일</c>로 적힌다.</param>
    /// <returns>만든 오브젝트의 이름들(표의 순서).</returns>
    public IReadOnlyList<string> ImportLayers(string kind, LayerSheet sheet, string imageFolder, Vec2 stageSize, string? parent = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(imageFolder);
        RigAsset rig = RequireRig(kind);

        if (sheet.Layers.Count == 0)
        {
            throw new InvalidOperationException("층 표에 층이 없다.");
        }

        string folder = imageFolder.Replace('\\', '/').TrimEnd('/');
        var created = new List<string>();

        Batch(MotionChangeKind.Document, () =>
        {
            if (sheet.Background is { } background && rig.Find(BackdropNode) is null)
            {
                AddRigNode(kind, BackdropNode, parent);
                UpdateRigNode(kind, BackdropNode, new RigNodeAsset { Width = stageSize.X, Height = stageSize.Y, Color = background, Alpha = true });
            }

            foreach (LayerEntry layer in sheet.Layers)
            {
                string id = UniqueNodeId(rig, NodeIdFrom(layer.File));
                AddRigNode(kind, id, parent);
                UpdateRigNode(kind, id, new RigNodeAsset
                {
                    // 표는 왼쪽 위 기준(y 아래가 +), 세트는 가운데 기준(y 위가 +).
                    X = layer.Left + layer.Width / 2f - stageSize.X / 2f,
                    Y = stageSize.Y / 2f - (layer.Top + layer.Height / 2f),
                    Width = layer.Width,
                    Height = layer.Height,
                    Image = folder.Length == 0 ? layer.File : $"{folder}/{layer.File}",
                    Alpha = true,
                });
                created.Add(id);
            }
        });

        return created;
    }

    /// <summary>파일 이름에서 오브젝트 이름을 — 확장자와 앞의 번호(<c>01_</c>)를 떼고, 영문·숫자·_만 남긴다. 영문자로 시작하지 않으면 Layer.</summary>
    private static string NodeIdFrom(string file)
    {
        string stem = Path.GetFileNameWithoutExtension(file.Replace('\\', '/'));
        stem = new string(stem.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray()).TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').Trim('_');
        return stem.Length > 0 && char.IsAsciiLetter(stem[0]) ? stem : "Layer";
    }

    private static string UniqueNodeId(RigAsset rig, string stem)
    {
        string id = stem;

        for (int i = 2; rig.Find(id) is not null; i++)
        {
            id = $"{stem}_{i}";
        }

        return id;
    }
}
