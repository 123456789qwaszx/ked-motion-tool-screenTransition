using Ked.Presentation.Core;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Preview;

/// <summary>무대의 시작 상태 — 세트들을 루트 밑에 세운다. 루트 공간은 무대(기준 화면)다.</summary>
public static class ScenePreview
{
    public static RectNodeTree Build(RigSchemaFile rig, SceneAsset scene)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(scene);

        var space = new RectSpace(rig.Dto.capturedUnderParentSize.ToVec2(), Vec2.Half);
        var tree = new RectNodeTree(space);

        foreach (SceneObject o in scene.Objects)
        {
            if (rig.TryGetRig(o.Rig, out RigSchemaRigDto dto))
            {
                RigSchemaLoader.AddRigTo(tree, dto, o.Prefix);
            }
        }

        return tree;
    }

    /// <summary>
    /// 이 오브젝트에 모션을 입혀 볼 수 있나 — rigKind가 같거나, 채널의 노드가 전부 그 리그에 있으면 된다.
    /// </summary>
    public static bool CanWear(RigSchemaFile rig, SceneObject o, MotionAsset motion)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(o);
        ArgumentNullException.ThrowIfNull(motion);

        if (string.Equals(o.Rig, motion.Rig, StringComparison.Ordinal))
        {
            return true;
        }

        // 채널이 없으면 공유할 것도 없다 — 리그가 다르면 안 맞는 것으로 본다.
        return motion.Channels.Count > 0
            && rig.NodeIds.TryGetValue(o.Rig, out ICollection<string>? nodes)
            && motion.Channels.All(c => nodes.Contains(c.Node));
    }

    public static string RootKeyOf(SceneObject o) => o.KeyOf(RigSchemaLoader.RootKey);
}
