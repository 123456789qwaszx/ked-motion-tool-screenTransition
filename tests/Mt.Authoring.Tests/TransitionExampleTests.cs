using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;

namespace Mt.Authoring.Tests;

/// <summary>
/// 예시 문서(<c>examples/transitions</c>)가 살아 있는지 — 문제 없이 서고, 저장된 파일이 지은 것과 같고, 그림이 다 있다.
/// 예시를 고쳤으면 <c>MT_WRITE_EXAMPLES=1 dotnet test --filter TransitionExampleTests</c>로 파일을 다시 쓴다.
/// </summary>
public class TransitionExampleTests
{
    private static readonly RigSchemaFile Dump = RigSchemaFile.Load(Fixtures.PathOf("rig-schemas.json"));

    private static string ExampleDirectory(string name)
    {
        DirectoryInfo? at = new(AppContext.BaseDirectory);

        while (at is not null && !File.Exists(Path.Combine(at.FullName, "KedMotionTool.sln")))
        {
            at = at.Parent;
        }

        return Path.Combine(at?.FullName ?? throw new InvalidOperationException("저장소 뿌리를 못 찾았다"), "examples", "transitions", name);
    }

    [Fact]
    public void 화면_이동_예시는_층_표에서_세트가_서고_문제_없이_돌고_저장된_파일과_같다()
    {
        string directory = ExampleDirectory("station");
        MotionEditor editor = TransitionExamples.BuildStation(Path.Combine(directory, "art", "layers.tsv"));
        MotionDocument document = editor.Document;
        RigSchemaFile rig = RigAssets.Compose(Dump, document);

        // 층 표: 바탕색 + 층 열 장이 오브젝트 열하나로 — 뒤에서 앞으로.
        RigAsset set = document.FindRig(TransitionExamples.Station)!;
        Assert.Equal(new[] { "Backdrop", "arches", "floor", "pillars_left", "pillars_right", "train", "sign", "light", "microbe_sun", "microbe_leaf", "microbe_cell" }, set.Nodes.Select(n => n.Id));
        Assert.Equal(("#10262B", 1920f, 1080f), (set.Find("Backdrop")!.Color, set.Find("Backdrop")!.Width, set.Find("Backdrop")!.Height));
        RigNodeAsset sign = set.Find("sign")!; // 표: 왼쪽 556 · 위 56 · 889×249 → 가운데 기준 (40.5, 359.5)
        Assert.Equal((40.5f, 359.5f, 889f, 249f), (sign.X, sign.Y, sign.Width, sign.Height));

        string Problems<T>(IEnumerable<T> problems, Func<T, string> text) => string.Join("\n", problems.Select(text));
        Assert.True(!MotionValidator.Validate(document, rig.NodeIds, null, rig.AlphaNodeIds).Any(), Problems(MotionValidator.Validate(document, rig.NodeIds, null, rig.AlphaNodeIds), p => $"{p.MotionName}: {p.Message}"));
        Assert.True(!EmojiValidator.Validate(document, rig.NodeIds).Any(), Problems(EmojiValidator.Validate(document, rig.NodeIds), p => $"{p.EmojiName}: {p.Message}"));

        string path = Path.Combine(directory, "station.motion-authoring.json");
        string json = MotionDocumentJson.Write(document);

        if (Environment.GetEnvironmentVariable("MT_WRITE_EXAMPLES") == "1")
        {
            File.WriteAllText(path, json);
        }

        Assert.Equal(json, File.ReadAllText(path));
        Assert.All(document.Skins.SelectMany(s => s.AllImages()).Distinct(), image => Assert.True(File.Exists(Path.Combine(directory, image)), image));

        // 수명: 등장 1.2 › 머묾 1 › 퇴장 0.9. 등장의 가운데에서 왼쪽 기둥은 아직 들어오는 중이고, 끝나면 제자리다.
        EmojiAsset cue = document.FindEmoji(TransitionExamples.Station)!;
        Assert.Equal(3.1f, EmojiPreview.PhasesOf(document, cue).Total, 3);
        var scene = new SceneAsset { Name = "s", Objects = { new SceneObject { Id = "t", Rig = TransitionExamples.Station } } };
        RectNodeTree template = ScenePreview.Build(rig, scene);
        float rest = template.GetState("t/pillars_left").AnchoredPosition.X;
        float PillarX(double t) => EmojiPreview.Pose(template, document, cue, scene.Objects[0], t).GetState("t/pillars_left").AnchoredPosition.X;
        Assert.Equal(rest - 820f, PillarX(0.1), 1);
        Assert.InRange(PillarX(0.6), rest - 700f, rest - 100f);
        Assert.Equal(rest, PillarX(1.2), 1);
        Assert.Equal(rest, PillarX(2.0), 1);          // 머묾 — 기둥은 가만히
        Assert.True(PillarX(2.9) < rest - 400f);      // 퇴장 — 들어온 길로 물러난다

        // "covered" 이벤트가 등장의 0.25초에 — 대본이 여기서 장면을 바꾼다.
        MotionEventAsset covered = Assert.Single(document.Find("station_in")!.Events);
        Assert.Equal(("covered", 0.25f), (covered.Name, covered.T));
    }

    [Fact]
    public void 기둥과_노을_예시는_시차로_지은_층들이_차례로_솟고_저장된_파일과_같다()
    {
        string directory = ExampleDirectory("pillar");
        MotionEditor editor = TransitionExamples.BuildPillar(Path.Combine(directory, "art", "layers.tsv"));
        MotionDocument document = editor.Document;
        RigSchemaFile rig = RigAssets.Compose(Dump, document);

        // 바탕색 + 층 열둘.
        Assert.Equal(13, document.FindRig(TransitionExamples.Pillar)!.Nodes.Count);
        string Problems<T>(IEnumerable<T> problems, Func<T, string> text) => string.Join("\n", problems.Select(text));
        Assert.True(!MotionValidator.Validate(document, rig.NodeIds, null, rig.AlphaNodeIds).Any(), Problems(MotionValidator.Validate(document, rig.NodeIds, null, rig.AlphaNodeIds), p => $"{p.MotionName}: {p.Message}"));
        Assert.True(!EmojiValidator.Validate(document, rig.NodeIds).Any(), Problems(EmojiValidator.Validate(document, rig.NodeIds), p => $"{p.EmojiName}: {p.Message}"));

        string path = Path.Combine(directory, "pillar.motion-authoring.json");
        string json = MotionDocumentJson.Write(document);

        if (Environment.GetEnvironmentVariable("MT_WRITE_EXAMPLES") == "1")
        {
            File.WriteAllText(path, json);
        }

        Assert.Equal(json, File.ReadAllText(path));
        Assert.All(document.Skins.SelectMany(s => s.AllImages()).Distinct(), image => Assert.True(File.Exists(Path.Combine(directory, image)), image));

        // 시차: 지평선의 층 넷이 0.12초씩 늦게 솟기 시작한다(노을 0.2 · 먼 도시 0.32 · 가까운 도시 0.44 · 언덕 0.56).
        float StartOf(string node) => document.Find("pillar_in")!.FindChannel(node, MotionChannelKind.Position)!.Y!.Keys[1].T;
        Assert.Equal(new[] { 0.2f, 0.32f, 0.44f, 0.56f }, new[] { "haze", "city_far", "city_near", "hill" }.Select(n => MathF.Round(StartOf(n), 2)));

        // 등장의 가운데에서 노을은 이미 올라왔고 언덕은 아직 아래에 있다.
        EmojiAsset cue = document.FindEmoji(TransitionExamples.Pillar)!;
        var scene = new SceneAsset { Name = "s", Objects = { new SceneObject { Id = "t", Rig = TransitionExamples.Pillar } } };
        RectNodeTree template = ScenePreview.Build(rig, scene);
        float Offset(string node, double t) => EmojiPreview.Pose(template, document, cue, scene.Objects[0], t).GetState("t/" + node).AnchoredPosition.Y - template.GetState("t/" + node).AnchoredPosition.Y;
        Assert.True(Offset("haze", 0.7) > -1f);
        Assert.True(Offset("hill", 0.7) < -300f);
        Assert.Equal(0f, Offset("hill", 1.5), 1);

        // 해: 제 차례(0.6초) 전에는 숨어 있고, 떠올라 1.3초에 제자리에 서고, 퇴장에서 진다.
        float SunAlpha(double t)
        {
            var alphas = new Dictionary<string, float>(StringComparer.Ordinal);
            EmojiPreview.Pose(template, document, cue, scene.Objects[0], t, alphas);
            return alphas.TryGetValue("t/sun", out float a) ? a : 1f;
        }

        Assert.Equal(0f, SunAlpha(0.3), 2);
        Assert.Equal(1f, SunAlpha(1.0), 2);
        Assert.True(Offset("sun", 0.9) < -100f);
        Assert.Equal(0f, Offset("sun", 1.4), 1);
        Assert.True(Offset("sun", 1.5 + 1.5 + 0.4) < -200f);
    }

    [Theory]
    [InlineData("lapis", "orb", 0.85)]
    [InlineData("heredity", "orb,orb_swirl,crystals", 0.65)]
    [InlineData("mercury", "star", 0.7)]
    public void 나머지_세_장면은_검증을_지나고_저장된_파일과_같다(string name, string late, double turn)
    {
        string directory = ExampleDirectory(name);
        string layers = Path.Combine(directory, "art", "layers.tsv");
        MotionEditor editor = name switch
        {
            "lapis" => TransitionExamples.BuildLapis(layers),
            "heredity" => TransitionExamples.BuildHeredity(layers),
            _ => TransitionExamples.BuildMercury(layers),
        };
        MotionDocument document = editor.Document;
        RigSchemaFile rig = RigAssets.Compose(Dump, document);

        string Problems<T>(IEnumerable<T> problems, Func<T, string> text) => string.Join("\n", problems.Select(text));
        Assert.True(!MotionValidator.Validate(document, rig.NodeIds, null, rig.AlphaNodeIds).Any(), Problems(MotionValidator.Validate(document, rig.NodeIds, null, rig.AlphaNodeIds), p => $"{p.MotionName}: {p.Message}"));
        Assert.True(!EmojiValidator.Validate(document, rig.NodeIds).Any(), Problems(EmojiValidator.Validate(document, rig.NodeIds), p => $"{p.EmojiName}: {p.Message}"));

        string path = Path.Combine(directory, name + ".motion-authoring.json");
        string json = MotionDocumentJson.Write(document);

        if (Environment.GetEnvironmentVariable("MT_WRITE_EXAMPLES") == "1")
        {
            File.WriteAllText(path, json);
        }

        Assert.Equal(json, File.ReadAllText(path));
        Assert.All(document.Skins.SelectMany(s => s.AllImages()).Distinct(), image => Assert.True(File.Exists(Path.Combine(directory, image)), image));

        // 늦게 나오는 층은 제 차례 전에 숨어 있고("제 차례 전까지 숨기기"), 등장이 끝나면 다 보인다.
        EmojiAsset cue = document.FindEmoji(name)!;
        var scene = new SceneAsset { Name = "s", Objects = { new SceneObject { Id = "t", Rig = name } } };
        RectNodeTree template = ScenePreview.Build(rig, scene);

        foreach (string node in late.Split(','))
        {
            var early = new Dictionary<string, float>(StringComparer.Ordinal);
            EmojiPreview.Pose(template, document, cue, scene.Objects[0], turn - 0.1, early);
            Assert.Equal(0f, early["t/" + node], 2);
            var shown = new Dictionary<string, float>(StringComparer.Ordinal);
            EmojiPreview.Pose(template, document, cue, scene.Objects[0], document.Find(name + "_in")!.Duration, shown);
            Assert.Equal(1f, shown.TryGetValue("t/" + node, out float alpha) ? alpha : 1f, 2);
        }
    }
}
