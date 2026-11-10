using Ked.Presentation.Core;
using Mt.Authoring.Model;
using Mt.Authoring.Rig;

namespace Mt.Authoring.Preview;

/// <summary>
/// 씬 → 트리. 오브젝트마다 리그를 슬롯 접두사로 세우고(코어 <c>RigSchemaLoader.AddRigTo</c> — 리듀서의 slot 폴드와 같은 함수),
/// 배치 값을 배치 축 노드에 넣고, 초상 사이징을 입힌다. 모션은 <see cref="Pose"/>가 고른 오브젝트 하나에 얹는다.
/// </summary>
public static class ScenePreview
{
    /// <summary>
    /// 씬 전체의 시작 상태. 덤프에 없는 rigKind의 오브젝트는 건너뛴다(<paramref name="missing"/>에 남긴다).
    /// 루트 공간은 덤프가 캡처된 공간이다(스트레치 노드 크기의 기준).
    /// </summary>
    /// <param name="stage">
    /// 무대 뼈대(<c>PresentataionUIRoot</c>, rigKind "stage"). 있으면 <c>stage/</c> 접두사로 먼저 서고 오브젝트는
    /// <c>Stage{NN}Depth_{Layer}_Content</c> 밑에 붙는다. 없으면 루트 직속(좌표는 같다 — 뼈대는 항등).
    /// </param>
    public static RectNodeTree Build(RigSchemaFile rig, SceneAsset scene, Func<SceneObject, float>? aspectOf = null, List<SceneObject>? missing = null, RigSchemaRigDto? stage = null)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(scene);

        var space = new RectSpace(rig.Dto.capturedUnderParentSize.ToVec2(), Vec2.Half);
        var state = new StageState(space);

        if (stage is not null)
        {
            RigSchemaLoader.AddRigTo(state.Nodes, stage, StageNodes.Prefix);
        }

        foreach (SceneObject o in scene.DrawOrder())
        {
            string? attach = null;

            if (stage is not null)
            {
                // 오브젝트는 그 무대·층의 Content에 선다. 런타임 GetDepthContent(stage, layer)와 같은 자리. 모르는 라벨은 루트 직속.
                string content = StageNodes.KeyOf(StageNodes.ContentOf(o.Stage, o.Layer));
                attach = state.Nodes.Contains(content) ? content : null;
            }

            if (!rig.TryGetRig(o.Rig, out RigSchemaRigDto dto))
            {
                missing?.Add(o);
                continue;
            }

            RigSchemaLoader.AddRigTo(state.Nodes, dto, o.Prefix, attach!);
            state.RegisterSlot(o.Id);
            PortraitLayout.ApplySizing(state.Nodes, aspectOf?.Invoke(o) ?? PortraitLayout.PlaceholderAspect, o.Prefix);

            ApplyPlacement(state.Nodes, o);
        }

        return state.Nodes;
    }

    /// <summary>배치 값을 배치 축 노드에. 그 노드가 없는 리그(배경 등)면 루트 노드에 — 그래서 어느 오브젝트든 옮길 수 있다.</summary>
    public static void ApplyPlacement(RectNodeTree tree, SceneObject o)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(o);

        string root = RootKeyOf(o);
        string position = tree.Contains(o.KeyOf(PlacementNodes.Position)) ? o.KeyOf(PlacementNodes.Position) : root;
        string rotation = tree.Contains(o.KeyOf(PlacementNodes.Rotation)) ? o.KeyOf(PlacementNodes.Rotation) : root;
        string scale = tree.Contains(o.KeyOf(PlacementNodes.Scale)) ? o.KeyOf(PlacementNodes.Scale) : root;

        if (tree.Contains(position))
        {
            tree.SetState(position, tree.GetState(position).WithAnchoredPosition(new Vec2(o.PositionX, o.PositionY)));
        }

        if (tree.Contains(rotation))
        {
            RectNodeState state = tree.GetState(rotation);
            tree.SetState(rotation, state.WithLocalEuler(new Vec3(state.LocalEulerAngles.X, state.LocalEulerAngles.Y, o.RotationZ)));
        }

        if (tree.Contains(scale))
        {
            RectNodeState state = tree.GetState(scale);
            tree.SetState(scale, state.WithLocalScale(new Vec3(o.ScaleX, o.ScaleY, state.LocalScale.Z)));
        }
    }

    /// <summary>모션을 오브젝트 하나에 얹은 시각 t의 포즈. 채널의 노드가 그 리그에 없으면 그 채널은 건너뛴다.</summary>
    public static RectNodeTree Pose(RectNodeTree template, SceneObject target, MotionAsset motion, double seconds, Func<Graph.CurveRef, bool>? isMuted = null, IDictionary<string, float>? alphas = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(motion);
        return MotionPreview.Pose(template, motion, seconds, motion.PreviewMode, isMuted, target.Prefix, alphas);
    }

    /// <summary>
    /// 이 오브젝트에 모션을 입혀 볼 수 있나 — rigKind가 같거나, 채널의 노드가 전부 그 리그에 있으면(공통 스키마면 공유) 된다.
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

    /// <summary>오브젝트의 그림 노드 키(초상). 리그에 없으면 null — 그때는 루트 rect를 윤곽으로 그린다.</summary>
    public static string? ImageKeyOf(RectNodeTree tree, SceneObject o)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(o);
        string key = o.KeyOf(PortraitLayout.ImageNodeId);
        return tree.Contains(key) ? key : null;
    }

    public static string RootKeyOf(SceneObject o) => o.KeyOf(RigSchemaLoader.RootKey);
}
