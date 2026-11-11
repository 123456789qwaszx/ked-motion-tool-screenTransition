using Avalonia;
using Mt.App.Views;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;

namespace Mt.App.Tests;

/// <summary>세트의 화면 — 층(그림 · 색 사각형)을 짓고, 배치하고, 따로 보고, 움직인다.</summary>
public class SetShellTests
{
    private static MainWindow NewWindow()
    {
        string path = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"settings-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
        return new(path);
    }

    [Fact]
    public void 불투명도_칸에_값을_넣으면_알파_채널의_키가_되고_조각이_그만큼_흐려진다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                MotionEditor editor = window.Session.Editor;
                editor.AddTimeline("vanish", 1f, TransitionTemplates.Emoji());
                window.SelectSet("vanish");
                window.Viewport.SnapEnabled = false;
                window.InspectorTab = 2; // 타임라인 탭
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // 불투명도를 받을 수 없는 노드(Anchor)를 고르면 칸이 없다. 조각을 고르면 선다.
                window.Viewport.SelectedNode = "Emoji_Anchor";
                window.Viewport.ScrubTo(0.0);
                Assert.False(window.AlphaBox.Visible);
                window.Viewport.SelectedNode = "Emoji_Part1";
                window.Viewport.ScrubTo(0.0001);
                window.Viewport.ScrubTo(0.0);
                Assert.True(window.AlphaBox.Visible);
                Assert.Equal("1", window.AlphaBox.Box.Text);

                // 처음에 1, 끝에 0 — 값을 넣고 Enter가 그 시각의 키(한 칸). 없던 알파 채널이 생긴다.
                window.Viewport.SetAlphaValue("Emoji_Part1", 1f);
                window.Viewport.ScrubTo(1.0);
                long revision = editor.Revision;
                TextBox box = window.AlphaBox.Box;
                box.Focus();
                box.Text = "0";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal(revision + 1, editor.Revision);
                ChannelAsset channel = Assert.Single(editor.Document.Find("vanish")!.Channels);
                Assert.Equal(("Emoji_Part1", MotionChannelKind.Alpha), (channel.Node, channel.Kind));
                Assert.Equal(new[] { (0f, 1f), (1f, 0f) }, channel.X!.Keys.Select(k => (k.T, k.V)));
                Assert.Equal(0, window.ProblemRowsShown);

                // 뷰포트: 가운데에서 절반, 끝에서 0(안 그려진다). 다른 조각은 그대로 1.
                window.Viewport.ScrubTo(0.5);
                Assert.Equal(0.5f, window.Viewport.NodeOpacity("Emoji_Part1")!.Value, 2);
                Assert.Equal(1f, window.Viewport.NodeOpacity("Emoji_Part2")!.Value, 3);
                Assert.Equal("0.5", window.AlphaBox.Box.Text);
                window.Viewport.ScrubTo(1.0);
                Assert.Equal(0f, window.Viewport.NodeOpacity("Emoji_Part1")!.Value, 3);

                // 묶음(Body)의 불투명도는 조각의 것과 곱해진다.
                window.Viewport.SelectedNode = "Emoji_Body";
                window.Viewport.ScrubTo(0.0);
                window.Viewport.SetAlphaValue("Emoji_Body", 0.5f);
                window.Viewport.ScrubTo(1.0);
                window.Viewport.SetAlphaValue("Emoji_Body", 0.5f);
                window.Viewport.ScrubTo(0.5);
                Assert.Equal(0.25f, window.Viewport.NodeOpacity("Emoji_Part1")!.Value, 2);
                Assert.Equal(0.5f, window.Viewport.NodeOpacity("Emoji_Part2")!.Value, 2);

                // 범위 밖 값은 0~1로 죈다. 캐릭터의 노드(받을 수 없는 것)에는 안 놓인다.
                window.Viewport.SetAlphaValue("Emoji_Body", 7f);
                Assert.All(editor.Document.Find("vanish")!.FindChannel("Emoji_Body", MotionChannelKind.Alpha)!.X!.Keys, k => Assert.InRange(k.V, 0f, 1f));
                revision = editor.Revision;
                window.Viewport.SetAlphaValue("Emoji_Anchor", 0.3f);
                Assert.Equal(revision, editor.Revision);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 배치_모드에서_그림을_가져와_놓고_끌어서_가만히_선_자세를_잡는다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                MotionEditor editor = window.Session.Editor;
                ViewportView view = window.Viewport;
                DirectoryInfo? root = new(AppContext.BaseDirectory);

                while (root is not null && !File.Exists(Path.Combine(root.FullName, "KedMotionTool.sln")))
                {
                    root = root.Parent;
                }

                string art = Path.Combine(root!.FullName, "tests", "Mt.App.Tests", "Fixtures", "alert.png");
                window.NewSetFromForm();
                window.InspectorTab = 1;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(view.IsLayoutMode);
                TransitionAsset Rig() => editor.Document.FindTransition("set")!;

                // 그림 가져오기 — 오브젝트(이름은 파일에서, 크기는 그림의 것) + 자리 + 그림이 한 칸.
                long revision = editor.Revision;
                Assert.Equal("alert", window.AddImageObjectFromForm(art, new Vec2(100f, 50f)));
                Assert.Equal(revision + 1, editor.Revision);
                LayerAsset node = Rig().Find("alert")!;
                Assert.Equal(("Body", 100f, 50f, 256f, 256f), (node.Parent, node.X, node.Y, node.Width, node.Height));
                Assert.EndsWith("alert.png", node.Image, StringComparison.Ordinal);
                Assert.Equal("alert", window.SetEditorState.Node);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(new[] { "alert" }, view.DrawnParts());
                Assert.Equal("alert_2", window.AddImageObjectFromForm(art, new Vec2(-200f, 0f))); // 같은 이름이면 번호가 붙는다
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                Point At(Vec2 p) => view.TranslatePoint(view.ToScreen(p), window) ?? throw new InvalidOperationException("뷰가 창에 붙어 있지 않다");
                Vec2 Center(string id) => view.PartQuad(id)!.Value.Center;

                // 끌기 = 자리. 집은 것이 하이어아키·칸에서도 골라진다. 끈 하나가 한 칸이고 끄는 동안에는 기록이 없다. 모션은 안 생긴다.
                revision = editor.Revision;
                Vec2 grab = Center("alert");
                var drop = new Vec2(grab.X + 60f, grab.Y - 40f);
                window.MouseDown(At(grab), MouseButton.Left);
                Assert.Equal("alert", window.SetEditorState.Node);
                window.MouseMove(At(new Vec2(grab.X + 20f, grab.Y)), RawInputModifiers.LeftMouseButton);
                Assert.Equal(revision, editor.Revision);
                Assert.True(Center("alert").X > grab.X + 10f);
                window.MouseMove(At(drop), RawInputModifiers.LeftMouseButton);
                window.MouseUp(At(drop), MouseButton.Left);
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal((160f, 10f), (Rig().Find("alert")!.X, Rig().Find("alert")!.Y));
                Assert.Empty(editor.Document.Find("set")!.Channels); // 키가 아니다
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal("160", window.LayerBoxes.X.Text);

                // Shift+끌기 = 배율(원점에서의 거리 비), Ctrl+끌기 = 회전.
                Vec2 c = Center("alert");
                window.MouseDown(At(new Vec2(c.X + 100f, c.Y)), MouseButton.Left, RawInputModifiers.Shift);
                window.MouseMove(At(new Vec2(c.X + 50f, c.Y)), RawInputModifiers.LeftMouseButton | RawInputModifiers.Shift);
                window.MouseUp(At(new Vec2(c.X + 50f, c.Y)), MouseButton.Left, RawInputModifiers.Shift);
                Assert.Equal((0.5f, 0.5f), (Rig().Find("alert")!.ScaleX, Rig().Find("alert")!.ScaleY));
                c = Center("alert");
                window.MouseDown(At(new Vec2(c.X + 40f, c.Y)), MouseButton.Left, RawInputModifiers.Control);
                window.MouseMove(At(new Vec2(c.X, c.Y + 40f)), RawInputModifiers.LeftMouseButton | RawInputModifiers.Control);
                window.MouseUp(At(new Vec2(c.X, c.Y + 40f)), MouseButton.Left, RawInputModifiers.Control);
                Assert.Equal(90f, Rig().Find("alert")!.Rotation, 0);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(("90", "0.5"), (window.LayerPoseBoxes.Rotation.Text, window.LayerPoseBoxes.ScaleX.Text));

                // 칸에서도 고친다.
                window.LayerPoseBoxes.Rotation.Focus();
                window.LayerPoseBoxes.Rotation.Text = "15";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal(15f, Rig().Find("alert")!.Rotation);

                // 제자리에서 눌렀다 떼면 고르기만 한다(기록 없음).
                revision = editor.Revision;
                Vec2 other = Center("alert_2");
                window.MouseDown(At(other), MouseButton.Left);
                window.MouseUp(At(other), MouseButton.Left);
                Assert.Equal(revision, editor.Revision);
                Assert.Equal("alert_2", window.SetEditorState.Node);

                // 그림 칸 — 고른 오브젝트의 그림 경로. 비우고 Enter면 그림 오브젝트가 아니다(한 칸), 다시 적으면 돌아온다.
                TextBox imageBox = window.LayerBoxes.Image;
                string path = imageBox.Text!;
                Assert.EndsWith("alert.png", path, StringComparison.Ordinal);
                revision = editor.Revision;
                imageBox.Focus();
                imageBox.Text = "";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Null(Rig().Find("alert_2")!.Image);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(new[] { "alert" }, view.DrawnParts());
                imageBox.Focus();
                imageBox.Text = path;
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal(path, Rig().Find("alert_2")!.Image);

                // 배치를 끄면 배치 모드가 아니다 — 끌기는 세트의 타임라인의 키가 된다.
                Assert.True(view.IsLayoutMode);
                window.LayoutModeChecked = false;
                Assert.False(view.IsLayoutMode);
                Assert.Equal("set", window.Graph.MotionName);
                Assert.Equal("set", window.SelectedSet);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 세트를_열면_그_세트의_애니메이션만_보이고_오브젝트를_따로_본다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                MotionEditor editor = window.Session.Editor;
                ViewportView view = window.Viewport;
                editor.AddTimeline("nod", 0.6f, TransitionTemplates.Figure());
                window.NewSetFromForm();
                string a = window.AddSetNodeFromForm()!;   // Node1 — Body 밑
                window.SelectSetNode("Body");
                string b = window.AddSetNodeFromForm()!;   // Node2 — Body 밑
                window.SelectSetNode(b);
                string c = window.AddSetNodeFromForm()!;   // Node3 — Node2 밑
                foreach (string part in new[] { a, b, c })
                {
                    editor.UpdateLayer("set", part, new LayerAsset { Width = 100f, Height = 100f, Image = $"art/{part}.png" });
                }

                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                IReadOnlyList<string> Drawn() => view.DrawnParts();
                Assert.Equal(new[] { a, b, c }, Drawn());

                // 세트를 고르면 그 타임라인이 묶인다. 배치를 끄면 애니메이션 모드.
                window.LayoutModeChecked = false;
                Assert.Equal("set", window.Graph.MotionName);
                Assert.False(view.IsLayoutMode);

                // 이것만 보기 — 그 오브젝트와 그 밑의 것만 그려진다. 문서는 안 바뀐다.
                long revision = editor.Revision;
                window.SetSetNodeViewFromMenu(b, solo: true, hidden: null);
                Assert.Equal(new[] { b, c }, Drawn());
                Assert.False(view.IsSetNodeShown(a));

                // 솔로 안에서 숨기기 — 밑의 것만 숨긴다.
                window.SetSetNodeViewFromMenu(c, solo: null, hidden: true);
                Assert.Equal(new[] { b }, Drawn());

                // 솔로를 풀면 숨긴 것만 빠진다. 부모를 숨기면 밑의 것도 안 보인다.
                window.SetSetNodeViewFromMenu(b, solo: false, hidden: null);
                Assert.Equal(new[] { a, b }, Drawn());
                window.SetSetNodeViewFromMenu("Body", solo: null, hidden: true);
                Assert.Empty(Drawn());

                // 움직임 밀기 · 차례로(시차) — 고른 애니메이션에서 오브젝트의 키를 통째로. 각각 한 칸.
                editor.AddChannel("set", a, MotionChannelKind.Position);
                editor.SetKeys("set", a, MotionChannelKind.Position, Axis.X, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(0.2f, 0f), TestDocuments.Key(0.6f, 50f), TestDocuments.Key(1f, 50f) });
                editor.AddChannel("set", b, MotionChannelKind.Position);
                editor.SetKeys("set", b, MotionChannelKind.Position, Axis.X, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(0.2f, 0f), TestDocuments.Key(0.6f, 50f), TestDocuments.Key(1f, 50f) });
                float SecondKey(string node) => editor.Document.Find("set")!.FindChannel(node, MotionChannelKind.Position)!.X!.Keys[1].T;
                long before = editor.Revision;
                window.ShiftSetNodeKeysFromMenu(a, 0.1f);
                Assert.Equal(before + 1, editor.Revision);
                Assert.Equal(0.3f, SecondKey(a), 3);
                editor.Undo();
                before = editor.Revision;
                window.StaggerSetNodesFromMenu(a, 0.1f); // a는 그대로, 그 뒤의 형제 b가 0.1초 늦게
                Assert.Equal(before + 1, editor.Revision);
                Assert.Equal((0.2f, 0.3f), (MathF.Round(SecondKey(a), 3), MathF.Round(SecondKey(b), 3)));

                // 무대만 보기 — 보기 상태(문서는 그대로).
                Assert.False(view.StageOnly);
                window.StageOnlyChecked = true;
                Assert.True(view.StageOnly);
                window.StageOnlyChecked = false;

                // 전부 보이기.
                window.SetSetNodeViewFromMenu(null, null, null);
                Assert.Equal(new[] { a, b, c }, Drawn());

                // 세트를 닫으면 따로 보기가 풀리고 타임라인도 풀린다.
                window.SetSetNodeViewFromMenu(a, solo: null, hidden: true);
                window.SelectSet(null);
                Assert.Null(window.Graph.MotionName);
                window.SelectSet("set");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(new[] { a, b, c }, Drawn());
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 색_사각형을_더하면_그림_없이_세트에_서고_끌어서_옮기고_칸에서_색을_바꾼다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                MotionEditor editor = window.Session.Editor;
                ViewportView view = window.Viewport;
                window.NewSetFromForm();
                window.InspectorTab = 1;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                LayerAsset Node(string id) => editor.Document.FindTransition("set")!.Find(id)!;

                // 색 사각형 더하기 — 오브젝트 + 크기 + 색이 한 칸. 그림이 없어도 선다(세트의 오브젝트 사각형이 그 색 사각형이다).
                long revision = editor.Revision;
                Assert.Equal("Color1", window.AddColorNodeFromForm());
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal(("Body", 400f, 300f, "#3D5A80"), (Node("Color1").Parent, Node("Color1").Width, Node("Color1").Height, Node("Color1").Color));
                Assert.Null(Node("Color1").Image);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Quad quad = view.SetQuad()!.Value;
                Assert.Equal(400f, quad.BottomRight.X - quad.BottomLeft.X, 1);
                Assert.Equal("#3D5A80", window.LayerColorBox.Text);

                // 칸에서 색을 바꾼다(Enter) — 한 칸. 틀린 글은 막히고 문서는 그대로다.
                window.LayerColorBox.Focus();
                window.LayerColorBox.Text = "ee6c4d";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal("#EE6C4D", Node("Color1").Color);
                window.LayerColorBox.Text = "red";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal("#EE6C4D", Node("Color1").Color);

                // 배치 모드에서 끌어 옮긴다 — 색은 그대로 남는다.
                Point At(Vec2 p) => view.TranslatePoint(view.ToScreen(p), window) ?? throw new InvalidOperationException("뷰가 창에 붙어 있지 않다");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Vec2 grab = view.SetQuad()!.Value.Center;
                var drop = new Vec2(grab.X + 50f, grab.Y + 20f);
                window.MouseDown(At(grab), MouseButton.Left);
                window.MouseMove(At(drop), RawInputModifiers.LeftMouseButton);
                window.MouseUp(At(drop), MouseButton.Left);
                Assert.Equal((50f, 20f, "#EE6C4D"), (Node("Color1").X, Node("Color1").Y, Node("Color1").Color));

                // 색 견본 — 이 문서에서 쓴 색이 먼저 서고, 고르면 바로 들어간다(한 칸).
                window.SelectSetNode("Color1");
                Assert.Equal("#EE6C4D", window.ColorSwatches()[0]);
                Assert.Contains("#243B6B", window.ColorSwatches());
                revision = editor.Revision;
                window.PickColorSwatch("#243B6B");
                Assert.Equal("#243B6B", Node("Color1").Color);
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal("#243B6B", window.LayerColorBox.Text);

                // 색을 비우면 보통 오브젝트로 돌아간다.
                window.SelectSetNode("Color1");
                window.LayerColorBox.Focus();
                window.LayerColorBox.Text = "";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Null(Node("Color1").Color);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 층_표를_가져오면_그림_한_묶음이_세트의_그림_오브젝트로_한_칸에_선다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                MotionEditor editor = window.Session.Editor;
                DirectoryInfo? root = new(AppContext.BaseDirectory);

                while (root is not null && !File.Exists(Path.Combine(root.FullName, "KedMotionTool.sln")))
                {
                    root = root.Parent;
                }

                string sheet = Path.Combine(root!.FullName, "examples", "transitions", "station", "art", "layers.tsv");
                Assert.Empty(window.ImportLayerSheetFromForm(sheet)); // 연 세트가 없으면 아무 일도 없다
                window.NewSetFromForm();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // 한 칸에: 바탕색 + 층 열 장, 스킨까지. 뷰포트의 세트에 바로 선다.
                long revision = editor.Revision;
                IReadOnlyList<string> created = window.ImportLayerSheetFromForm(sheet);
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal(10, created.Count);
                TransitionAsset rig = editor.Document.FindTransition("set")!;
                Assert.Equal("#10262B", rig.Find("Backdrop")!.Color);
                Assert.Equal((1920f, 1080f), (rig.Find("arches")!.Width, rig.Find("arches")!.Height));
                Assert.EndsWith("art/06_sign.png", rig.Find("sign")!.Image, StringComparison.Ordinal);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(created, window.Viewport.DrawnParts());

                // 되돌리면 통째로 사라진다(처음의 Body만 남는다).
                editor.Undo();
                Assert.Equal(new[] { "Body" }, editor.Document.FindTransition("set")!.Layers.Select(n => n.Id));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 세트_이름_칸에서_이름을_바꾸면_세트와_타임라인이_한_칸에_따라온다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                MotionEditor editor = window.Session.Editor;
                window.NewSetFromForm();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                long revision = editor.Revision;
                window.SetNameBox.Focus();
                window.SetNameBox.Text = "lamp";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                Assert.Equal(revision + 1, editor.Revision);
                Assert.Null(editor.Document.FindTransition("set"));
                Assert.Equal("lamp", editor.Document.Find("lamp")!.Name); // 타임라인의 이름도 함께
                Assert.Equal("lamp", window.Graph.MotionName);
                Assert.Equal("lamp", window.SelectedSet); // 연 세트는 그대로 열려 있다
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 하이어아키_메뉴의_화면_밖에서_들어온다는_지금_시각부터_자리_키를_놓는다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                MotionEditor editor = window.Session.Editor;
                window.NewSetFromForm();
                window.SelectSetNode(null);
                window.AddColorNodeFromForm(); // Color1 — 400×300, 무대 가운데
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // 0.25초에서 "왼쪽에서" — 한 칸. 무대의 반(960) + 너비의 반(200) = 1160만큼 왼쪽에서 0.5초 동안 들어온다.
                window.LayoutModeChecked = false;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Graph.CurrentTime = 0.25;
                long revision = editor.Revision;
                Assert.Equal(-1160f, window.SlideSetNodeFromMenu("Color1", StageSide.Left, leaving: false));
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal(new[] { (0f, -1160f), (0.25f, -1160f), (0.75f, 0f), (1f, 0f) },
                    editor.Document.Find("set")!.FindChannel("Color1", MotionChannelKind.Position)!.X!.Keys.Select(k => (k.T, k.V)));
                Assert.Equal(0, window.ProblemRowsShown);

                // 뷰포트: 0.1초에는 무대 밖(왼쪽), 끝에는 제자리.
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                float X(double t)
                {
                    window.Graph.CurrentTime = t;
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    return window.Viewport.CurrentPose()!.TransformPoint("Color1", Vec3.Zero).X;
                }

                Assert.Equal(-1160f, X(0.1) - X(1.0), 0);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
