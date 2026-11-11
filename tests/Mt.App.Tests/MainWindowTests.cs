using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Ked.Presentation.Core;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;
using Mt.Authoring.Serialization;
using Mt.Authoring.Validation;

namespace Mt.App.Tests;

public class MainWindowTests
{
    /// <summary>테스트는 이 컴퓨터의 설정 파일을 건드리지 않는다 — 임시 경로.</summary>
    private static MainWindow NewWindow()
    {
        string path = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"settings-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ \"snapStep\": 0.016666666666666666 }"); // 이 테스트들의 기대값은 1/60 스냅 기준 — 기본값은 1/24(GraphMathTests)
        var window = new MainWindow(path);
        window.Session.Editor.AddRig(RigTemplates.Character()); // 모션들이 서는 발판 세트
        return window;
    }

    [Fact]
    public void 주_창이_헤드리스에서_열리고_그려진다()
    {
        HeadlessUi.Run(() =>
        {
            Assert.True(HeadlessUi.OnUiThread);

            MainWindow window = NewWindow();
            window.Show();

            try
            {
                Assert.Equal("ked-motion-tool", window.Title);

                var status = window.FindControl<TextBlock>("StatusText");
                Assert.NotNull(status);
                Assert.True(status!.Bounds.Width > 0, "상태 줄이 배치되지 않았다");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 뷰포트와_그래프의_현재_시각은_하나이고_뷰포트에_모션의_세트가_선다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.AddMotionFromForm("nod", "character", "0.6");

                Assert.Equal("nod", window.Viewport.MotionName);
                Assert.True(window.Viewport.HasRig);
                Assert.NotNull(window.Viewport.ObjectQuad(Mt.App.Views.ViewportView.SetObjectId));

                window.Viewport.ScrubTo(0.25);
                Assert.Equal(0.25, window.Graph.CurrentTime, 9);

                window.Graph.CurrentTime = 0.5;
                Assert.Equal(0.5, window.Viewport.Time, 9);

                // 길이 밖은 뷰포트가 길이로 죈다 — 그래프는 그대로.
                window.Graph.CurrentTime = 2;
                Assert.Equal(0.6f, window.Viewport.Time, 6); // 길이는 float(0.6f)다
                Assert.Equal(2, window.Graph.CurrentTime, 9);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 그래프와_도프시트는_선택이_하나이고_현재_시각도_하나다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.AddMotionFromForm("nod", "character", "0.6");
                var y = new CurveRef("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y);
                window.AddChannelFromForm(y.Node, y.Kind);
                window.EnsureComponent(y.Node, y.Kind, y.Axis);

                Assert.Same(window.Graph.Selection, window.Dope.Selection);
                Assert.Same(window.SharedSelection, window.Graph.Selection);
                Assert.Equal("nod", window.Dope.MotionName);
                Assert.Equal(3, window.Dope.Rows.Count);

                window.Graph.Select(new[] { new KeyRef(y, 0) }, additive: false);
                Assert.Single(window.Dope.Selection);

                // 클립보드도 하나다 — 그래프에서 복사해 도프시트에서 붙인다.
                window.Graph.CopySelection();
                window.Dope.CurrentTime = 0.25;
                window.Dope.Paste();
                Assert.Equal(2, y.Resolve(window.Session.Editor.Document.Find("nod")!)!.Keys.Count);

                window.Dope.Selection.Clear();
                Assert.Empty(window.Graph.Selection);

                window.Dope.CurrentTime = 0.2;
                Assert.Equal(0.2, window.Graph.CurrentTime, 9);
                Assert.Equal(0.2, window.Viewport.Time, 9);

                window.Viewport.ScrubTo(0.4);
                Assert.Equal(0.4, window.Dope.CurrentTime, 9);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 뷰포트_드래그만으로_키가_생기고_노드_목록이_선다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.AddMotionFromForm("wave", "character", "0.5");
                Assert.Contains("CharacterPortrait_Track_Move_X", window.Viewport.NodeChoices);
                Assert.DoesNotContain("CharSlot_Track_Focus", window.Viewport.NodeChoices);

                window.Viewport.SelectedNode = "CharacterPortrait_Track_Move_X";
                window.Viewport.Manipulator = ManipulatorMode.Move;
                window.AutoKeyChecked = true;
                Assert.True(window.Viewport.AutoKey);

                window.Viewport.ScrubTo(0.25);
                Point center = window.Viewport.TranslatePoint(window.Viewport.GizmoCenter()!.Value, window)!.Value;
                var target = new Point(center.X + 40, center.Y);
                window.MouseDown(center, MouseButton.Left);
                window.MouseMove(target, RawInputModifiers.LeftMouseButton);
                window.MouseUp(target, MouseButton.Left);

                ChannelAsset channel = window.Session.Editor.Document.Find("wave")!.FindChannel("CharacterPortrait_Track_Move_X", MotionChannelKind.Position)!;
                Assert.Single(channel.X!.Keys);
                Assert.True(channel.X.Keys[0].V > 0);
                Assert.Equal(3, window.Dope.Rows.Count);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 문제_행의_돕는_손이_문제를_없애고_설정은_파일로_남고_자동_저장은_문서_옆이다()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string settingsPath = Path.Combine(directory, "settings.json");

        HeadlessUi.Run(() =>
        {
            var window = new MainWindow(settingsPath);
            window.Session.Editor.AddRig(RigTemplates.Character());
            window.Show();

            try
            {
                window.AddMotionFromForm("nod", "character", "0.5");
                var y = new CurveRef("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y);
                window.AddChannelFromForm(y.Node, y.Kind);
                window.Session.Editor.SetKeys("nod", y.Node, y.Kind, y.Axis, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(0.3f, -18f), TestDocuments.Key(0.6f, 0f) });

                Assert.True(window.ProblemRowsShown >= 1); // 마지막 키 0.6 ≠ 길이 0.5
                Assert.True(window.ApplyFix("nod", Mt.Authoring.Validation.MotionFix.DurationToLastKey));
                Assert.Equal(0, window.ProblemRowsShown);
                Assert.Equal(0.6f, window.Session.Editor.Document.Find("nod")!.Duration, 4);

                // 설정: 스냅을 끄고 창을 닫으면 파일에 남는다.
                window.Settings.SnapEnabled = false;
                string documentPath = Path.Combine(directory, "doc.motion-authoring.json");
                window.Session.Save(documentPath);
                window.Session.Editor.SetDuration("nod", 0.7f);
                string? autosaved = window.Session.AutoSave();
                Assert.NotNull(autosaved);
                Assert.Equal(Path.Combine(directory, "doc.autosave.motion-authoring.json"), autosaved);
                Assert.True(File.Exists(autosaved!));
                Assert.True(window.Session.IsDirty);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(File.Exists(settingsPath));
        Assert.Contains("\"snapEnabled\": false", File.ReadAllText(settingsPath), StringComparison.Ordinal);
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void 세트를_열지_않아도_고른_애니메이션의_세트가_하이어아키에_선다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                var editor = window.Session.Editor;
                editor.AddRig(RigTemplates.Emoji());
                editor.AddMotion("pop", RigTemplates.EmojiKind, 0.5f);
                editor.AddMotion("nod", RigTemplates.CharacterKind, 0.5f);
                string set = Mt.App.Views.ViewportView.SetObjectId;

                window.SelectMotion("pop");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(new[] { "세트  emoji", "Emoji_Anchor", "Emoji_Body" }, window.HierarchyPathOf(set, "Emoji_Body"));

                // 다른 세트의 애니메이션을 고르면 하이어아키가 그 세트로 바뀐다.
                window.SelectMotion("nod");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Empty(window.HierarchyPathOf(set, "Emoji_Body"));
                Assert.Equal("세트  character", window.HierarchyPathOf(set, "CharacterPortrait_Track_Move")[0]);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 이름을_비우고_모션을_추가하면_motion이_되고_규칙_밖_이름은_상태줄에_남는다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.AddMotionFromForm("", "character", "0.6");
                window.AddMotionFromForm("  ", "character", "0.6");

                Assert.Equal(new[] { "motion", "motion_2" }, window.Session.Editor.Document.Motions.Select(m => m.Name));
                Assert.DoesNotContain(window.ProblemsShown, p => p.Message.Contains("이름은", StringComparison.Ordinal));

                Assert.Throws<InvalidOperationException>(() => window.AddMotionFromForm("Nod", "character", "0.6"));
                Assert.Equal(2, window.Session.Editor.Document.Motions.Count);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 전역_단축키_이전_다음_키_프레임_복사_붙여넣기_지우기_인비트윈()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.AddMotionFromForm("nod", "character", "0.6");
                var y = new CurveRef("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y);
                window.AddChannelFromForm(y.Node, y.Kind);
                window.Session.Editor.SetKeys("nod", y.Node, y.Kind, y.Axis, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(0.3f, -18f), TestDocuments.Key(0.6f, 0f) });
                MotionAsset Nod() => window.Session.Editor.Document.Find("nod")!;

                // 2 다음 키 · 1 이전 키 (물리 키로 — 포커스가 어디 있든 듣는다)
                window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.None);
                Assert.Equal(0.3, window.Viewport.Time, 4);
                window.NextKey();
                Assert.Equal(0.6, window.Viewport.Time, 4);
                window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.None);
                Assert.Equal(0.3, window.Viewport.Time, 4);

                // Alt+2 / Alt+1 한 프레임(24fps)
                window.StepFrame(+1);
                Assert.Equal(0.3 + 1.0 / 24.0, window.Viewport.Time, 4);
                window.StepFrame(-1);
                Assert.Equal(0.3, window.Viewport.Time, 4);

                // Shift+W 복사 → 0.45로 가서 Shift+E 붙여넣기
                window.CopyKeysAtCurrentTime();
                window.Viewport.ScrubTo(0.45);
                window.PasteKeysAtCurrentTime();
                Assert.Equal(4, y.Resolve(Nod())!.Keys.Count);
                Assert.Equal(-18f, y.Resolve(Nod())!.Keys[2].V);

                // Shift+F 지우기
                window.ClearKeysAtCurrentTime();
                Assert.Equal(3, y.Resolve(Nod())!.Keys.Count);

                // Shift+2 인비트윈 넣기 / Shift+1 빼기 — 0.3 뒤의 키만 움직인다
                window.Viewport.ScrubTo(0.3);
                window.AddInbetween();
                Assert.Equal(0.6f + 1f / 24f, y.Resolve(Nod())!.Keys[2].T, 4);
                window.RemoveInbetween();
                Assert.Equal(0.6f, y.Resolve(Nod())!.Keys[2].T, 4);
                Assert.Equal(0.3f, y.Resolve(Nod())!.Keys[1].T, 4);

                // Alt+Q 재생 토글
                window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.Alt);
                Assert.True(window.Viewport.IsPlaying);
                window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.Alt);
                Assert.False(window.Viewport.IsPlaying);

                // ` 첫 프레임 · Alt+` 처음부터 재생(토글이 아니다 — 재생 중에 누르면 처음으로 되감는다) · Alt+Q 토글
                window.Viewport.ScrubTo(0.3);
                window.KeyPressQwerty(PhysicalKey.Backquote, RawInputModifiers.None);
                Assert.Equal(0, window.Viewport.Time, 6);
                window.Viewport.ScrubTo(0.3);
                window.KeyPressQwerty(PhysicalKey.Backquote, RawInputModifiers.Alt);
                Assert.True(window.Viewport.IsPlaying);
                Assert.InRange(window.Viewport.Time, 0, 0.15); // 처음부터(0.3이 아니라) — 재생 시계가 이미 한 틱 갔을 수 있다
                window.KeyPressQwerty(PhysicalKey.Backquote, RawInputModifiers.Alt);
                Assert.True(window.Viewport.IsPlaying);
                window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.Alt);
                Assert.False(window.Viewport.IsPlaying);

                // W/E/R/Q 매니퓰레이터 — 창 어디에 포커스가 있든.
                window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.None);
                Assert.Equal(ManipulatorMode.Rotate, window.Manipulator);
                window.KeyPressQwerty(PhysicalKey.R, RawInputModifiers.None);
                Assert.Equal(ManipulatorMode.Scale, window.Manipulator);
                window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None);
                Assert.Equal(ManipulatorMode.Move, window.Manipulator);
                window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.None);
                Assert.Equal(ManipulatorMode.Select, window.Manipulator);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 되돌리기는_툴바_없이도_편집기를_지난다_그리고_문제는_아래에_보인다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.AddMotionFromForm("a", "character", "1");
                Assert.NotNull(window.Session.Editor.Document.Find("a"));

                // 채널이 없는 모션은 유효하다(채널은 첫 키에서 생긴다) — 문제 칸에 아무것도 없다. 길이를 0으로 하면 보인다.
                Assert.DoesNotContain(window.ProblemsShown, p => p.MotionName == "a");
                window.Session.Editor.SetDuration("a", 0f);
                Assert.Contains(window.ProblemsShown, p => p.MotionName == "a");
                window.Session.Editor.Undo();

                window.Session.Editor.Undo();
                Assert.Null(window.Session.Editor.Document.Find("a"));
                Assert.Null(window.Graph.MotionName);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 인스펙터는_탭_셋이고_마지막_탭은_설정에_남는다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                Assert.Equal(3, window.InspectorTabCount); // 채널 박스 · 세트 · 모션
                Assert.Equal(0, window.InspectorTab);
                window.InspectorTab = 2;
                Assert.Equal(2, window.Settings.InspectorTab);
                window.InspectorTab = 0;
            }
            finally
            {
                window.Close();
            }
        });
    }

    // 설정 창·단축키 창은 헤드리스 세션(창 하나 전제)에서 두 번째 창을 열면 UI 스레드가 멈춰 여기서 열지 않는다 — 데스크톱에서 확인한다.

    [Fact]
    public void Ctrl_Z_Y는_창_어디에_포커스가_있든_한_번만_돌고_글_입력_칸에서는_문서를_안_건드린다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.AddMotionFromForm("nod", "character", "0.6");
                var editor = window.Session.Editor;

                foreach (Control focus in new Control[] { window.Graph, window.Dope, window.Viewport, window.Hierarchy })
                {
                    editor.SetDuration("nod", 1.5f);
                    long after = editor.Revision;
                    focus.Focus();

                    window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
                    Assert.Equal(0.6f, editor.Document.Find("nod")!.Duration);
                    Assert.Equal(after + 1, editor.Revision); // 정확히 한 번(두 번이면 +2)

                    window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.Control);
                    Assert.Equal(1.5f, editor.Document.Find("nod")!.Duration);
                    Assert.Equal(after + 2, editor.Revision);

                    window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
                    window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control | RawInputModifiers.Shift); // Ctrl+Shift+Z = 다시하기
                    Assert.Equal(1.5f, editor.Document.Find("nod")!.Duration);
                    Assert.Equal(after + 4, editor.Revision);
                }

                // 글 입력 칸에 포커스가 있으면 Ctrl+Z는 글자 되돌리기 — 문서는 그대로.
                long revision = editor.Revision;
                TextBox box = window.RangeStartBox; // 하단 바 — 항상 보인다
                Assert.True(box.Focus());
                window.KeyTextInput("5");
                window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
                Assert.Equal(revision, editor.Revision);
                Assert.Equal(1.5f, editor.Document.Find("nod")!.Duration);
                Assert.True(box.IsFocused);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 값_칸은_Enter_한_번이_되돌리기_한_번이고_포커스가_떠나도_같은_편집을_다시_기록하지_않는다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.AddMotionFromForm("nod", "character", "0.6");
                var editor = window.Session.Editor;
                var y = new CurveRef("CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y);
                editor.AddChannel("nod", y.Node, y.Kind);
                editor.SetKeys("nod", y.Node, y.Kind, y.Axis, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(0.3f, -18f), TestDocuments.Key(0.6f, 0f) });
                float ValueAt1() => y.Resolve(editor.Document.Find("nod")!)!.Keys[1].V;

                window.Graph.Select(new[] { new KeyRef(y, 1) }, additive: false);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                TextBox box = window.KeyValueBox;
                Assert.True(box.IsEnabled);

                // 값 입력 → Enter: 되돌리기 한 칸. Enter 뒤 포커스는 칸 밖이라 이어지는 Ctrl+Z는 문서 되돌리기다.
                long before = editor.Revision;
                box.Focus();
                box.Text = "7";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal(7f, ValueAt1());
                Assert.Equal(before + 1, editor.Revision);
                Assert.False(box.IsFocused);

                // 다른 패널 클릭(포커스 이동)으로는 같은 편집이 다시 기록되지 않는다.
                box.Focus();
                window.Graph.Focus();
                Assert.Equal(before + 1, editor.Revision);

                window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
                Assert.Equal(-18f, ValueAt1());
                Assert.True(editor.CanRedo);

                // 되돌린 뒤 같은 값을 확정하거나 포커스만 옮겨도 다시하기가 남는다.
                window.Graph.Select(new[] { new KeyRef(y, 1) }, additive: false);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                box.Focus();
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                box.Focus();
                window.Graph.Focus();
                Assert.Equal(-18f, ValueAt1());
                Assert.True(editor.CanRedo);

                // 실제 새 편집은 다시하기를 지운다.
                box.Focus();
                box.Text = "+=2";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal(-16f, ValueAt1());
                Assert.False(editor.CanRedo);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 키_없는_포즈_여러_성분을_S로_확정하면_되돌리기_한_칸이다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.AddMotionFromForm("step", "character", "0.6");
                var editor = window.Session.Editor;
                MotionAsset Step() => editor.Document.Find("step")!;
                window.AutoKeyChecked = false;
                window.Viewport.ScrubTo(0.25);
                window.SelectInHierarchy(Mt.App.Views.ViewportView.SetObjectId, "CharacterPortrait_Track_Move");

                // 좌우·상하 두 성분을 키 없는 포즈로 두고 S — 키 확정은 한 칸이고, 되돌리면 둘 다 키 없는 포즈로 돌아온다.
                window.SetChannelBoxValue(MotionChannelKind.Position, Axis.X, "10");
                window.SetChannelBoxValue(MotionChannelKind.Position, Axis.Y, "-5");
                Assert.True(window.Viewport.HasPending);
                Assert.Null(Step().FindChannel("CharacterPortrait_Track_Move", MotionChannelKind.Position));
                long before = editor.Revision;
                window.SetKeyFromShell();
                ChannelAsset body = Step().FindChannel("CharacterPortrait_Track_Move", MotionChannelKind.Position)!;
                Assert.Equal(10f, body.X!.Keys.Single().V, 3);
                Assert.Equal(-5f, body.Y!.Keys.Single().V, 3);
                Assert.Equal(before + 1, editor.Revision);
                editor.Undo();
                Assert.Null(Step().FindChannel("CharacterPortrait_Track_Move", MotionChannelKind.Position));
                Assert.True(window.Viewport.HasPending);
                editor.Redo();
                Assert.False(window.Viewport.HasPending);
                Assert.Equal(10f, Step().FindChannel("CharacterPortrait_Track_Move", MotionChannelKind.Position)!.X!.Keys.Single().V, 3);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
