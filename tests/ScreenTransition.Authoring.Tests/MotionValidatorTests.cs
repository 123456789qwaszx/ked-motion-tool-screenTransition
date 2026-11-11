using ScreenTransition.Core;
using ScreenTransition.Authoring.Editing;
using ScreenTransition.Authoring.Graph;
using ScreenTransition.Authoring.Model;
using ScreenTransition.Authoring.Validation;

namespace ScreenTransition.Authoring.Tests;

public class MotionValidatorTests
{
    [Fact]
    public void nod_bow는_발판_전환으로_검증하면_문제가_0건이다()
    {
        MotionDocument document = Fixtures.NodBowDocument();
        Assert.Empty(MotionValidator.Validate(document));
    }

    [Fact]
    public void 전환에_없는_층의_채널은_거부한다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddTimeline("a", 1f, TransitionTemplates.Figure());
        editor.AddChannel("a", "NoSuchNode", MotionChannelKind.Position);
        // relative 전 구간 중립값이면 성분이 내보내기에서 빠지므로 가운데 키를 둔다 — 보려는 것은 층 검사뿐이다.
        editor.SetKeys("a", "NoSuchNode", MotionChannelKind.Position, Axis.X, new[] { Fixtures.Key(0f, 0f), Fixtures.Key(0.5f, 5f), Fixtures.Key(1f, 0f) });

        Assert.Contains("전환에 없는 층", Assert.Single(MotionValidator.Validate(editor.Document)).Message, StringComparison.Ordinal);
    }
}

public class MotionFixesTests
{
    private static readonly CurveRef Y = new("MoverY", MotionChannelKind.Position, Axis.Y);

    [Fact]
    public void 문구에서_돕는_손을_고른다()
    {
        Assert.Equal(new[] { MotionFix.EndKeysToDuration, MotionFix.DurationToLastKey, MotionFix.KeyAtDuration }, MotionFixes.Suggest(new MotionProblem("a", "채널 0: Y: 마지막 키 시각이 길이와 다르다 (0.5 ≠ 1)")));
        Assert.Empty(MotionFixes.Suggest(new MotionProblem("a", "같은 이름이 둘이다")));
    }

    [Fact]
    public void 끝_키를_길이로_옮기면_문제가_사라지고_되돌리기_한_번이다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        editor.SetDuration("nod", 0.5f); // 마지막 키 0.6 ≠ 0.5
        Assert.NotEmpty(MotionValidator.Validate(editor.Document));
        long before = editor.Revision;

        Assert.True(MotionFixes.Apply(editor, "nod", MotionFix.EndKeysToDuration));

        Assert.Equal(before + 1, editor.Revision);
        Assert.Empty(MotionValidator.Validate(editor.Document));
        Assert.Equal(0.5f, Y.Resolve(editor.Document.Find("nod")!)!.Keys[^1].T, 4);
        Assert.False(MotionFixes.Apply(editor, "nod", MotionFix.EndKeysToDuration)); // 바꿀 것이 없다
    }

    [Fact]
    public void 길이_자리에_키_넣기는_곡선_위_값으로_키를_더하고_길이_밖_키는_지운다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        editor.SetDuration("nod", 0.8f);
        MotionProblem problem = Assert.Single(MotionValidator.Validate(editor.Document));
        Assert.Contains(MotionFix.KeyAtDuration, MotionFixes.Suggest(problem));

        Assert.True(MotionFixes.Apply(editor, "nod", MotionFix.KeyAtDuration));

        IReadOnlyList<KeyAsset> keys = Y.Resolve(editor.Document.Find("nod")!)!.Keys;
        Assert.Equal(new[] { 0f, 0.3f, 0.6f, 0.8f }, keys.Select(k => (float)Math.Round(k.T, 4)));
        Assert.Equal(0f, keys[3].V, 4); // 마지막 키 값이 이어진다
        Assert.Empty(MotionValidator.Validate(editor.Document));
        Assert.False(MotionFixes.Apply(editor, "nod", MotionFix.KeyAtDuration));

        var shorter = new MotionEditor(Fixtures.NodBowDocument());
        shorter.SetDuration("nod", 0.5f);
        Assert.True(MotionFixes.Apply(shorter, "nod", MotionFix.KeyAtDuration));
        keys = Y.Resolve(shorter.Document.Find("nod")!)!.Keys;
        Assert.Equal(new[] { 0f, 0.3f, 0.5f }, keys.Select(k => (float)Math.Round(k.T, 4)));
        // 길이를 줄이면 새 끝 키는 곡선 위 값이라 중립값이 아니다 — 그래도 문제가 없다(값은 알리지 않는다).
        Assert.Empty(MotionValidator.Validate(shorter.Document));
    }

    [Fact]
    public void 첫_키를_0초로_옮기면_겹치는_키는_사라진다()
    {
        var editor = new MotionEditor(Fixtures.NodBowDocument());
        editor.SetKeys("nod", Y.Node, Y.Kind, Y.Axis, new[] { Fixtures.Key(0.1f, 0f), Fixtures.Key(0.3f, -18f), Fixtures.Key(0.6f, 0f) });

        Assert.True(MotionFixes.Apply(editor, "nod", MotionFix.FirstKeysToZero));

        Assert.Equal(new[] { 0f, 0.3f, 0.6f }, Y.Resolve(editor.Document.Find("nod")!)!.Keys.Select(k => (float)Math.Round(k.T, 4)));
    }
}

public class ToolSettingsTests
{
    [Fact]
    public void 설정은_왕복하고_없으면_기본값이고_깨지면_기본값에_사유를_남긴다()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, ScreenTransition.Authoring.Settings.ToolSettings.FileName);

        try
        {
            var settings = ScreenTransition.Authoring.Settings.ToolSettings.Load(path);
            Assert.True(settings.SnapEnabled);
            Assert.Equal(TangentMode.Auto, settings.DefaultTangent);

            settings.SnapEnabled = false;
            settings.DefaultTangent = TangentMode.Plateau;
            settings.AutoSaveMinutes = 1;
            settings.Touch("/a/one.transition-authoring.json");
            settings.Touch("/a/two.transition-authoring.json");
            settings.Touch("/a/one.transition-authoring.json");
            settings.Save(path);

            var back = ScreenTransition.Authoring.Settings.ToolSettings.Load(path);
            Assert.False(back.SnapEnabled);
            Assert.Equal(TangentMode.Plateau, back.DefaultTangent);
            Assert.Equal(1, back.AutoSaveMinutes);
            Assert.Equal(2, back.RecentFiles.Count);
            Assert.EndsWith("one.transition-authoring.json", back.RecentFiles[0]);
            Assert.Contains("\"plateau\"", File.ReadAllText(path), StringComparison.Ordinal);

            File.WriteAllText(path, "{ broken");
            var broken = ScreenTransition.Authoring.Settings.ToolSettings.Load(path);
            Assert.True(broken.SnapEnabled);
            Assert.NotNull(broken.LoadProblem);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void 최근_파일은_여덟_개까지다()
    {
        var settings = new ScreenTransition.Authoring.Settings.ToolSettings();

        for (int i = 0; i < 12; i++)
        {
            settings.Touch($"/x/{i}.json");
        }

        Assert.Equal(ScreenTransition.Authoring.Settings.ToolSettings.MaxRecentFiles, settings.RecentFiles.Count);
        Assert.EndsWith("11.json", settings.RecentFiles[0]);
    }
}
