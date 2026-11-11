using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Tests;

/// <summary>불투명도 채널(<c>PLAN-parts.md</c> P4, <c>motions/3</c>) — 알파를 키와 커브로.</summary>
public class AlphaChannelTests
{
    private const string Part = "Emoji_Part1";
    private const string Body = "Emoji_Body";
    private const string SpriteRoot = "CharacterPortraitSprite_Root";

    private static readonly RigSchemaFile RigFile = RigSchemaFile.Load(EmojiRigFixture.WriteDump(Fixtures.PathOf("rig-schemas.json")));

    private static KeyAsset Linear(float t, float v) => new() { T = t, V = v, Tangent = TangentMode.Linear };

    /// <summary>e1(이모지). fade(1초 commit): Part1 알파 1 → 0. dim(1초 commit): Body 알파 1 → 0.5.</summary>
    private static MotionEditor NewEditor()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddSceneObject(null, "e1", EmojiRigFixture.RigKind);

        editor.AddMotion("fade", EmojiRigFixture.RigKind, 1f, MotionEnd.Commit);
        editor.AddChannel("fade", Part, MotionChannelKind.Alpha);
        editor.SetKeys("fade", Part, MotionChannelKind.Alpha, Axis.X, new[] { Linear(0f, 1f), Linear(1f, 0f) });

        editor.AddMotion("dim", EmojiRigFixture.RigKind, 1f, MotionEnd.Commit);
        editor.AddChannel("dim", Body, MotionChannelKind.Alpha);
        editor.SetKeys("dim", Body, MotionChannelKind.Alpha, Axis.X, new[] { Linear(0f, 1f), Linear(1f, 0.5f) });
        return editor;
    }

    private static RectNodeTree Template(MotionEditor editor) => ScenePreview.Build(RigFile, editor.Document.ActiveSceneAsset!);

    private static void Near(float expected, float actual) => Assert.InRange(actual, expected - 1e-3f, expected + 1e-3f);

    [Fact]
    public void 알파_채널은_트리가_아니라_불투명도_표에_얹힌다()
    {
        MotionEditor editor = NewEditor();
        RectNodeTree template = Template(editor);
        MotionAsset fade = editor.Document.Find("fade")!;
        var alphas = new Dictionary<string, float>();

        RectNodeTree pose = MotionPreview.Pose(template, fade, 0.25, MotionChannelMode.Relative, null, "e1/", alphas);
        Near(0.75f, alphas["e1/" + Part]);
        Assert.Equal(template.GetState("e1/" + Part).AnchoredPosition, pose.GetState("e1/" + Part).AnchoredPosition); // 트리는 그대로

        // relative는 곱이다 — 이미 0.5인 불투명도에서 흐려지면 0.5 × 0.75.
        alphas = new Dictionary<string, float> { ["e1/" + Part] = 0.5f };
        MotionPreview.Pose(template, fade, 0.25, MotionChannelMode.Relative, null, "e1/", alphas);
        Near(0.375f, alphas["e1/" + Part]);

        // absolute는 키 그대로.
        alphas = new Dictionary<string, float> { ["e1/" + Part] = 0.5f };
        MotionPreview.Pose(template, fade, 0.25, MotionChannelMode.Absolute, null, "e1/", alphas);
        Near(0.75f, alphas["e1/" + Part]);

        // 끝: commit은 남고(0), restore는 시작값 그대로다(표를 안 건드린다).
        alphas = new Dictionary<string, float>();
        MotionPreview.Pose(template, fade, 9.0, MotionChannelMode.Relative, null, "e1/", alphas);
        Near(0f, alphas["e1/" + Part]);
        editor.SetEnd("fade", MotionEnd.Restore);
        alphas = new Dictionary<string, float>();
        MotionPreview.Pose(template, editor.Document.Find("fade")!, 9.0, MotionChannelMode.Relative, null, "e1/", alphas);
        Assert.Empty(alphas);

        // 표를 안 주면 알파 채널은 건너뛴다(던지지 않는다).
        MotionPreview.Pose(template, fade, 0.5, MotionChannelMode.Relative, null, "e1/");
    }
}
