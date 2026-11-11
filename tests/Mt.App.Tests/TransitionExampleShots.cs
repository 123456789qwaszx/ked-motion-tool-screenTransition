using Avalonia.Headless;
using Mt.App;

namespace Mt.App.Tests;

/// <summary>
/// 화면 이동 예시(<c>examples/transitions/station</c>)를 앱에서 열어 장면을 그림으로 뽑는다 — <c>MT_RENDER_DIR=폴더 dotnet test --filter TransitionExampleShots</c>.
/// 폴더가 없으면 아무것도 하지 않는다(보통의 테스트 실행).
/// </summary>
public class TransitionExampleShots
{
    [Fact]
    public void 화면_이동_예시의_수명을_시간별로_뽑는다()
    {
        if (RenderShots.Directory is not { } directory)
        {
            return;
        }

        HeadlessUi.Run(() =>
        {
            string settings = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"settings-{Guid.NewGuid():N}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
            File.WriteAllText(settings, "{}");
            var window = new MainWindow(settings) { Width = 1500, Height = 950 };
            window.Show();

            try
            {
                DirectoryInfo? root = new(AppContext.BaseDirectory);

                while (root is not null && !File.Exists(Path.Combine(root.FullName, "KedMotionTool.sln")))
                {
                    root = root.Parent;
                }

                window.Session.Open(Path.Combine(root!.FullName, "examples", "transitions", "station", "station.motion-authoring.json"));
                Directory.CreateDirectory(directory);

                void Shot(string name)
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using FileStream stream = File.Create(Path.Combine(directory, name + ".png"));
                    window.CaptureRenderedFrame()?.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                }

                // 연출을 본다 — 그 세트가 무대에 선다.
                window.InspectorTab = 3;
                window.SelectEmoji("station");

                foreach (double t in new[] { 0.1, 0.3, 0.6, 0.9, 1.2, 1.7, 2.5, 2.8, 3.05 })
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    window.Viewport.ScrubTo(t);
                    Shot($"station-t{t:0.00}");
                }

                // 무대만 보기 — 무대 밖을 가린다(게임에서 보이는 그대로).
                window.StageOnlyChecked = true;
                window.Viewport.ScrubTo(0.6);
                Shot("station-stage-only");
                window.StageOnlyChecked = false;

                // 기둥과 노을.
                window.Session.Open(Path.Combine(root!.FullName, "examples", "transitions", "pillar", "pillar.motion-authoring.json"));
                window.InspectorTab = 3;
                window.SelectEmoji("pillar");
                window.StageOnlyChecked = true;

                foreach (double t in new[] { 0.3, 0.6, 0.9, 1.2, 1.5, 2.3, 3.2, 3.6, 3.95 })
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    window.Viewport.ScrubTo(t);
                    Shot($"pillar-t{t:0.00}");
                }

                window.StageOnlyChecked = false;

                // 세트를 열고 등장 애니메이션을 고른다 — 하이어아키의 층, 그래프의 커브.
                window.SelectSet("pillar");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.SelectMotion("pillar_in");
                window.SelectSetNode("sun");
                window.InspectorTab = 2;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Viewport.ScrubTo(1.2);
                Shot("pillar-set");
                window.SelectSet(null);

                // 나머지 세 장면 — 수명을 따라.
                foreach (string name in new[] { "lapis", "heredity", "mercury" })
                {
                    window.Session.Open(Path.Combine(root!.FullName, "examples", "transitions", name, name + ".motion-authoring.json"));
                    window.InspectorTab = 3;
                    window.SelectEmoji(name);
                    window.StageOnlyChecked = true;

                    foreach (double t in new[] { 0.5, 0.9, 1.5, 2.4, 3.5 })
                    {
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                        window.Viewport.ScrubTo(t);
                        Shot($"{name}-t{t:0.00}");
                    }

                    window.StageOnlyChecked = false;
                }

                window.Session.Open(Path.Combine(root!.FullName, "examples", "transitions", "station", "station.motion-authoring.json"));

                // 세트를 연다 — 배치 모드.
                window.SelectSet("station");
                window.InspectorTab = 1;
                Shot("station-set");
            }
            finally
            {
                window.Close();
            }
        });
    }
}
