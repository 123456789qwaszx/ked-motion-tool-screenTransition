using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using ScreenTransition.Core;
using ScreenTransition.App.Views;
using ScreenTransition.Authoring.Editing;
using ScreenTransition.Authoring.Model;
using ScreenTransition.Authoring.Preview;

namespace ScreenTransition.App.Tests;

public class ViewportViewTests
{
    private static (Window Window, ViewportView View, MotionEditor Editor) Show()
    {
        MotionEditor editor = TestDocuments.Nod();
        var view = new ViewportView { SnapStep = 1.0 / 60 }; // 기대값은 1/60 스냅 기준(기본값은 1/24)
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        view.Bind(editor, "nod");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, view, editor);
    }

    [Fact]
    public void 휠은_커서_아래_점을_고정한_채_확대이고_MMB_드래그는_보기_이동이며_F는_맞춤이다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, _) = Show();

        try
        {
            Rect before = view.StageRect;
            var anchor = new Point(before.X + before.Width * 0.25, before.Y + before.Height * 0.25);
            Vec2 under = view.ToRoot(anchor);
            Point inWindow = view.TranslatePoint(anchor, window) ?? throw new InvalidOperationException("뷰가 창에 붙어 있지 않다");

            // 휠 위로 = 확대. 커서 아래의 루트 점은 그 자리에 남는다.
            window.MouseWheel(inWindow, new Vector(0, 2));
            Assert.True(view.ViewZoom > 1);
            Assert.True(view.StageRect.Width > before.Width);
            Point after = view.ToScreen(under);
            Assert.Equal(anchor.X, after.X, 1);
            Assert.Equal(anchor.Y, after.Y, 1);

            // MMB 드래그 = 보기 이동(전환·키는 그대로).
            Vector panBefore = view.ViewPan;
            window.MouseDown(inWindow, MouseButton.Middle);
            window.MouseMove(new Point(inWindow.X + 30, inWindow.Y - 10), RawInputModifiers.MiddleMouseButton);
            window.MouseUp(new Point(inWindow.X + 30, inWindow.Y - 10), MouseButton.Middle);
            Assert.Equal(30, view.ViewPan.X - panBefore.X, 3);
            Assert.Equal(-10, view.ViewPan.Y - panBefore.Y, 3);

            // RMB 드래그도 보기 이동(그래프와 같다).
            panBefore = view.ViewPan;
            window.MouseDown(inWindow, MouseButton.Right);
            window.MouseMove(new Point(inWindow.X - 12, inWindow.Y + 8), RawInputModifiers.RightMouseButton);
            window.MouseUp(new Point(inWindow.X - 12, inWindow.Y + 8), MouseButton.Right);
            Assert.Equal(-12, view.ViewPan.X - panBefore.X, 3);
            Assert.Equal(8, view.ViewPan.Y - panBefore.Y, 3);

            // F = 맞춤(배율 1 · 이동 0) — 무대가 처음 자리로.
            view.Focus();
            window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.None);
            Assert.Equal(1, view.ViewZoom, 6);
            Assert.Equal(default, view.ViewPan);
            Assert.Equal(before, view.StageRect);

            // 범위 밖으로는 안 간다.
            for (int i = 0; i < 40; i++)
            {
                window.MouseWheel(inWindow, new Vector(0, 3));
            }

            Assert.Equal(ViewportView.MaxViewZoom, view.ViewZoom, 6);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 타임라인을_묶으면_전환이_서고_포즈가_시각을_따른다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, _) = Show();

        try
        {
            Assert.True(view.HasStage);
            Quad rest = view.TransitionQuad() ?? throw new InvalidOperationException("전환이 없다");
            Assert.Equal(0f, rest.Center.Y, 2);

            view.ScrubTo(0.3);
            Quad dipped = view.TransitionQuad()!.Value;
            Assert.Equal(-18f, dipped.Center.Y, 2);

            // 길이에서는 restore라 시작 상태.
            view.ScrubTo(0.6);
            Assert.Equal(0f, view.TransitionQuad()!.Value.Center.Y, 2);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 재생은_시계를_밀고_루프를_끄면_끝에서_멈춘다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, _) = Show();

        try
        {
            int changes = 0;
            view.TimeChanged += (_, _) => changes++;

            view.Play();
            Assert.True(view.IsPlaying);
            view.Advance(0.3);
            Assert.Equal(0.3, view.Time, 9);
            Assert.Equal(1, changes);

            view.Loop = false;
            view.Advance(1);
            Assert.Equal(0.6f, view.Time, 6); // 길이는 float(0.6f)다
            Assert.False(view.IsPlaying);

            view.Stop();
            Assert.Equal(0, view.Time);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 점은_키로_점프하고_거꾸로_재생은_범위_끝에서_처음으로_간다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, _) = Show();

        try
        {
            Assert.True(view.JumpToNextKey());
            Assert.Equal(0.3, view.Time, 6);
            Assert.True(view.JumpToNextKey());
            Assert.Equal(0.6f, view.Time, 6);
            Assert.False(view.JumpToNextKey());
            Assert.True(view.JumpToPreviousKey());
            Assert.Equal(0.3, view.Time, 6);

            view.Mode = PlaybackMode.Once;
            view.PlayBackward();
            Assert.True(view.IsPlaying);
            Assert.True(view.Clock.IsReversed);
            view.Advance(0.1);
            Assert.Equal(0.2, view.Time, 6);
            view.Advance(1);
            Assert.Equal(0, view.Time, 6);
            Assert.False(view.IsPlaying);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 이동_매니퓰레이터는_AutoKey면_놓을_때_바뀐_성분만_키로_놓는다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, MotionEditor editor) = Show();

        try
        {
            const string node = "MoverY";
            view.SelectedNode = node;
            view.Manipulator = ManipulatorMode.Move;
            view.AutoKey = true;
            view.ScrubTo(0.3);
            long before = editor.Revision;

            Point centerLocal = view.GizmoCenter() ?? throw new InvalidOperationException("기즈모가 없다");
            Point center = view.TranslatePoint(centerLocal, window)!.Value;
            double scale = view.StageRect.Width / 1920.0;
            var target = new Point(center.X, center.Y + 20);

            window.MouseDown(center, MouseButton.Left);
            window.MouseMove(target, RawInputModifiers.LeftMouseButton);
            Assert.Equal(before, editor.Revision); // 드래그 중간값은 렌더만
            window.MouseUp(target, MouseButton.Left);

            Assert.Equal(before + 1, editor.Revision);
            Assert.False(view.HasPending);
            ChannelAsset channel = editor.Document.Find("nod")!.FindChannel(node, MotionChannelKind.Position)!;
            Assert.Null(channel.X); // 세로로만 끌었으니 x는 건드리지 않는다
            KeyAsset key = channel.Y!.Keys.Single(k => Math.Abs(k.T - 0.3f) < 1e-4);
            Assert.Equal(-18.0 - 20.0 / scale, key.V, 1);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void AutoKey가_꺼져_있으면_키_없는_포즈로_남고_S가_키를_놓고_시각을_옮기면_버린다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, MotionEditor editor) = Show();

        try
        {
            const string node = "MoverY";
            view.SelectedNode = node;
            view.Manipulator = ManipulatorMode.Move;
            view.AutoKey = false;
            view.ScrubTo(0.15); // 키가 없는 시각
            long before = editor.Revision;
            int pendingChanges = 0;
            view.PendingChanged += (_, _) => pendingChanges++;

            Point center = view.TranslatePoint(view.GizmoCenter()!.Value, window)!.Value;
            var target = new Point(center.X, center.Y - 30);
            window.MouseDown(center, MouseButton.Left);
            window.MouseMove(target, RawInputModifiers.LeftMouseButton);
            window.MouseUp(target, MouseButton.Left);

            Assert.Equal(before, editor.Revision);
            Assert.True(view.HasPending);
            Assert.True(pendingChanges > 0);
            Assert.Equal(3, editor.Document.Find("nod")!.FindChannel(node, MotionChannelKind.Position)!.Y!.Keys.Count);

            view.SetKey();
            Assert.False(view.HasPending);
            Assert.Equal(before + 1, editor.Revision);
            List<KeyAsset> keys = editor.Document.Find("nod")!.FindChannel(node, MotionChannelKind.Position)!.Y!.Keys;
            Assert.Equal(4, keys.Count);
            Assert.Contains(keys, k => Math.Abs(k.T - 0.15f) < 1e-4 && k.V > -9f);

            // 다시 끌고 시각을 옮기면 버린다.
            center = view.TranslatePoint(view.GizmoCenter()!.Value, window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseMove(new Point(center.X, center.Y - 30), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(center.X, center.Y - 30), MouseButton.Left);
            Assert.False(view.HasPending); // 0.15에 이미 키가 있으니 바로 키가 된다(마야: 키 있는 시각은 덮어쓴다)
            Assert.Equal(before + 2, editor.Revision);

            view.ScrubTo(0.5);
            view.ScrubTo(0.2);
            center = view.TranslatePoint(view.GizmoCenter()!.Value, window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseMove(new Point(center.X, center.Y - 30), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(center.X, center.Y - 30), MouseButton.Left);
            Assert.True(view.HasPending);
            view.ScrubTo(0.4);
            Assert.False(view.HasPending);
            Assert.Equal(before + 2, editor.Revision);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 배율_매니퓰레이터는_팔_방향_투영이라_안으로_끌면_줄고_가운데를_넘어도_커지지_않는다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, MotionEditor editor) = Show();

        try
        {
            const string node = "Panel";
            view.SelectedNode = node;
            view.Manipulator = ManipulatorMode.Scale;
            view.AutoKey = true;
            view.ScrubTo(0.3);

            Point centerLocal = view.GizmoCenter() ?? throw new InvalidOperationException("기즈모가 없다");
            Point xArm = view.TranslatePoint(new Point(centerLocal.X + 60, centerLocal.Y), window)!.Value; // x 팔 끝(회전 없는 층 = 화면 +x)
            Point halfway = new(xArm.X - 30, xArm.Y);

            window.MouseDown(xArm, MouseButton.Left);
            window.MouseMove(halfway, RawInputModifiers.LeftMouseButton);
            window.MouseUp(halfway, MouseButton.Left);

            ChannelAsset channel = editor.Document.Find("nod")!.FindChannel(node, MotionChannelKind.Scale)!;
            Assert.Null(channel.Y); // x 팔만 끌었다
            Assert.Equal(0.5f, channel.X!.Keys.Single(k => Math.Abs(k.T - 0.3f) < 1e-4).V, 2);

            // 다시 팔 끝에서 가운데를 지나 반대쪽까지 — 거리 비였다면 다시 커졌을 것. 바닥(0.01)에 머문다.
            centerLocal = view.GizmoCenter()!.Value;
            xArm = view.TranslatePoint(new Point(centerLocal.X + 60, centerLocal.Y), window)!.Value;
            Point beyond = new(xArm.X - 150, xArm.Y);
            window.MouseDown(xArm, MouseButton.Left);
            window.MouseMove(beyond, RawInputModifiers.LeftMouseButton);
            window.MouseUp(beyond, MouseButton.Left);

            float v = channel.X!.Keys.Single(k => Math.Abs(k.T - 0.3f) < 1e-4).V;
            Assert.InRange(v, 0.001f, 0.02f);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 회전_매니퓰레이터는_고리를_끌어_z_회전_키를_놓는다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, MotionEditor editor) = Show();

        try
        {
            const string node = "Pivot";
            view.SelectedNode = node;
            view.Manipulator = ManipulatorMode.Rotate;
            view.AutoKey = true;
            view.ScrubTo(0);
            long before = editor.Revision;

            Point centerLocal = view.GizmoCenter()!.Value;
            Point onRing = view.TranslatePoint(new Point(centerLocal.X + 60, centerLocal.Y), window)!.Value;
            Point rotated = view.TranslatePoint(new Point(centerLocal.X, centerLocal.Y - 60), window)!.Value; // 화면 위 = 루트 y 위 = 반시계 90도

            window.MouseDown(onRing, MouseButton.Left);
            window.MouseMove(rotated, RawInputModifiers.LeftMouseButton);
            window.MouseUp(rotated, MouseButton.Left);

            Assert.Equal(before + 1, editor.Revision);
            ChannelAsset channel = editor.Document.Find("nod")!.FindChannel(node, MotionChannelKind.Euler)!;
            Assert.Null(channel.X);
            Assert.Null(channel.Y);
            Assert.Equal(90f, channel.Z!.Keys[0].V, 1);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 뮤트된_채널은_재생에서_빠진다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, _) = Show();

        try
        {
            var y = new ScreenTransition.Authoring.Graph.CurveRef("MoverY", MotionChannelKind.Position, ScreenTransition.Authoring.Model.Axis.Y);
            view.ScrubTo(0.3);
            Assert.Equal(-18f, view.TransitionQuad()!.Value.Center.Y, 2);

            view.Flags.SetMuted(y, true);
            Assert.Equal(0f, view.TransitionQuad()!.Value.Center.Y, 2);

            view.Flags.SetMuted(y, false);
            Assert.Equal(-18f, view.TransitionQuad()!.Value.Center.Y, 2);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 화면_좌표와_루트_좌표는_서로_되돌아온다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, _) = Show();

        try
        {
            Vec2 back = view.ToRoot(view.ToScreen(new Vec2(123f, -45f)));
            Assert.Equal(123f, back.X, 2);
            Assert.Equal(-45f, back.Y, 2);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 묶인_타임라인이_없으면_무대가_비고_그래도_그려진다() => HeadlessUi.Run(() =>
    {
        MotionEditor editor = TestDocuments.Nod();
        var view = new ViewportView { SnapStep = 1.0 / 60 }; // 기대값은 1/60 스냅 기준(기본값은 1/24)
        var window = new Window { Width = 600, Height = 400, Content = view };
        window.Show();

        try
        {
            Assert.False(view.HasStage);
            Assert.Null(view.TransitionQuad());

            view.Bind(editor, "nod");
            Assert.True(view.HasStage);
            Assert.NotNull(view.TransitionQuad());

            // 묶인 타임라인이 없으면 무대가 빈다.
            view.Bind(editor, null);
            Assert.Null(view.TransitionQuad());
            view.InvalidateVisual();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void 키_없는_포즈도_되돌리기_다시하기가_되고_S로_키한_것을_되돌리면_그_포즈로_돌아온다() => HeadlessUi.Run(() =>
    {
        (Window window, ViewportView view, MotionEditor editor) = Show();

        try
        {
            const string node = "MoverY";
            view.SelectedNode = node;
            view.Manipulator = ManipulatorMode.Move;
            view.AutoKey = false;
            view.ScrubTo(0.15); // 키가 없는 시각
            float start = view.ComponentValue(node, MotionChannelKind.Position, Axis.Y)!.Value;
            long revision = editor.Revision;

            void Drag()
            {
                Point center = view.TranslatePoint(view.GizmoCenter()!.Value, window)!.Value;
                window.MouseDown(center, MouseButton.Left);
                window.MouseMove(new Point(center.X, center.Y - 30), RawInputModifiers.LeftMouseButton);
                window.MouseUp(new Point(center.X, center.Y - 30), MouseButton.Left);
            }

            // 드래그 한 번 = 포즈 칸 하나(문서는 그대로).
            Drag();
            Assert.True(view.HasPending);
            float posed = view.ComponentValue(node, MotionChannelKind.Position, Axis.Y)!.Value;
            Assert.NotEqual(start, posed);
            Assert.Equal(revision, editor.Revision);

            editor.Undo();
            Assert.False(view.HasPending);
            Assert.Equal(start, view.ComponentValue(node, MotionChannelKind.Position, Axis.Y)!.Value, 3);
            editor.Redo();
            Assert.True(view.HasPending);
            Assert.Equal(posed, view.ComponentValue(node, MotionChannelKind.Position, Axis.Y)!.Value, 3);

            // S = 키 확정 한 칸. 되돌리면 키 확정 직전의 포즈로, 한 번 더 되돌리면 그 전으로. 다시하기는 같은 순서.
            view.SetKey();
            Assert.False(view.HasPending);
            Assert.Equal(revision + 1, editor.Revision);
            Assert.Equal(4, editor.Document.Find("nod")!.FindChannel(node, MotionChannelKind.Position)!.Y!.Keys.Count);

            editor.Undo();
            Assert.Equal(3, editor.Document.Find("nod")!.FindChannel(node, MotionChannelKind.Position)!.Y!.Keys.Count);
            Assert.True(view.HasPending);
            Assert.Equal(posed, view.ComponentValue(node, MotionChannelKind.Position, Axis.Y)!.Value, 3);
            editor.Undo();
            Assert.False(view.HasPending);
            Assert.Equal(start, view.ComponentValue(node, MotionChannelKind.Position, Axis.Y)!.Value, 3);
            editor.Redo();
            Assert.True(view.HasPending);
            editor.Redo();
            Assert.False(view.HasPending);
            Assert.Equal(4, editor.Document.Find("nod")!.FindChannel(node, MotionChannelKind.Position)!.Y!.Keys.Count);

            // Esc로 버린 포즈도, 스크럽으로 사라진 포즈도 되돌리기로 돌아온다.
            view.ScrubTo(0.2);
            Drag();
            Assert.True(view.HasPending);
            view.Focus();
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.False(view.HasPending);
            editor.Undo();
            Assert.True(view.HasPending);

            view.ScrubTo(0.25);
            Assert.False(view.HasPending);
            editor.Undo();
            Assert.True(view.HasPending);

            // 포즈를 든 채 한 문서 조작을 되돌려도 포즈는 그대로다(순서대로).
            editor.SetDuration("nod", 2f);
            editor.Undo();
            Assert.Equal(0.6f, editor.Document.Find("nod")!.Duration);
            Assert.True(view.HasPending);

            // 문서를 갈아 끼우면 이전 문서의 포즈·이력은 남지 않는다.
            view.Bind(new MotionEditor(new MotionDocument()), null);
            Assert.False(view.HasPending);
        }
        finally
        {
            window.Close();
        }
    });
}
