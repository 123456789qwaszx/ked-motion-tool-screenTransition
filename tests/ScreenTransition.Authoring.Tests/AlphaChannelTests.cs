using ScreenTransition.Core;
using ScreenTransition.Authoring.Editing;
using ScreenTransition.Authoring.Model;
using ScreenTransition.Authoring.Preview;
using ScreenTransition.Authoring.Serialization;
using ScreenTransition.Authoring.Validation;

namespace ScreenTransition.Authoring.Tests;

/// <summary>불투명도 채널 — 알파를 키와 커브로.</summary>
public class AlphaChannelTests
{
    private const string Part = "Emoji_Part1";
    private const string Body = "Emoji_Body";

    private static KeyAsset Linear(float t, float v) => new() { T = t, V = v, Tangent = TangentMode.Linear };

    /// <summary>이모지 전환 둘. fade(1초 commit): Part1 알파 1 → 0. dim(1초 commit): Body 알파 1 → 0.5.</summary>
    private static MotionEditor NewEditor()
    {
        var editor = new MotionEditor(new MotionDocument());

        editor.AddTimeline("fade", 1f, TransitionTemplates.Emoji());
        editor.AddChannel("fade", Part, MotionChannelKind.Alpha);
        editor.SetKeys("fade", Part, MotionChannelKind.Alpha, Axis.X, new[] { Linear(0f, 1f), Linear(1f, 0f) });

        editor.AddTimeline("dim", 1f, TransitionTemplates.Emoji());
        editor.AddChannel("dim", Body, MotionChannelKind.Alpha);
        editor.SetKeys("dim", Body, MotionChannelKind.Alpha, Axis.X, new[] { Linear(0f, 1f), Linear(1f, 0.5f) });
        return editor;
    }

    private static RectNodeTree Template(MotionEditor editor) => StageTree.Build(editor.Document.FindTransition("fade")!);

    private static void Near(float expected, float actual) => Assert.InRange(actual, expected - 1e-3f, expected + 1e-3f);

    [Fact]
    public void 알파_채널은_트리가_아니라_불투명도_표에_얹힌다()
    {
        MotionEditor editor = NewEditor();
        RectNodeTree template = Template(editor);
        TimelineAsset fade = editor.Document.Find("fade")!;
        var alphas = new Dictionary<string, float>();

        RectNodeTree pose = MotionPreview.Pose(template, fade, 0.25, null, alphas);
        Near(0.75f, alphas[Part]);
        Assert.Equal(template.GetState(Part).AnchoredPosition, pose.GetState(Part).AnchoredPosition); // 트리는 그대로

        // 곱이다 — 이미 0.5인 불투명도에서 흐려지면 0.5 × 0.75.
        alphas = new Dictionary<string, float> { [Part] = 0.5f };
        MotionPreview.Pose(template, fade, 0.25, null, alphas);
        Near(0.375f, alphas[Part]);

        // 끝: 길이를 지나면 마지막 키가 남는다(0).
        alphas = new Dictionary<string, float>();
        MotionPreview.Pose(template, fade, 9.0, null, alphas);
        Near(0f, alphas[Part]);

        // 표를 안 주면 알파 채널은 건너뛴다(던지지 않는다).
        MotionPreview.Pose(template, fade, 0.5, null);
    }
}
