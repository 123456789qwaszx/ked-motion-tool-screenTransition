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

/// <summary>스킨과 조각의 화면(<c>PLAN-parts.md</c> P1) — 조각마다 그림을 주면 그 조각만 그려진다.</summary>
public class SkinShellTests
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
                window.Session.Editor.AddRig(RigTemplates.Emoji());
                MotionEditor editor = window.Session.Editor;
                string id = window.PlaceRig(RigTemplates.EmojiKind);
                window.AddSkinFromForm();
                editor.SetSkinPart("skin", "Emoji_Part1", "art/sweat.png");
                editor.AddMotion("vanish", RigTemplates.EmojiKind, 1f, MotionEnd.Commit);
                window.SelectMotion("vanish");
                window.Viewport.SelectedObjectId = id;
                window.Viewport.SnapEnabled = false;
                window.InspectorTab = 2; // 모션 탭
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
                Assert.Equal(0.5f, window.Viewport.NodeOpacity(id, "Emoji_Part1")!.Value, 2);
                Assert.Equal(1f, window.Viewport.NodeOpacity(id, "Emoji_Part2")!.Value, 3);
                Assert.Equal("0.5", window.AlphaBox.Box.Text);
                window.Viewport.ScrubTo(1.0);
                Assert.Equal(0f, window.Viewport.NodeOpacity(id, "Emoji_Part1")!.Value, 3);

                // 묶음(Body)의 불투명도는 조각의 것과 곱해진다.
                window.Viewport.SelectedNode = "Emoji_Body";
                window.Viewport.ScrubTo(0.0);
                window.Viewport.SetAlphaValue("Emoji_Body", 0.5f);
                window.Viewport.ScrubTo(1.0);
                window.Viewport.SetAlphaValue("Emoji_Body", 0.5f);
                window.Viewport.ScrubTo(0.5);
                Assert.Equal(0.25f, window.Viewport.NodeOpacity(id, "Emoji_Part1")!.Value, 2);
                Assert.Equal(0.5f, window.Viewport.NodeOpacity(id, "Emoji_Part2")!.Value, 2);

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
    public void 스킨의_그림_넘기기와_놓임이_뷰포트에_그대로_보인다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.Editor.AddRig(RigTemplates.Emoji());
                MotionEditor editor = window.Session.Editor;
                string id = window.PlaceRig(RigTemplates.EmojiKind);
                window.AddSkinFromForm();
                window.InspectorTab = 1; // 세트 탭
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // 그림 칸에 ' | '로 이어 적으면 장이 여럿 — 한 칸.
                long revision = editor.Revision;
                TextBox images = window.SkinPartBox("Emoji_Part1")!;
                images.Focus();
                images.Text = "art/a.png | art/b.png | art/c.png";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal(new[] { "art/a.png", "art/b.png", "art/c.png" }, editor.Document.FindSkin("skin")!.ImagesOf("Emoji_Part1"));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal("art/a.png | art/b.png | art/c.png", window.SkinPartBox("Emoji_Part1")!.Text);
                Assert.Null(window.SkinSetupBoxes("Emoji_Part2")); // 그림이 없는 조각에는 덧붙임 칸이 없다

                // 시계를 따라 넘어간다(장당 0.2초).
                editor.AddMotion("still", RigTemplates.EmojiKind, 1f);
                window.SelectMotion("still");
                window.Viewport.SelectedObjectId = id;
                window.Viewport.ScrubTo(0.0);
                Assert.Equal("art/a.png", window.Viewport.PartImage(id, "Emoji_Part1"));
                window.Viewport.ScrubTo(0.25);
                Assert.Equal("art/b.png", window.Viewport.PartImage(id, "Emoji_Part1"));
                window.Viewport.ScrubTo(0.5);
                Assert.Equal("art/c.png", window.Viewport.PartImage(id, "Emoji_Part1"));

                // 놓임 칸(X) — 한 칸, 조각이 그만큼 옮겨 그려진다. 노드의 값은 그대로다(모션과 따로).
                Quad before = window.Viewport.PartQuad(id, "Emoji_Part1")!.Value;
                var boxes = window.SkinSetupBoxes("Emoji_Part1")!;
                revision = editor.Revision;
                boxes.X.Focus();
                boxes.X.Text = "50";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal(50f, editor.Document.FindSkin("skin")!.SetupOf("Emoji_Part1").X);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Quad placed = window.Viewport.PartQuad(id, "Emoji_Part1")!.Value;
                // 놓임은 조각 노드의 px이다 — 화면에서는 부모들의 배율(캐릭터의 깊이 배율 등)이 곱해진다: 조각의 폭이 곱해진 그만큼.
                float perPixel = (before.BottomRight.X - before.BottomLeft.X) / RigTemplates.DefaultPartSize;
                Assert.Equal(before.Center.X + 50f * perPixel, placed.Center.X, 2);
                Assert.Equal(0f, window.Viewport.CurrentPose()!.GetState($"{id}/Emoji_Part1").AnchoredPosition.X, 3);

                // 크기 0.5 — 사각형이 반으로.
                SkinPartSetup half = editor.Document.FindSkin("skin")!.SetupOf("Emoji_Part1").Clone();
                half.Scale = 0.5f;
                editor.SetSkinPartSetup("skin", "Emoji_Part1", half);
                Quad small = window.Viewport.PartQuad(id, "Emoji_Part1")!.Value;
                Assert.Equal((before.BottomRight.X - before.BottomLeft.X) / 2f, small.BottomRight.X - small.BottomLeft.X, 2);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 이모지_정의를_보며_조각을_끌면_스킨의_놓임이_한_칸으로_바뀐다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                MotionEditor editor = window.Session.Editor;
                string id = window.PlaceRig(RigTemplates.EmojiKind);
                window.InspectorTab = 3;
                window.AddEmojiFromForm();
                window.NewEmojiSkinFromForm();
                editor.SetSkinPartImages("emoji", "Emoji_Part1", new[] { "art/a.png" });
                editor.SetSkinPartImages("emoji", "Emoji_Part2", new[] { "art/b.png" });
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Viewport.ScrubTo(0.05);
                ViewportView view = window.Viewport;
                SkinAsset skin = editor.Document.FindSkin("emoji")!;

                Point At(Vec2 root) => view.TranslatePoint(view.ToScreen(root), window) ?? throw new InvalidOperationException("뷰가 창에 붙어 있지 않다");
                Quad Part(string node) => view.PartQuad(id, node)!.Value;

                // 두 조각이 겹쳐 있다 — 위에 그려진 것(Part2)이 집힌다. 끌면 그만큼 놓임이 바뀐다: 한 칸, 모션의 키는 안 생긴다.
                long revision = editor.Revision;
                Quad before = Part("Emoji_Part2");
                float perPixel = (before.BottomRight.X - before.BottomLeft.X) / RigTemplates.DefaultPartSize; // 노드의 px → 루트 공간
                Vec2 grab = before.Center;
                var drop = new Vec2(grab.X + 60f * perPixel, grab.Y + 30f * perPixel);
                window.MouseDown(At(grab), MouseButton.Left);
                window.MouseMove(At(new Vec2(grab.X + 20f, grab.Y)), RawInputModifiers.LeftMouseButton);
                Assert.Equal(revision, editor.Revision); // 끄는 동안에는 기록이 없다(그림만 따라온다)
                Assert.True(Part("Emoji_Part2").Center.X > before.Center.X);
                window.MouseMove(At(drop), RawInputModifiers.LeftMouseButton);
                window.MouseUp(At(drop), MouseButton.Left);
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal((60f, 30f), (skin.SetupOf("Emoji_Part2").X, skin.SetupOf("Emoji_Part2").Y));
                Assert.Equal(0f, skin.SetupOf("Emoji_Part1").X);
                Assert.Equal(("emoji", "Emoji_Part2"), view.PlacedPart);
                Assert.Empty(editor.Document.Motions);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal("60", window.EmojiSkinSetupBoxes("Emoji_Part2")!.X.Text); // 칸이 따라온다

                // 옮겨 간 Part2가 안 덮는 쪽(왼쪽)에서 집으면 Part1이다. Shift+끌기 = 크기(가운데에서의 거리 비).
                Quad one = Part("Emoji_Part1");
                var edge = new Vec2(one.Center.X - 60f * perPixel, one.Center.Y);
                var half = new Vec2(one.Center.X - 30f * perPixel, one.Center.Y);
                window.MouseDown(At(edge), MouseButton.Left, RawInputModifiers.Shift);
                window.MouseMove(At(half), RawInputModifiers.LeftMouseButton | RawInputModifiers.Shift);
                window.MouseUp(At(half), MouseButton.Left, RawInputModifiers.Shift);
                Assert.Equal(0.5f, skin.SetupOf("Emoji_Part1").Scale, 2);
                Assert.Equal(revision + 2, editor.Revision);

                // Ctrl+끌기 = 회전(가운데 둘레) — 왼쪽에서 아래로 4분의 1바퀴 = 반시계 +90도.
                one = Part("Emoji_Part1");
                var right = new Vec2(one.Center.X - 30f * perPixel, one.Center.Y);
                var up = new Vec2(one.Center.X, one.Center.Y - 30f * perPixel);
                window.MouseDown(At(right), MouseButton.Left, RawInputModifiers.Control);
                window.MouseMove(At(up), RawInputModifiers.LeftMouseButton | RawInputModifiers.Control);
                window.MouseUp(At(up), MouseButton.Left, RawInputModifiers.Control);
                Assert.Equal(90f, skin.SetupOf("Emoji_Part1").Rotation, 0);

                // 제자리에서 눌렀다 떼면 기록이 없다. 빈 자리를 누르면 집은 것이 풀린다.
                revision = editor.Revision;
                Vec2 two = Part("Emoji_Part2").Center;
                window.MouseDown(At(two), MouseButton.Left);
                window.MouseUp(At(two), MouseButton.Left);
                Assert.Equal(revision, editor.Revision);
                var empty = new Vec2(two.X - 900f, two.Y - 400f);
                window.MouseDown(At(empty), MouseButton.Left);
                window.MouseUp(At(empty), MouseButton.Left);
                Assert.Null(view.PlacedPart);

                // 되돌리면 놓임이 돌아간다.
                editor.Undo();
                editor.Undo();
                editor.Undo();
                Assert.Empty(editor.Document.FindSkin("emoji")!.Setups); // 되돌리기는 문서를 갈아 끼운다 — 다시 찾는다
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 이모지를_보면서_구간의_모션을_고치면_그래프가_그_모션을_보이고_키가_그_모션에_놓인다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                MotionEditor editor = window.Session.Editor;
                string id = window.PlaceRig(RigTemplates.EmojiKind);
                KeyAsset Linear(float t, float v) => new() { T = t, V = v, Tangent = TangentMode.Linear };
                editor.AddMotion("pop", RigTemplates.EmojiKind, 0.2f, MotionEnd.Commit, MotionChannelMode.Absolute);
                editor.AddChannel("pop", "Emoji_Body", MotionChannelKind.Position);
                editor.SetKeys("pop", "Emoji_Body", MotionChannelKind.Position, Axis.Y, new[] { Linear(0f, 0f), Linear(0.2f, 40f) });
                editor.AddMotion("bob", RigTemplates.EmojiKind, 0.4f);
                editor.AddChannel("bob", "Emoji_Body", MotionChannelKind.Position);
                editor.SetKeys("bob", "Emoji_Body", MotionChannelKind.Position, Axis.Y, new[] { Linear(0f, 0f), Linear(0.2f, 10f), Linear(0.4f, 0f) });
                editor.AddEmoji("sweat", RigTemplates.EmojiKind);
                editor.UpdateEmoji("sweat", new EmojiAsset { Enter = "pop", Hold = "bob", HoldSeconds = 1f });
                editor.AddEmoji("other", RigTemplates.EmojiKind);
                editor.UpdateEmoji("other", new EmojiAsset { Hold = "bob", HoldSeconds = 1f });
                window.InspectorTab = 3;
                window.SelectEmoji("sweat");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Viewport.SnapEnabled = false;
                Vec2 Body() => window.Viewport.CurrentPose()!.GetState($"{id}/Emoji_Body").AnchoredPosition;

                // 구간을 안 골랐으면 타임 슬라이더에 세 구간의 키가 다 보인다(구간마다 색이 다르다): 등장 pop의 0 · 0.2, 머묾 bob이 바퀴마다. 퇴장은 없다.
                Assert.Equal(new[] { (EmojiPhase.Enter, 0.0), (EmojiPhase.Enter, 0.2) }, window.Viewport.EmojiKeyTimes().Where(k => k.Phase == EmojiPhase.Enter).Select(k => (k.Phase, Math.Round(k.Time, 3))));
                Assert.Equal(8, window.Viewport.EmojiKeyTimes().Count(k => k.Phase == EmojiPhase.Hold));
                Assert.DoesNotContain(window.Viewport.EmojiKeyTimes(), k => k.Phase == EmojiPhase.Exit);
                Assert.Empty(window.Viewport.ContextKeyTimes());

                // 구간을 안 골랐으면 그래프는 비고 포즈를 안 잡는다(조각을 끌어 놓는 쪽).
                Assert.Null(window.Graph.MotionName);
                Assert.Null(window.Viewport.EditMotion);
                Assert.Equal((EmojiPhase?)null, window.EmojiPhaseState.Phase);

                // 머묾을 고친다 — 이모지는 고른 채, 그래프에 bob이 뜨고 시계가 그 구간의 처음으로 간다.
                window.EditEmojiPhase(EmojiPhase.Hold);
                Assert.Equal("sweat", window.SelectedEmoji);
                Assert.Null(window.SelectedMotion);
                Assert.Equal("bob", window.Graph.MotionName);
                Assert.Equal("bob", window.Viewport.EditMotion?.Name);
                Assert.InRange(window.Viewport.Time, 0.2, 0.21);
                Assert.Contains("함께 쓴다: other", window.EmojiPhaseState.Note, StringComparison.Ordinal);

                // 타임 슬라이더의 키 틱 — bob의 키(0 · 0.2 · 0.4)가 머묾의 바퀴마다 시계의 자리에: 0.2부터 0.4씩, 머무는 시간(1초)이 다하는 1.2까지.
                Assert.Equal(new[] { 0.2, 0.4, 0.6, 0.6, 0.8, 1.0, 1.0, 1.2 }, window.Viewport.ContextKeyTimes().Select(t => Math.Round(t, 3)));
                Assert.True(window.EmojiPhaseState.CanMakeUnique);

                Assert.Empty(window.Viewport.EmojiKeyTimes()); // 구간을 고르면 그 구간만(빨간 틱)

                // 그래프 목록 — bob이 건드리는 것은 Body의 위치뿐이다. 나머지(다른 노드 · 배율 · 회전 · 불투명도)가 "키 없음"으로 흐리게 선다.
                Assert.DoesNotContain(("Emoji_Body", MotionChannelKind.Position), window.UnkeyedRows);
                Assert.Contains(("Emoji_Body", MotionChannelKind.Scale), window.UnkeyedRows);
                Assert.Contains(("Emoji_Body", MotionChannelKind.Alpha), window.UnkeyedRows);
                Assert.Contains(("Emoji_Part1", MotionChannelKind.Euler), window.UnkeyedRows);
                Assert.DoesNotContain(("Emoji_Anchor", MotionChannelKind.Alpha), window.UnkeyedRows); // 불투명도를 못 받는 노드

                // ＋ — 지금 값이 지금 시각(bob의 0초)의 키가 되고 채널이 생긴다: 한 칸. 줄은 "키 없음"에서 빠진다.
                long before = editor.Revision;
                window.KeyUnkeyedRow("Emoji_Body", MotionChannelKind.Scale);
                Assert.Equal(before + 1, editor.Revision);
                ChannelAsset scale = editor.Document.Find("bob")!.FindChannel("Emoji_Body", MotionChannelKind.Scale)!;
                Assert.Equal((1f, 1f), (scale.X!.Keys.Single().V, scale.Y!.Keys.Single().V));
                Assert.DoesNotContain(("Emoji_Body", MotionChannelKind.Scale), window.UnkeyedRows);
                Assert.Equal("Emoji_Body", window.Viewport.SelectedNode);
                editor.Undo();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Contains(("Emoji_Body", MotionChannelKind.Scale), window.UnkeyedRows);

                // 둘째 바퀴의 0.1초(수명 0.7초) — 그래프의 시각은 모션 기준이다.
                window.Viewport.ScrubTo(0.7);
                Assert.Equal(0.1, window.Graph.CurrentTime, 4);
                Assert.Equal(45f, Body().Y, 2); // 등장이 남긴 40 + bob의 5

                // 값을 넣고 S — 키가 bob의 0.1초에 놓인다. 시작값은 등장이 끝난 자세라 상대값 20이 들어간다(60 − 40).
                window.Viewport.SelectedNode = "Emoji_Body";
                long revision = editor.Revision;
                window.Viewport.SetComponent("Emoji_Body", MotionChannelKind.Position, Axis.Y, 60f);
                window.Viewport.SetKey();
                KeyAsset key = editor.Document.Find("bob")!.FindChannel("Emoji_Body", MotionChannelKind.Position)!.Y!.Keys.Single(k => Math.Abs(k.T - 0.1f) < 1e-4f);
                Assert.Equal(20f, key.V, 2);
                Assert.True(editor.Revision > revision);
                Assert.Equal(60f, Body().Y, 2);
                window.Viewport.ScrubTo(0.3); // 첫 바퀴의 같은 자리에도 보인다(같은 모션이 되풀이된다)
                Assert.Equal(60f, Body().Y, 2);
                Assert.Equal("sweat", window.SelectedEmoji); // 고쳐도 이모지가 풀리지 않는다
                Assert.Equal("bob", window.Graph.MotionName);

                // 그래프에서 시각을 옮기면 뷰포트가 그 바퀴 안에서 따라온다.
                window.Graph.CurrentTime = 0.2;
                Assert.Equal(0.4, window.Viewport.Time, 4);

                // 구간 밖(등장 중)에서는 손이 안 간다.
                window.Viewport.ScrubTo(0.1);
                Assert.Null(window.Viewport.EditMotion);
                window.Viewport.ScrubTo(0.7);

                // 이 이모지만의 모션으로 — 복제해 꽂고 그래프가 그것을 따라간다. 다른 이모지는 원래 것을 쓴다.
                Assert.Equal("bob_2", window.MakeEmojiPhaseUniqueFromForm());
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(("bob_2", "bob"), (editor.Document.FindEmoji("sweat")!.Hold, editor.Document.FindEmoji("other")!.Hold));
                Assert.Equal("bob_2", window.Graph.MotionName);
                Assert.Contains("이 이모지만 쓴다", window.EmojiPhaseState.Note, StringComparison.Ordinal);
                Assert.False(window.EmojiPhaseState.CanMakeUnique);

                // 등장으로 바꾸면 그래프도 바뀌고, 풀면 그래프가 빈다.
                window.EditEmojiPhase(EmojiPhase.Enter);
                Assert.Equal("pop", window.Graph.MotionName);
                Assert.InRange(window.Viewport.Time, 0.0, 0.2);
                Assert.Equal(new[] { 0.0, 0.2 }, window.Viewport.ContextKeyTimes().Select(t => Math.Round(t, 3)));
                window.EditEmojiPhase(null);
                Assert.Null(window.Graph.MotionName);
                Assert.Null(window.Viewport.EditMotion);
                Assert.Empty(window.Viewport.ContextKeyTimes());

                // 모션 목록에서 모션을 고르면 이모지를 떠난다(보통의 모션 편집).
                window.SelectMotion("pop");
                Assert.Null(window.SelectedEmoji);
                Assert.Equal("pop", window.Graph.MotionName);
                Assert.Equal("pop", window.Viewport.EditMotion?.Name);
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
                RigAsset Rig() => editor.Document.FindRig("set")!;

                // 그림 가져오기 — 오브젝트(이름은 파일에서, 크기는 그림의 것) + 자리 + 스킨의 그림이 한 칸. 스킨이 없어 세트의 이름으로 만들어 입혀 본다.
                long revision = editor.Revision;
                Assert.Equal("alert", window.AddImageObjectFromForm(art, new Vec2(100f, 50f)));
                Assert.Equal(revision + 1, editor.Revision);
                RigNodeAsset node = Rig().Find("alert")!;
                Assert.Equal(("Body", 100f, 50f, 256f, 256f, true), (node.Parent, node.X, node.Y, node.Width, node.Height, node.Image));
                Assert.Equal("set", view.SetSkin);
                Assert.EndsWith("alert.png", editor.Document.FindSkin("set")!.Parts["alert"], StringComparison.Ordinal);
                Assert.Equal("alert", window.SetEditorState.Node);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(new[] { "alert" }, view.DrawnParts(view.SelectedObject!));
                Assert.Equal("alert_2", window.AddImageObjectFromForm(art, new Vec2(-200f, 0f))); // 같은 이름이면 번호가 붙는다
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                Point At(Vec2 p) => view.TranslatePoint(view.ToScreen(p), window) ?? throw new InvalidOperationException("뷰가 창에 붙어 있지 않다");
                Vec2 Center(string id) => view.PartQuad(ViewportView.SetObjectId, id)!.Value.Center;

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
                Assert.Empty(editor.Document.Motions);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal("160", window.RigNodeBoxes.X.Text);

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
                Assert.Equal(("90", "0.5"), (window.RigNodePoseBoxes.Rotation.Text, window.RigNodePoseBoxes.ScaleX.Text));

                // 칸에서도 고친다.
                window.RigNodePoseBoxes.Rotation.Focus();
                window.RigNodePoseBoxes.Rotation.Text = "15";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal(15f, Rig().Find("alert")!.Rotation);

                // 제자리에서 눌렀다 떼면 고르기만 한다(기록 없음).
                revision = editor.Revision;
                Vec2 other = Center("alert_2");
                window.MouseDown(At(other), MouseButton.Left);
                window.MouseUp(At(other), MouseButton.Left);
                Assert.Equal(revision, editor.Revision);
                Assert.Equal("alert_2", window.SetEditorState.Node);

                // 그 세트의 애니메이션을 고르면 배치 모드가 아니다 — 끌기는 키가 된다(전부터 있던 길).
                editor.AddMotion("in", "set", 0.5f);
                window.SelectMotion("in");
                Assert.False(view.IsLayoutMode);
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
                editor.AddMotion("nod", "character", 0.6f);
                window.NewSetFromForm();
                string a = window.AddSetNodeFromForm(image: true)!;   // Part1 — Body 밑
                window.SelectSetNode("Body");
                string b = window.AddSetNodeFromForm(image: true)!;   // Part2 — Body 밑
                window.SelectSetNode(b);
                string c = window.AddSetNodeFromForm(image: true)!;   // Part3 — Part2 밑
                editor.AddSkin("skin", "set");
                foreach (string part in new[] { a, b, c })
                {
                    editor.SetSkinPart("skin", part, $"art/{part}.png");
                }

                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                IReadOnlyList<string> Drawn() => view.DrawnParts(view.SelectedObject!);
                Assert.Equal(new[] { a, b, c }, Drawn());

                // 목록은 이 세트의 애니메이션만 — 캐릭터의 nod는 안 보인다. "모션 추가"는 이 세트의 것을 만든다.
                Assert.Equal(("애니메이션 — 세트 set", 0), (window.MotionListState.Header, window.MotionListState.Names.Count));
                window.AddMotionFromButton();
                Assert.Equal("set", editor.Document.Find("motion")!.Rig);
                Assert.Equal(new[] { "motion" }, window.MotionListState.Names);
                Assert.Equal("motion", window.SelectedMotion);
                Assert.Equal("set", window.SelectedSet);  // 세트는 열린 채다
                Assert.False(view.IsLayoutMode);          // 애니메이션 모드

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
                editor.AddChannel("motion", a, MotionChannelKind.Position);
                editor.SetKeys("motion", a, MotionChannelKind.Position, Axis.X, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(0.2f, 0f), TestDocuments.Key(0.6f, 50f), TestDocuments.Key(1f, 50f) });
                editor.AddChannel("motion", b, MotionChannelKind.Position);
                editor.SetKeys("motion", b, MotionChannelKind.Position, Axis.X, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(0.2f, 0f), TestDocuments.Key(0.6f, 50f), TestDocuments.Key(1f, 50f) });
                float SecondKey(string node) => editor.Document.Find("motion")!.FindChannel(node, MotionChannelKind.Position)!.X!.Keys[1].T;
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

                // 세트를 닫으면 따로 보기가 풀리고 목록이 전부로 돌아온다.
                window.SetSetNodeViewFromMenu(a, solo: null, hidden: true);
                window.SelectSet(null);
                Assert.Equal("모션", window.MotionListState.Header);
                Assert.Equal(new[] { "nod", "motion" }, window.MotionListState.Names);
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
    public void 색_사각형을_더하면_스킨_없이_세트에_서고_끌어서_옮기고_칸에서_색을_바꾼다()
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
                RigNodeAsset Node(string id) => editor.Document.FindRig("set")!.Find(id)!;

                // 색 사각형 더하기 — 오브젝트 + 크기 + 색이 한 칸. 스킨이 없어도 선다(세트의 오브젝트 사각형이 그 색 사각형이다).
                long revision = editor.Revision;
                Assert.Equal("Color1", window.AddColorNodeFromForm());
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal(("Body", 400f, 300f, "#3D5A80", false), (Node("Color1").Parent, Node("Color1").Width, Node("Color1").Height, Node("Color1").Color, Node("Color1").Image));
                Assert.Null(view.SetSkin);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Quad quad = view.ObjectQuad(ViewportView.SetObjectId)!.Value;
                Assert.Equal(400f, quad.BottomRight.X - quad.BottomLeft.X, 1);
                Assert.Equal("#3D5A80", window.RigNodeColorBox.Text);

                // 칸에서 색을 바꾼다(Enter) — 한 칸. 틀린 글은 막히고 문서는 그대로다.
                window.RigNodeColorBox.Focus();
                window.RigNodeColorBox.Text = "ee6c4d";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal("#EE6C4D", Node("Color1").Color);
                window.RigNodeColorBox.Text = "red";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal("#EE6C4D", Node("Color1").Color);

                // 배치 모드에서 끌어 옮긴다 — 색은 그대로 남는다.
                Point At(Vec2 p) => view.TranslatePoint(view.ToScreen(p), window) ?? throw new InvalidOperationException("뷰가 창에 붙어 있지 않다");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Vec2 grab = view.ObjectQuad(ViewportView.SetObjectId)!.Value.Center;
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
                Assert.Equal("#243B6B", window.RigNodeColorBox.Text);

                // 색을 비우면 보통 오브젝트로 돌아간다.
                window.SelectSetNode("Color1");
                window.RigNodeColorBox.Focus();
                window.RigNodeColorBox.Text = "";
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
    public void 층_표를_가져오면_그림_한_묶음이_세트의_오브젝트와_스킨으로_한_칸에_선다()
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
                RigAsset rig = editor.Document.FindRig("set")!;
                Assert.Equal("#10262B", rig.Find("Backdrop")!.Color);
                Assert.Equal((1920f, 1080f, true), (rig.Find("arches")!.Width, rig.Find("arches")!.Height, rig.Find("arches")!.Image));
                Assert.Equal("set", window.Viewport.SetSkin);
                Assert.EndsWith("art/06_sign.png", editor.Document.FindSkin("set")!.Parts["sign"], StringComparison.Ordinal);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(created, window.Viewport.DrawnParts(window.Viewport.SelectedObject!));

                // 되돌리면 통째로 사라진다(처음의 Body만 남는다).
                editor.Undo();
                Assert.Equal(new[] { "Body" }, editor.Document.FindRig("set")!.Nodes.Select(n => n.Id));
                Assert.Null(editor.Document.FindSkin("set"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 세트_이름_칸에서_이름을_바꾸면_세트와_그것을_딴_이름들이_한_칸에_따라온다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                MotionEditor editor = window.Session.Editor;
                window.NewSetFromForm();
                editor.AddMotion("set_in", "set", 1f);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                long revision = editor.Revision;
                window.SetNameBox.Focus();
                window.SetNameBox.Text = "lamp";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                Assert.Equal(revision + 1, editor.Revision);
                Assert.Null(editor.Document.FindRig("set"));
                Assert.Equal("lamp", editor.Document.Find("lamp_in")!.Rig);
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
                editor.AddMotion("set_in", "set", 1f, MotionEnd.Commit);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // 애니메이션을 고르지 않았으면 할 일이 없다.
                Assert.Null(window.SlideSetNodeFromMenu("Color1", StageSide.Left, leaving: false));

                // 0.25초에서 "왼쪽에서" — 한 칸. 무대의 반(960) + 너비의 반(200) = 1160만큼 왼쪽에서 0.5초 동안 들어온다.
                window.SelectMotion("set_in");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Graph.CurrentTime = 0.25;
                long revision = editor.Revision;
                Assert.Equal(-1160f, window.SlideSetNodeFromMenu("Color1", StageSide.Left, leaving: false));
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal(new[] { (0f, -1160f), (0.25f, -1160f), (0.75f, 0f), (1f, 0f) },
                    editor.Document.Find("set_in")!.FindChannel("Color1", MotionChannelKind.Position)!.X!.Keys.Select(k => (k.T, k.V)));
                Assert.Equal(0, window.ProblemRowsShown);

                // 뷰포트: 0.1초에는 무대 밖(왼쪽), 끝에는 제자리.
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                float X(double t)
                {
                    window.Graph.CurrentTime = t;
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    return window.Viewport.CurrentPose()!.TransformPoint("set/Color1", Vec3.Zero).X;
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
