using System.Text.Json;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Serialization;

namespace Mt.Authoring.Tests;

public class MotionDocumentJsonTests
{
    [Fact]
    public void 저작_원본은_쓰고_읽으면_같은_문서다()
    {
        MotionDocument document = Fixtures.NodBowDocument();
        string first = MotionDocumentJson.Write(document);
        MotionDocument read = MotionDocumentJson.Read(first);
        string second = MotionDocumentJson.Write(read);

        Assert.Equal(first, second);
        Assert.Equal(AuthoringFormat.AuthoringSchema, read.Schema);
        Assert.Equal(document.Transitions.Select(r => r.Name), read.Transitions.Select(r => r.Name));
        Assert.DoesNotContain("\"notes\"", first, StringComparison.Ordinal); // 메모는 없다(소유자, 2026-10-28)
    }

    [Fact]
    public void 제작_정보_탄젠트_모드와_끊김은_저작_원본에_남고_열거형은_소문자다()
    {
        var document = new MotionDocument();
        var editor = new MotionEditor(document);
        editor.AddTimeline("a", 1f);
        editor.AddChannel("a", "N", MotionChannelKind.Euler);
        editor.SetKeys("a", "N", MotionChannelKind.Euler, Axis.Z, new[]
        {
            new KeyAsset { T = 0f, V = 0f, Tangent = TangentMode.Auto },
            new KeyAsset { T = 1f, V = 10f, InTangent = 3f, OutTangent = -3f, Tangent = TangentMode.Free, Broken = true },
        });

        string json = MotionDocumentJson.Write(editor.Document);

        Assert.DoesNotContain("\"end\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"previewMode\"", json, StringComparison.Ordinal);
        Assert.Contains("\"schema\": \"transition-authoring/1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"euler\"", json, StringComparison.Ordinal);
        Assert.Contains("\"tangent\": \"auto\"", json, StringComparison.Ordinal);
        Assert.Contains("\"broken\": true", json, StringComparison.Ordinal);
        // 없는 성분은 쓰지 않는다.
        Assert.DoesNotContain("\"x\"", json, StringComparison.Ordinal);

        MotionDocument read = MotionDocumentJson.Read(json);
        ChannelAsset channel = read.Find("a")!.Channels[0];
        Assert.Null(channel.X);
        Assert.Equal(TangentMode.Free, channel.Z!.Keys[1].Tangent);
        Assert.True(channel.Z.Keys[1].Broken);
    }

    [Fact]
    public void 새_탄젠트_모드도_소문자로_왕복한다()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddTimeline("a", 1f, TransitionTemplates.Figure());
        editor.AddChannel("a", "N", MotionChannelKind.Position);
        editor.SetKeys("a", "N", MotionChannelKind.Position, Axis.X, new[]
        {
            new KeyAsset { T = 0f, V = 0f, Tangent = TangentMode.Spline },
            new KeyAsset { T = 1f, V = 1f, Tangent = TangentMode.Plateau },
        });

        string json = MotionDocumentJson.Write(editor.Document);
        Assert.Contains("\"tangent\": \"spline\"", json, StringComparison.Ordinal);
        Assert.Contains("\"tangent\": \"plateau\"", json, StringComparison.Ordinal);

        List<KeyAsset> keys = MotionDocumentJson.Read(json).Find("a")!.Channels[0].X!.Keys;
        Assert.Equal(TangentMode.Spline, keys[0].Tangent);
        Assert.Equal(TangentMode.Plateau, keys[1].Tangent);
    }

    [Fact]
    public void 다른_스키마는_거부한다()
    {
        Assert.Throws<JsonException>(() => MotionDocumentJson.Read("""{ "schema": "motion-authoring/4", "rigs": [] }"""));
    }

    [Fact]
    public void 빈_성분_목록은_없음으로_읽는다()
    {
        string json = """
            { "schema": "transition-authoring/1",
              "transitions": [ { "name": "a", "layers": [], "timeline": { "duration": 1,
                             "channels": [ { "node": "N", "kind": "position", "x": { "keys": [] } } ] } } ] }
            """;

        MotionDocument read = MotionDocumentJson.Read(json);
        Assert.Null(read.Find("a")!.Channels[0].X);
    }
}
