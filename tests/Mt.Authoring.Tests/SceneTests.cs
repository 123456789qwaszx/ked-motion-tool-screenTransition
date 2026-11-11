using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Tests;

public class SceneTests
{
    private static readonly RigSchemaFile RigFile = RigSchemaFile.Load(Fixtures.PathOf("rig-schemas.json"));

    [Fact]
    public void 새_문서는_캐릭터_하나짜리_기본_씬이고_씬은_저작본에_왕복한다()
    {
        var editor = new MotionEditor(new MotionDocument());
        SceneAsset scene = Assert.Single(editor.Document.Scenes);
        Assert.Equal(scene.Name, editor.Document.ActiveScene);
        SceneObject s1 = Assert.Single(scene.Objects);
        Assert.Equal("s1", s1.Id);
        Assert.Equal("character", s1.Rig);
        Assert.Equal(SceneLayers.Mid, s1.Layer);
        Assert.Equal("stage00", s1.Stage); // 게임·코어 slot 기본과 같다

        editor.AddSceneObject(null, "fx", "screenEffect", SceneLayers.Far);
        editor.SetObjectPlacement(null, "s1", 120f, -30f, 5f, 1.2f, 0.9f);
        editor.SetObjectPortrait(null, "s1", "assets/harin.png");
        editor.SetObjectVisible(null, "fx", false);

        string json = MotionDocumentJson.Write(editor.Document);
        MotionDocument read = MotionDocumentJson.Read(json);
        Assert.Equal(json, MotionDocumentJson.Write(read));

        SceneAsset back = read.ActiveSceneAsset!;
        Assert.Equal(new[] { "s1", "fx" }, back.Objects.Select(o => o.Id));
        Assert.Equal(120f, back.Objects[0].PositionX);
        Assert.Equal(0.9f, back.Objects[0].ScaleY);
        Assert.Equal("assets/harin.png", back.Objects[0].Portrait);
        Assert.False(back.Objects[1].Visible);
        Assert.Contains("\"layer\": \"far\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"prefix\"", json, StringComparison.Ordinal); // 계산 전용 속성은 파일에 없다
        Assert.DoesNotContain("\"activeSceneAsset\"", json, StringComparison.Ordinal);
        Assert.Equal(new[] { "fx", "s1" }, back.DrawOrder().Select(o => o.Id)); // far 먼저
    }

    [Fact]
    public void 씬_명령은_규칙을_지키고_각각_되돌리기_한_번이다()
    {
        var editor = new MotionEditor(new MotionDocument());
        Assert.Throws<InvalidOperationException>(() => editor.AddSceneObject(null, "s1", "character")); // 겹침
        Assert.Throws<InvalidOperationException>(() => editor.AddSceneObject(null, "C2", "character")); // 대문자
        Assert.Throws<InvalidOperationException>(() => editor.AddSceneObject(null, "s2", "character", "sky")); // 모르는 레이어

        long before = editor.Revision;
        editor.AddSceneObject(null, "s2", "character", SceneLayers.Close);
        editor.SetObjectPlacement(null, "s2", 10f, 0f, 0f, 1f, 1f);
        editor.SetObjectPlacement(null, "s2", 10f, 0f, 0f, 1f, 1f); // 같은 값은 조작이 아니다
        editor.SetObjectLayer(null, "s2", SceneLayers.Back);
        Assert.Equal(before + 3, editor.Revision);

        editor.Undo();
        Assert.Equal(SceneLayers.Close, editor.Document.ActiveSceneAsset!.Find("s2")!.Layer);
        editor.Undo();
        editor.Undo();
        Assert.Null(editor.Document.ActiveSceneAsset!.Find("s2"));
    }

    [Fact]
    public void 씬_트리는_오브젝트마다_접두사로_서고_배치가_배치_축_노드에_들어간다()
    {
        var scene = new SceneAsset();
        scene.Objects.Add(new SceneObject { Id = "s1", Rig = "character", PositionX = 100f, PositionY = 50f, RotationZ = 10f, ScaleX = 1.5f, ScaleY = 2f });
        scene.Objects.Add(new SceneObject { Id = "fx", Rig = "screenEffect", Layer = SceneLayers.Far });
        scene.Objects.Add(new SceneObject { Id = "nope", Rig = "robot" });
        var missing = new List<SceneObject>();

        RectNodeTree tree = ScenePreview.Build(RigFile, scene, _ => 0.8f, missing);

        Assert.True(tree.Contains("s1/CharacterPortraitSprite_Image"));
        Assert.True(tree.Contains("fx/__root"));
        Assert.False(tree.Contains("CharacterPortraitSprite_Image")); // 접두사 없는 키는 없다
        Assert.Equal("nope", Assert.Single(missing).Id);

        RectNodeState track = tree.GetState("s1/CharSlot_Track");
        Assert.Equal(100f, track.AnchoredPosition.X);
        Assert.Equal(50f, track.AnchoredPosition.Y);
        Assert.Equal(10f, tree.GetState("s1/CharSlot_SwayPivot").LocalEulerAngles.Z);
        Assert.Equal(1.5f, tree.GetState("s1/CharSlot_Scale").LocalScale.X);
        Assert.Equal(2f, tree.GetState("s1/CharSlot_Scale").LocalScale.Y);
        Assert.Equal(1f, tree.GetState("s1/CharSlot_Scale").LocalScale.Z); // z는 그대로

        // 초상 사이징(종횡비 0.8)은 접두사 밑에서도 입혀진다.
        Vec2 image = tree.GetRectSize("s1/CharacterPortraitSprite_Image");
        Assert.True(image.X > 0 && image.Y > 0);
    }

    [Fact]
    public void 모션은_선택_오브젝트에만_얹히고_공통_스키마면_다른_리그도_입는다()
    {
        MotionDocument document = Fixtures.NodBowDocument();
        var editor = new MotionEditor(document);
        editor.AddSceneObject(null, "s2", "character");
        editor.AddSceneObject(null, "fx", "screenEffect", SceneLayers.Far);
        SceneAsset scene = document.ActiveSceneAsset!;
        RectNodeTree template = ScenePreview.Build(RigFile, scene);
        MotionAsset nod = document.Find("nod")!;

        RectNodeTree pose = ScenePreview.Pose(template, scene.Find("s2")!, nod, 0.3);

        Assert.Equal(-18f, pose.GetState("s2/CharacterPortrait_Track_Move_Y").AnchoredPosition.Y, 3);
        Assert.Equal(template.GetState("s1/CharacterPortrait_Track_Move_Y").AnchoredPosition.Y, pose.GetState("s1/CharacterPortrait_Track_Move_Y").AnchoredPosition.Y, 3);

        Assert.True(ScenePreview.CanWear(RigFile, scene.Find("s1")!, nod));
        Assert.False(ScenePreview.CanWear(RigFile, scene.Find("fx")!, nod)); // 화면 효과 리그에는 그 노드가 없다
    }

    [Fact]
    public void 삭제_복제_이름바꾸기는_오브젝트_하나씩이고_다음_id는_숫자를_올린다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddSceneObject(null, "s2", "character", SceneLayers.Front);
        editor.AddSceneObject(null, "s3", "character", SceneLayers.Front);
        Assert.Throws<InvalidOperationException>(() => editor.AddSceneObject(null, "pivot", "", SceneLayers.Mid)); // 리그 없는 오브젝트는 없다
        SceneAsset scene = editor.Document.ActiveSceneAsset!;

        // 레이어·무대는 오브젝트 하나만 바꾼다(오브젝트끼리 부모·자식은 없다).
        editor.SetObjectLayer(null, "s1", SceneLayers.Close);
        Assert.Equal(SceneLayers.Close, scene.Find("s1")!.Layer);
        Assert.Equal(SceneLayers.Front, scene.Find("s2")!.Layer);

        // 이름 바꾸기.
        editor.RenameSceneObject(null, "s2", "arm");
        Assert.NotNull(scene.Find("arm"));
        Assert.Throws<InvalidOperationException>(() => editor.RenameSceneObject(null, "arm", "s1")); // 겹침
        Assert.Throws<InvalidOperationException>(() => editor.RenameSceneObject(null, "arm", "Arm")); // 규칙 밖

        // 복제 — 새 id는 숫자 올림, 원본 바로 뒤.
        long before = editor.Revision;
        SceneObject copy = editor.DuplicateSceneObject(null, "s1");
        Assert.Equal(before + 1, editor.Revision);
        Assert.Equal("s2", copy.Id); // s1 → s2 (비어 있다)
        Assert.Equal(new[] { "s1", "s2", "arm", "s3" }, scene.Objects.Select(o => o.Id));
        Assert.Equal(SceneLayers.Close, copy.Layer);
        editor.Undo();
        scene = editor.Document.ActiveSceneAsset!; // 되돌리기는 스냅샷 복원 — 참조를 다시 읽는다
        Assert.Equal(3, scene.Objects.Count);

        // 삭제 — 되돌리기 한 번.
        before = editor.Revision;
        editor.RemoveSceneObject(null, "arm");
        Assert.Equal(before + 1, editor.Revision);
        Assert.Equal(new[] { "s1", "s3" }, scene.Objects.Select(o => o.Id));
        editor.Undo();
        Assert.Equal(3, editor.Document.ActiveSceneAsset!.Objects.Count);

        // 저작본에 parent·parentNode·character는 없다.
        string json = MotionDocumentJson.Write(editor.Document);
        Assert.DoesNotContain("\"parent", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"character\":", json, StringComparison.Ordinal); // 값("rig": "character")이 아니라 키

        Assert.Equal("s5", MotionEditor.NextObjectId("s1", new[] { "s2", "s3", "s4" }));
        Assert.Equal("bg_2", MotionEditor.NextObjectId("bg", Array.Empty<string>()));
        Assert.Equal("11_2", MotionEditor.NextObjectId("11", Array.Empty<string>())); // 전부 숫자면 줄기가 없다
    }

    [Fact]
    public void 씬은_여럿일_수_있고_활성_씬이_문서에_남는다()
    {
        var editor = new MotionEditor(new MotionDocument());
        Assert.Throws<InvalidOperationException>(() => editor.RemoveScene("scene")); // 마지막 씬
        Assert.Throws<InvalidOperationException>(() => editor.AddScene("Scene B")); // 규칙 밖

        editor.AddScene("scene_b");
        Assert.Equal("scene_b", editor.Document.ActiveScene);
        Assert.Equal("s1", Assert.Single(editor.Document.ActiveSceneAsset!.Objects).Id);
        editor.AddSceneObject(null, "s2", "character", SceneLayers.Front);

        editor.DuplicateScene("scene_b", "scene_c");
        Assert.Equal("scene_c", editor.Document.ActiveScene);
        Assert.Equal(2, editor.Document.ActiveSceneAsset!.Objects.Count);
        Assert.Equal(new[] { "scene", "scene_b", "scene_c" }, editor.Document.Scenes.Select(x => x.Name));

        editor.SetActiveScene("scene");
        Assert.Single(editor.Document.ActiveSceneAsset!.Objects);
        editor.RenameScene("scene", "main");
        Assert.Equal("main", editor.Document.ActiveScene);
        Assert.Throws<InvalidOperationException>(() => editor.RenameScene("scene_b", "main"));

        editor.RemoveScene("main"); // 활성 씬을 지우면 이웃이 활성
        Assert.Equal("scene_b", editor.Document.ActiveScene);
        editor.Undo();
        Assert.Equal("main", editor.Document.ActiveScene);

        string json = MotionDocumentJson.Write(editor.Document);
        MotionDocument read = MotionDocumentJson.Read(json);
        Assert.Equal("main", read.ActiveScene);
        Assert.Equal(3, read.Scenes.Count);
    }

    [Fact]
    public void 내장_기본값은_런타임_ExportedTuning_그대로이고_뼈대는_항등_85노드다()
    {
        Assert.Equal(RigFile.Sha256, BuiltinTuning.RigSchemas.Sha256); // 픽스처 = 익스포터 산출물 = 내장본
        Assert.Contains("character", BuiltinTuning.RigSchemas.RigKinds);
        Assert.Equal(1920f, BuiltinTuning.BaseResolution.X);
        RigSchemaRigDto stage = BuiltinTuning.StageRig;
        Assert.Equal(85, stage.nodes.Count);
        Assert.All(stage.nodes, n => Assert.Equal(1f, n.localScale.x));
        Assert.Contains(stage.nodes, n => n.id == "Stage00Depth_Mid_Content" && n.parent == "Stage00Depth_Mid_FramingScale");
        Assert.True(StageNodes.TryParseContent("Stage02Depth_Close_Content", out string st, out string layer));
        Assert.Equal(("stage02", "close"), (st, layer));
        Assert.False(StageNodes.TryParseContent("Stage00Mask_Root", out _, out _));
    }

    [Fact]
    public void 뼈대_위에_세워도_좌표는_같고_오브젝트는_그_무대_층의_Content_밑에_선다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddSceneObject(null, "s2", "character", SceneLayers.Far, stage: "stage01");
        editor.SetObjectPlacement(null, "s1", 120f, -30f, 5f, 1.1f, 1f);
        SceneAsset scene = editor.Document.ActiveSceneAsset!;
        Assert.Throws<InvalidOperationException>(() => editor.AddSceneObject(null, "s3", "character", SceneLayers.Mid, stage: "stage07"));

        RectNodeTree plain = ScenePreview.Build(RigFile, scene);
        RectNodeTree staged = ScenePreview.Build(RigFile, scene, null, null, BuiltinTuning.StageRig);

        Assert.Equal("stage/Stage00Depth_Mid_Content", staged.GetParentKey("s1/__root"));
        Assert.Equal("stage/Stage01Depth_Far_Content", staged.GetParentKey("s2/__root"));
        Assert.Null(plain.GetParentKey("s1/__root"));

        // 그리는 순서는 무대 먼저(stage00 → stage01 → stage02), 무대 안에서 far → close — 유니티 PresentataionUIRoot 그대로.
        editor.AddSceneObject(null, "s3", "character", SceneLayers.Close, stage: "stage00");
        editor.AddSceneObject(null, "s4", "character", SceneLayers.Far, stage: "stage02");
        Assert.Equal(new[] { "s1", "s3", "s2", "s4" }, scene.DrawOrder().Select(o => o.Id)); // stage00 mid·close → stage01 far → stage02 far
        editor.RemoveSceneObject(null, "s3");
        editor.RemoveSceneObject(null, "s4");

        foreach (string key in new[] { "s1/CharacterPortraitSprite_Image", "s2/CharacterPortraitSprite_Image", "s1/CharSlot_Track" })
        {
            Vec2 a = plain.TransformPoint(key, Vec3.Zero).XY;
            Vec2 b = staged.TransformPoint(key, Vec3.Zero).XY;
            Assert.Equal(a.X, b.X, 3);
            Assert.Equal(a.Y, b.Y, 3);
        }

        Assert.Equal(1920f, staged.GetRectSize("stage/Stage00Depth_Mid_Content").X, 3);
    }
}
