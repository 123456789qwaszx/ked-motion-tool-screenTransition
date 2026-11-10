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
    public void 조각이_있는_리그는_스킨이_그림을_준_조각만_그리고_캐릭터는_전과_같다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(EmojiRigFixture.WriteDump(TestDocuments.FixturePath("rig-schemas.json")));
                MotionEditor editor = window.Session.Editor;
                editor.AddSceneObject(null, "e1", EmojiRigFixture.RigKind);
                window.InspectorTab = 1; // 씬 탭
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                SceneObject Emoji() => editor.Document.ActiveSceneAsset!.Find("e1")!;

                // 캐릭터를 고르면 스킨 칸이 없다 — 초상 한 장의 리그다.
                window.Viewport.SelectedObjectId = "s1";
                Assert.False(window.SkinFormState.Visible);
                Assert.NotNull(window.Viewport.CurrentPortraitQuad());
                Quad character = window.Viewport.ObjectQuad("s1")!.Value;

                // 이모지를 고르면 스킨 칸이 선다. 스킨이 없으면 조각은 하나도 안 그려진다.
                window.Viewport.SelectedObjectId = "e1";
                Assert.Equal((true, (string?)null, 0), window.SkinFormState);
                Assert.Empty(window.Viewport.DrawnParts(Emoji()));

                // 스킨 추가 — 만들어서 입힌다, 한 칸. 조각 줄 넷이 선다.
                long revision = editor.Revision;
                Assert.Equal("skin", window.AddSkinFromForm());
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal("skin", Emoji().Skin);
                Assert.Equal((true, "skin", 4), window.SkinFormState);
                Assert.Empty(window.Viewport.DrawnParts(Emoji())); // 그림을 준 조각이 아직 없다

                // 조각 둘에 그림(경로 칸에 넣고 Enter) — 그 둘만 그려진다. 파일이 없어도 자리표시로 그린다.
                TextBox part1 = window.SkinPartBox("Emoji_Part1")!;
                part1.Focus();
                part1.Text = "art/flower-big.png";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                editor.SetSkinPart("skin", "Emoji_Part3", "art/flower-small.png");
                Assert.Equal(new[] { "Emoji_Part1", "Emoji_Part3" }, window.Viewport.DrawnParts(Emoji()));
                Assert.Equal("art/flower-big.png", editor.Document.FindSkin("skin")!.Parts["Emoji_Part1"]);
                Assert.Equal("art/flower-big.png", window.SkinPartBox("Emoji_Part1")!.Text);

                // 조각은 노드의 rect(160×160)로, 무대 가운데에 선다.
                Quad quad = window.Viewport.PartQuad("e1", "Emoji_Part1")!.Value;
                Assert.Equal(EmojiRigFixture.PartSize, quad.BottomRight.X - quad.BottomLeft.X, 2);
                Assert.Equal(EmojiRigFixture.PartSize, quad.TopLeft.Y - quad.BottomLeft.Y, 2);
                Assert.Equal(0f, quad.Center.X, 2);
                Assert.Null(window.Viewport.PartQuad("e1", "Nope"));

                // 조각마다 따로 움직인다 — 모션은 그 리그의 보통 모션이다.
                editor.AddMotion("float", EmojiRigFixture.RigKind, 1f, MotionEnd.Commit, MotionChannelMode.Absolute);
                editor.AddChannel("float", "Emoji_Part1", MotionChannelKind.Position);
                editor.SetKeys("float", "Emoji_Part1", MotionChannelKind.Position, Axis.X, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(1f, 100f) });
                window.SelectMotion("float");
                window.Viewport.SelectedObjectId = "e1";
                window.Viewport.ScrubTo(1.0);
                Assert.Equal(100f, window.Viewport.PartQuad("e1", "Emoji_Part1")!.Value.Center.X, 2);
                Assert.Equal(0f, window.Viewport.PartQuad("e1", "Emoji_Part3")!.Value.Center.X, 2);
                Assert.Equal(0, window.ProblemRowsShown); // 이모지 리그의 노드로 검증된다

                // 고르는 자리(오브젝트 사각형)는 첫 조각이다. 캐릭터의 사각형은 그대로다.
                Assert.Equal(100f, window.Viewport.ObjectQuad("e1")!.Value.Center.X, 2);
                Assert.Equal(character, window.Viewport.ObjectQuad("s1")!.Value);

                // 스킨을 벗기면 다시 안 그려지고, 스킨을 지우면 입은 오브젝트가 벗는다.
                editor.SetObjectSkin(null, "e1", null);
                Assert.Empty(window.Viewport.DrawnParts(Emoji()));
                editor.Undo();
                Assert.Equal(2, window.Viewport.DrawnParts(Emoji()).Count);
                editor.RemoveSkin("skin");
                Assert.Null(Emoji().Skin);
                Assert.Equal((true, (string?)null, 0), window.SkinFormState);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 붙는_리그를_만들면_부모_밑에_서서_따라간다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(EmojiRigFixture.WriteDump(TestDocuments.FixturePath("rig-schemas.json")));
                MotionEditor editor = window.Session.Editor;
                SceneAsset Scene() => editor.Document.ActiveSceneAsset!;
                Assert.Contains(EmojiRigFixture.RigKind, window.AttachableRigs);
                Assert.DoesNotContain("character", window.AttachableRigs);

                // 붙는 리그 만들기(하이어아키 메뉴와 같은 길) — 만들고 붙이는 것이 한 칸, 만든 것이 골라진다.
                long revision = editor.Revision;
                string id = window.CreateAttachedFromMenu(EmojiRigFixture.RigKind, "s1");
                Assert.Equal("emoji1", id);
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal(("s1", SceneObject.DefaultAttachNode), (Scene().Find(id)!.AttachTo, Scene().Find(id)!.AttachNode));
                Assert.Equal(id, window.Viewport.SelectedObjectId);
                Assert.Equal(2, window.HierarchyObjectCount);
                Assert.True(window.SkinFormState.Visible);

                // 스킨을 입히고 조각 하나에 그림 — 캐릭터 머리 근처가 아니라 붙는 노드의 가운데에 선다(자리는 오프셋으로 옮긴다).
                window.AddSkinFromForm();
                editor.SetSkinPart("skin", "Emoji_Part1", "art/sweat.png");
                Quad rest = window.Viewport.PartQuad(id, "Emoji_Part1")!.Value;

                // 부모를 옮기면 따라간다 — 캐릭터의 그림이 간 만큼(깊이 배율이 곱해진 화면 거리) 그대로.
                float characterRest = window.Viewport.ObjectQuad("s1")!.Value.Center.X;
                editor.SetObjectPlacement(null, "s1", 150f, 0f, 0f, 1f, 1f);
                float characterMoved = window.Viewport.ObjectQuad("s1")!.Value.Center.X - characterRest;
                Assert.True(characterMoved > 100f);
                Assert.Equal(rest.Center.X + characterMoved, window.Viewport.PartQuad(id, "Emoji_Part1")!.Value.Center.X, 1);
                editor.Undo();

                // 부모를 지우면 붙은 것도 같이 — 한 칸.
                editor.RemoveSceneObject(null, "s1");
                Assert.Empty(Scene().Objects);
                editor.Undo();
                Assert.Equal(2, Scene().Objects.Count);
            }
            finally
            {
                window.Close();
            }
        });
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
                window.Session.LoadRig(EmojiRigFixture.WriteDump(TestDocuments.FixturePath("rig-schemas.json")));
                MotionEditor editor = window.Session.Editor;
                string id = window.CreateAttachedFromMenu(EmojiRigFixture.RigKind, "s1");
                window.AddSkinFromForm();
                editor.SetSkinPart("skin", "Emoji_Part1", "art/sweat.png");
                editor.AddMotion("vanish", EmojiRigFixture.RigKind, 1f, MotionEnd.Commit);
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

                // 알파가 든 모션은 motions/3으로 나간다.
                string export = Path.Combine(Path.GetTempPath(), $"ked-motion-tool-{Guid.NewGuid():N}.json");

                try
                {
                    Assert.True(window.Session.Export(export), window.Session.Status);
                    string json = File.ReadAllText(export);
                    Assert.Contains("\"schema\": \"motions/3\"", json, StringComparison.Ordinal);
                    Assert.Contains("\"kind\": \"alpha\"", json, StringComparison.Ordinal);
                }
                finally
                {
                    File.Delete(export);
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 이모지_정의를_만들어_고르면_수명_한_번이_돌고_값을_고치면_바로_따라온다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(EmojiRigFixture.WriteDump(TestDocuments.FixturePath("rig-schemas.json")));
                MotionEditor editor = window.Session.Editor;
                string id = window.CreateAttachedFromMenu(EmojiRigFixture.RigKind, "s1");
                window.AddSkinFromForm();
                editor.RenameSkin("skin", "땀");
                editor.SetSkinPart("땀", "Emoji_Part1", "art/sweat.png");

                // 모션 셋: pop(0.2초, Body y → 40, 유지·절대값) · bob(0.4초, Body y 0 → 10 → 0, 복원) · fade(0.3초, Part1 알파 → 0, 유지).
                KeyAsset Linear(float t, float v) => new() { T = t, V = v, Tangent = TangentMode.Linear };
                editor.AddMotion("pop", EmojiRigFixture.RigKind, 0.2f, MotionEnd.Commit, MotionChannelMode.Absolute);
                editor.AddChannel("pop", "Emoji_Body", MotionChannelKind.Position);
                editor.SetKeys("pop", "Emoji_Body", MotionChannelKind.Position, Axis.Y, new[] { Linear(0f, 0f), Linear(0.2f, 40f) });
                editor.AddMotion("bob", EmojiRigFixture.RigKind, 0.4f);
                editor.AddChannel("bob", "Emoji_Body", MotionChannelKind.Position);
                editor.SetKeys("bob", "Emoji_Body", MotionChannelKind.Position, Axis.Y, new[] { Linear(0f, 0f), Linear(0.2f, 10f), Linear(0.4f, 0f) });
                editor.AddMotion("fade", EmojiRigFixture.RigKind, 0.3f, MotionEnd.Commit);
                editor.AddChannel("fade", "Emoji_Part1", MotionChannelKind.Alpha);
                editor.SetKeys("fade", "Emoji_Part1", MotionChannelKind.Alpha, Axis.X, new[] { Linear(0f, 1f), Linear(0.3f, 0f) });
                window.SelectMotion("pop");
                window.Viewport.SelectedObjectId = id;
                window.InspectorTab = 3; // 이모지 탭
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // 이모지 추가 — 고른 오브젝트의 리그와 입은 스킨으로, 한 칸. 골라지고 모션 선택이 풀린다.
                long revision = editor.Revision;
                Assert.Equal("emoji", window.AddEmojiFromForm());
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal("emoji", window.SelectedEmoji);
                Assert.Null(window.SelectedMotion);
                Assert.Equal("emoji", window.Viewport.EmojiName);
                Assert.Equal(id, window.Viewport.EmojiActor?.Id);
                Assert.Equal(("emoji", "땀", (string?)null, (string?)null, (string?)null, "", true), window.EmojiFormState);

                // 값을 채운다 — 칸마다 한 칸. 시계가 수명(0.2 + 1 + 0.3)을 따라간다.
                window.UpdateSelectedEmoji(e => { e.Enter = "pop"; e.Hold = "bob"; e.Exit = "fade"; e.HoldSeconds = 1f; e.OffsetX = 120f; e.OffsetY = 300f; });
                Assert.Equal(1.5, window.Viewport.Clock.Duration, 4);
                Assert.Equal(("pop", "bob", "fade", "1"), (window.EmojiFormState.Enter, window.EmojiFormState.Hold, window.EmojiFormState.Exit, window.EmojiFormState.HoldSeconds));
                Assert.Equal(0, window.ProblemRowsShown);

                // 수명: 등장(0 → 40) · 머묾(40 위에서 되풀이) · 퇴장(흐려짐). 자리는 정의의 오프셋.
                float BodyY() => window.Viewport.CurrentPose()!.GetState($"{id}/Emoji_Body").AnchoredPosition.Y;
                window.Viewport.ScrubTo(0.1);
                Assert.Equal(20f, BodyY(), 2);
                window.Viewport.ScrubTo(0.4);
                Assert.Equal(50f, BodyY(), 2);
                window.Viewport.ScrubTo(0.8);
                Assert.Equal(50f, BodyY(), 2);
                window.Viewport.ScrubTo(1.35);
                Assert.Equal(0.5f, window.Viewport.NodeOpacity(id, "Emoji_Part1")!.Value, 2);
                Assert.Equal(120f, window.Viewport.CurrentPose()!.GetState($"{id}/Emoji_Anchor").AnchoredPosition.X, 2);
                Assert.Equal(new[] { "Emoji_Part1" }, window.Viewport.DrawnParts(editor.Document.ActiveSceneAsset!.Find(id)!));

                // 머무는 시간을 비우면 머무는 이모지 — 미리보기는 머묾을 2초 보인다.
                window.UpdateSelectedEmoji(e => e.HoldSeconds = null);
                Assert.Equal(2.5, window.Viewport.Clock.Duration, 4);
                Assert.Equal("", window.EmojiFormState.HoldSeconds);
                editor.Undo();
                Assert.Equal(1.5, window.Viewport.Clock.Duration, 4);

                // 정의의 스킨이 그림이다 — 오브젝트가 스킨을 벗어도 미리보기에서는 보인다.
                editor.SetObjectSkin(null, id, null);
                Assert.Single(window.Viewport.DrawnParts(editor.Document.ActiveSceneAsset!.Find(id)!));

                // 없는 모션을 가리키면 문제 칸이 알린다. 내보내기는 막힌다.
                window.UpdateSelectedEmoji(e => e.Enter = "ghost");
                Assert.Equal(1, window.ProblemRowsShown);
                string target = Path.Combine(Path.GetTempPath(), $"ked-emojis-{Guid.NewGuid():N}.json");
                Assert.False(window.Session.ExportEmojis(target));
                editor.Undo();
                Assert.True(window.Session.ExportEmojis(target), window.Session.Status);
                Assert.Contains("\"schema\": \"emojis/1-draft\"", File.ReadAllText(target), StringComparison.Ordinal);
                File.Delete(target);

                // 모션을 고르면 이모지가 풀리고(오브젝트는 다시 제 스킨 — 벗었으니 안 그려진다), 다시 고르면 돌아온다.
                window.SelectMotion("bob");
                Assert.Null(window.SelectedEmoji);
                Assert.Null(window.Viewport.EmojiName);
                Assert.Empty(window.Viewport.DrawnParts(editor.Document.ActiveSceneAsset!.Find(id)!));
                window.SelectEmoji("emoji");
                Assert.Null(window.SelectedMotion);
                Assert.Equal("emoji", window.Viewport.EmojiName);

                // 지우면 뷰포트가 풀린다.
                editor.RemoveEmoji("emoji");
                Assert.Null(window.Viewport.EmojiName);
                Assert.False(window.EmojiFormState.Enabled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 덤프에_이모지_리그가_없어도_툴이_리그를_정의해_바로_이모지를_만든다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow(); // 내장 덤프 — character · screenEffect뿐
            window.Show();

            try
            {
                MotionEditor editor = window.Session.Editor;
                Assert.DoesNotContain(RigTemplates.EmojiKind, window.Session.Rig!.RigKinds);
                Assert.Contains(RigTemplates.EmojiKind, window.AttachableRigs); // 그래도 메뉴에는 있다 — 만들 때 정의한다

                // 붙는 리그 만들기 ▸ emoji — 리그 정의 + 오브젝트 + 붙이기가 되돌리기 한 칸.
                long revision = editor.Revision;
                string id = window.CreateAttachedFromMenu(RigTemplates.EmojiKind, "s1");
                Assert.Equal(revision + 1, editor.Revision);
                Assert.NotNull(editor.Document.FindRig(RigTemplates.EmojiKind));
                Assert.Contains(RigTemplates.EmojiKind, window.Session.Rig!.RigKinds);          // 세션의 리그에 합쳐졌다
                Assert.True(window.Viewport.CurrentPose()!.Contains($"{id}/Emoji_Part1"));       // 뷰포트에 섰다
                Assert.Equal("s1/CharSlot_Scale", window.Viewport.CurrentPose()!.GetParentKey($"{id}/__root"));
                Assert.Equal(0, window.ProblemRowsShown);

                // 스킨 칸이 서고(조각 넷), 스킨을 입혀 조각을 그린다.
                window.InspectorTab = 1;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(window.SkinFormState.Visible);
                window.AddSkinFromForm();
                Assert.Equal(4, window.SkinFormState.PartRows);
                editor.SetSkinPart("skin", "Emoji_Part1", "art/sweat.png");
                Assert.Equal(new[] { "Emoji_Part1" }, window.Viewport.DrawnParts(editor.Document.ActiveSceneAsset!.Find(id)!));

                // 모션 — 이 리그의 노드에, 불투명도도.
                editor.AddMotion("pop", RigTemplates.EmojiKind, 0.2f, MotionEnd.Commit, MotionChannelMode.Absolute);
                editor.AddChannel("pop", "Emoji_Body", MotionChannelKind.Position);
                editor.SetKeys("pop", "Emoji_Body", MotionChannelKind.Position, Axis.Y, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(0.2f, 40f) });
                window.SelectMotion("pop");
                window.Viewport.SelectedObjectId = id;
                window.Viewport.SelectedNode = "Emoji_Part1";
                Assert.True(window.Viewport.CanTakeAlpha("Emoji_Part1"));
                Assert.Equal(0, window.ProblemRowsShown);

                // 이모지 정의 — 리그가 이미 있으니 그대로 쓴다.
                window.InspectorTab = 3;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.AddEmojiFromForm();
                window.UpdateSelectedEmoji(e => { e.Enter = "pop"; e.HoldSeconds = 0.5f; });
                Assert.Equal(RigTemplates.EmojiKind, editor.Document.FindEmoji("emoji")!.Rig);
                Assert.Equal(0.7, window.Viewport.Clock.Duration, 4);
                Assert.Equal(0, window.ProblemRowsShown);

                // 조각이 넷인 본으로 섰다. 조각을 줄이면(편집기 명령) 뷰포트의 리그가 바로 따라온다.
                editor.SetRigParts(RigTemplates.EmojiKind, 2, 160f);
                Assert.False(window.Viewport.CurrentPose()!.Contains($"{id}/Emoji_Part3"));
                editor.Undo();
                Assert.True(window.Viewport.CurrentPose()!.Contains($"{id}/Emoji_Part3"));

                // 내보내기 셋: 리그(덤프와 같은 모양) · 모션 · 이모지.
                string directory = Path.Combine(Path.GetTempPath(), "ked-motion-tool-tests", $"emoji-out-{Guid.NewGuid():N}");
                Directory.CreateDirectory(directory);
                Assert.True(window.Session.ExportRigs(Path.Combine(directory, "rigs.json")), window.Session.Status);
                Assert.True(window.Session.Export(Path.Combine(directory, "motions.json")), window.Session.Status);
                Assert.True(window.Session.ExportEmojis(Path.Combine(directory, "emojis.json")), window.Session.Status);
                Assert.Contains("\"hasImage\": true", File.ReadAllText(Path.Combine(directory, "rigs.json")), StringComparison.Ordinal);
                Assert.Contains("\"rig\": \"emoji\"", File.ReadAllText(Path.Combine(directory, "motions.json")), StringComparison.Ordinal);

                // 저장하고 다시 열어도 리그가 선다.
                string saved = Path.Combine(directory, "a.motion-authoring.json");
                window.Session.Save(saved);
                window.Session.New();
                Assert.DoesNotContain(RigTemplates.EmojiKind, window.Session.Rig!.RigKinds);
                window.Session.Open(saved);
                Assert.Contains(RigTemplates.EmojiKind, window.Session.Rig!.RigKinds);
                Assert.True(window.Viewport.CurrentPose()!.Contains($"{id}/Emoji_Part1"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 캐릭터_모션의_emoji_이벤트가_울리면_붙은_이모지가_정의의_스킨을_입고_뜬다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(EmojiRigFixture.WriteDump(TestDocuments.FixturePath("rig-schemas.json")));
                MotionEditor editor = window.Session.Editor;
                string id = window.CreateAttachedFromMenu(EmojiRigFixture.RigKind, "s1");
                SceneObject Emoji() => editor.Document.ActiveSceneAsset!.Find(id)!;
                editor.AddSkin("땀", EmojiRigFixture.RigKind);
                editor.SetSkinPart("땀", "Emoji_Part1", "art/sweat.png");

                KeyAsset Linear(float t, float v) => new() { T = t, V = v, Tangent = TangentMode.Linear };
                editor.AddMotion("pop", EmojiRigFixture.RigKind, 0.2f, MotionEnd.Commit, MotionChannelMode.Absolute);
                editor.AddChannel("pop", "Emoji_Body", MotionChannelKind.Position);
                editor.SetKeys("pop", "Emoji_Body", MotionChannelKind.Position, Axis.Y, new[] { Linear(0f, 0f), Linear(0.2f, 40f) });
                editor.AddEmoji("sweat", EmojiRigFixture.RigKind);
                editor.UpdateEmoji("sweat", new EmojiAsset { Skin = "땀", Enter = "pop", HoldSeconds = 0.2f, OffsetX = 120f });

                // 캐릭터 모션 nod(0.6초)의 0.1초에 emoji sweat.
                editor.AddMotion("nod", "character", 0.6f, MotionEnd.Restore, MotionChannelMode.Relative);
                editor.AddChannel("nod", "CharacterPortrait_Track_Move_Y", MotionChannelKind.Position);
                editor.SetKeys("nod", "CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y, new[] { Linear(0f, 0f), Linear(0.3f, -18f), Linear(0.6f, 0f) });
                editor.AddMotionEvent("nod", 0.1f, "emoji", "sweat");
                window.SelectMotion("nod");
                window.Viewport.SelectedObjectId = "s1";
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                float BodyY() => window.Viewport.CurrentPose()!.GetState($"{id}/Emoji_Body").AnchoredPosition.Y;

                // 울리기 전: 부리는 오브젝트는 안 보인다(스킨도 안 입었다).
                window.Viewport.ScrubTo(0.05);
                Assert.Empty(window.Viewport.EmojiPlaysAt(0.05));
                Assert.True(window.Viewport.IsDormantEmoji(id));
                Assert.Empty(window.Viewport.DrawnParts(Emoji()));

                // 울린 뒤: 정의의 스킨을 입고 등장이 돈다. 자리는 정의의 오프셋.
                window.Viewport.ScrubTo(0.2);
                Assert.False(window.Viewport.IsDormantEmoji(id));
                Assert.Equal(new[] { "Emoji_Part1" }, window.Viewport.DrawnParts(Emoji()));
                Assert.Equal(20f, BodyY(), 2);
                Assert.Equal(120f, window.Viewport.CurrentPose()!.GetState($"{id}/Emoji_Anchor").AnchoredPosition.X, 2);
                window.Viewport.ScrubTo(0.4);
                Assert.Equal(40f, BodyY(), 2);

                // 붙는 자리(faceAura)를 주면 자리 노드가 캐릭터의 그 점으로 간다 — 오프셋은 거기서부터(머리 쪽이라 위로 올라간다).
                float before = window.Viewport.CurrentPose()!.GetState($"{id}/Emoji_Anchor").AnchoredPosition.Y;
                EmojiAsset located = editor.Document.FindEmoji("sweat")!.Clone();
                located.Locator = "faceAura";
                editor.UpdateEmoji("sweat", located);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(window.Viewport.CurrentPose()!.GetState($"{id}/Emoji_Anchor").AnchoredPosition.Y > before + 100f);
                Assert.Equal(40f, BodyY(), 2);

                // 수명(0.4초)이 다하면 다시 사라진다.
                window.Viewport.ScrubTo(0.55);
                Assert.True(window.Viewport.IsDormantEmoji(id));
                Assert.False(window.Viewport.IsDormantEmoji("s1"));

                // 모르는 이모지 키는 문제 목록이 알린다(막지는 않는다).
                Assert.Equal(0, window.ProblemRowsShown);
                editor.UpdateMotionEvent("nod", 0, 0.1f, "emoji", "ghost");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, window.ProblemRowsShown);
                Assert.False(window.Viewport.IsDormantEmoji(id)); // 부리는 이벤트가 없으니 보통 오브젝트다
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 씬_탭의_배역_칸에_키를_적으면_한_칸으로_들어가고_붙은_오브젝트에서는_잠긴다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(EmojiRigFixture.WriteDump(TestDocuments.FixturePath("rig-schemas.json")));
                MotionEditor editor = window.Session.Editor;
                string id = window.CreateAttachedFromMenu(EmojiRigFixture.RigKind, "s1");
                window.InspectorTab = 1; // 씬 탭
                window.Viewport.SelectedObjectId = "s1";
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(window.ObjectCharacterBox.IsEnabled);
                Assert.Equal("", window.ObjectCharacterBox.Text);

                long revision = editor.Revision;
                window.ObjectCharacterBox.Focus();
                window.ObjectCharacterBox.Text = "bandi";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal("bandi", editor.Document.ActiveSceneAsset!.Find("s1")!.Character);
                Assert.Equal(revision + 1, editor.Revision);

                editor.Undo();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Null(editor.Document.ActiveSceneAsset!.Find("s1")!.Character);
                Assert.Equal("", window.ObjectCharacterBox.Text);

                // 붙은 것은 부모의 배역을 따른다 — 칸이 잠긴다.
                window.Viewport.SelectedObjectId = id;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.False(window.ObjectCharacterBox.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 세트를_열면_그_세트만_보이고_하이어아키에서_오브젝트를_짓고_고친다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                MotionEditor editor = window.Session.Editor;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // 처음에는 세트가 없다 — 뷰포트는 문서의 씬(캐릭터 s1)을 보인다.
                Assert.Empty(window.SetEditorState.Sets);
                Assert.Null(window.Viewport.SetView);
                Assert.NotNull(window.Viewport.Scene!.Find("s1"));
                Assert.Null(window.AddSetNodeFromForm(image: true)); // 연 세트가 없으면 아무 일도 없다

                // 새 세트 — 한 칸. 목록에 서고, 열리고, 뷰포트와 하이어아키가 그 세트만 보인다.
                long revision = editor.Revision;
                Assert.Equal("set", window.NewSetFromForm());
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal(new[] { "set" }, window.SetEditorState.Sets);
                Assert.Equal(("set", "Body", true), (window.SetEditorState.Set, window.SetEditorState.Node, window.SetEditorState.Editable));
                Assert.Equal("set", window.Viewport.SetView);
                Assert.Equal(new[] { ViewportView.SetObjectId }, window.Viewport.SceneObjects.Select(o => o.Id));
                Assert.Equal(1, window.HierarchyObjectCount);
                Assert.True(window.Viewport.CurrentPose()!.Contains("set/Body"));
                Assert.False(window.Viewport.CurrentPose()!.Contains("s1/CharSlot_Scale")); // 캐릭터는 안 선다
                Assert.NotNull(editor.Document.ActiveSceneAsset!.Find("s1"));                // 문서의 씬은 그대로다
                Assert.Null(editor.Document.ActiveSceneAsset!.Find(ViewportView.SetObjectId));

                // 그림 오브젝트 둘 — 고른 오브젝트 밑에 서고 뷰포트의 세트에 바로 선다.
                Assert.Equal("Part1", window.AddSetNodeFromForm(image: true));
                window.SelectSetNode("Body");
                Assert.Equal("Part2", window.AddSetNodeFromForm(image: true));
                Assert.Equal(new[] { ("Body", ""), ("Part1", "Body"), ("Part2", "Body") }, editor.Document.FindRig("set")!.Nodes.Select(n => (n.Id, n.Parent)));
                Assert.True(window.Viewport.CurrentPose()!.Contains("set/Part2"));

                // 하이어아키에서 오브젝트를 고르면 "씬" 탭의 세트 칸이 그것을 본다. 칸에서 자리를 고친다(Enter) — 한 칸, 뷰포트가 따라온다.
                window.InspectorTab = 1;
                window.SelectInHierarchy(ViewportView.SetObjectId, "Part2");
                Assert.Equal("Part2", window.SetEditorState.Node);
                Assert.Equal("Part2", window.RigNodeBoxes.Id.Text);
                revision = editor.Revision;
                window.RigNodeBoxes.X.Focus();
                window.RigNodeBoxes.X.Text = "40";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal(40f, editor.Document.FindRig("set")!.Find("Part2")!.X);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(40f, window.Viewport.CurrentPose()!.GetState("set/Part2").AnchoredPosition.X, 3);

                // 이름 바꾸기 — 고른 것이 새 이름을 따라간다.
                window.RigNodeBoxes.Id.Focus();
                window.RigNodeBoxes.Id.Text = "Spark";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal("Spark", window.SetEditorState.Node);
                Assert.NotNull(editor.Document.FindRig("set")!.Find("Spark"));

                // 순서와 지우기.
                window.MoveSetNodeFromForm(-1);
                Assert.Equal(new[] { "Body", "Spark", "Part1" }, editor.Document.FindRig("set")!.Nodes.Select(n => n.Id));
                window.RemoveSetNodeFromForm();
                Assert.Equal(new[] { "Body", "Part1" }, editor.Document.FindRig("set")!.Nodes.Select(n => n.Id));
                Assert.Equal("Body", window.SetEditorState.Node);

                // 그 세트의 애니메이션(모션)을 고르면 세트가 연기한다 — 세트는 열린 채다.
                editor.AddMotion("in", "set", 0.5f, MotionEnd.Commit, MotionChannelMode.Absolute);
                editor.AddChannel("in", "Part1", MotionChannelKind.Position);
                editor.SetKeys("in", "Part1", MotionChannelKind.Position, Axis.X, new[] { TestDocuments.Key(0f, 0f), TestDocuments.Key(0.5f, 100f) });
                window.SelectMotion("in");
                Assert.Equal("set", window.SelectedSet);
                window.Viewport.ScrubTo(0.5);
                Assert.Equal(100f, window.Viewport.CurrentPose()!.GetState("set/Part1").AnchoredPosition.X, 2);

                // 세트를 연 동안 목록에는 그 세트의 애니메이션만 있다 — 다른 리그의 모션은 세트를 닫아야 고른다. 닫으면 씬으로 돌아간다.
                editor.AddMotion("nod", "character", 0.6f);
                Assert.DoesNotContain("nod", window.MotionListState.Names);
                window.SelectSet(null);
                Assert.Null(window.Viewport.SetView);
                Assert.NotNull(window.Viewport.Scene!.Find("s1"));
                window.SelectMotion("nod");
                Assert.Equal("nod", window.SelectedMotion);

                // 다시 열었다가, 세트를 만든 것까지 되돌리면 목록에서 사라지고 씬으로 돌아간다.
                window.SelectSet("set");
                Assert.Equal("set", window.Viewport.SetView);
                Assert.Null(window.SelectedMotion);

                while (editor.Document.FindRig("set") is not null)
                {
                    editor.Undo();
                }

                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Empty(window.SetEditorState.Sets);
                Assert.Null(window.Viewport.SetView);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 스킨의_그림_넘기기와_놓임과_반전_판이_뷰포트에_그대로_보인다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                window.Session.LoadRig(EmojiRigFixture.WriteDump(TestDocuments.FixturePath("rig-schemas.json")));
                MotionEditor editor = window.Session.Editor;
                string id = window.CreateAttachedFromMenu(EmojiRigFixture.RigKind, "s1");
                window.AddSkinFromForm();
                window.InspectorTab = 1; // 씬 탭
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
                editor.AddMotion("still", EmojiRigFixture.RigKind, 1f);
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
                float perPixel = (before.BottomRight.X - before.BottomLeft.X) / EmojiRigFixture.PartSize;
                Assert.Equal(before.Center.X + 50f * perPixel, placed.Center.X, 2);
                Assert.Equal(0f, window.Viewport.CurrentPose()!.GetState($"{id}/Emoji_Part1").AnchoredPosition.X, 3);

                // 크기 0.5 — 사각형이 반으로.
                SkinPartSetup half = editor.Document.FindSkin("skin")!.SetupOf("Emoji_Part1").Clone();
                half.Scale = 0.5f;
                editor.SetSkinPartSetup("skin", "Emoji_Part1", half);
                Quad small = window.Viewport.PartQuad(id, "Emoji_Part1")!.Value;
                Assert.Equal((before.BottomRight.X - before.BottomLeft.X) / 2f, small.BottomRight.X - small.BottomLeft.X, 2);

                // 반전 판 — 캐릭터가 좌우 반전이면 이모지는 반전 판 스킨을 입고, 그림은 뒤집지 않는다(사각형의 왼쪽 아래가 여전히 왼쪽).
                editor.AddSkin("skin_flip", EmojiRigFixture.RigKind);
                editor.SetSkinPartImages("skin_flip", "Emoji_Part1", new[] { "art/left.png" });
                editor.AddEmoji("e", EmojiRigFixture.RigKind);
                editor.UpdateEmoji("e", new EmojiAsset { Skin = "skin", SkinFlipped = "skin_flip", HoldSeconds = 1f, OffsetX = 100f });
                window.SelectEmoji("e");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Viewport.ScrubTo(0.1);
                Assert.Equal("art/a.png", window.Viewport.PartImage(id, "Emoji_Part1"));
                float right = window.Viewport.PartQuad(id, "Emoji_Part1")!.Value.Center.X;

                editor.SetObjectPlacement(null, "s1", 0f, 0f, 0f, -1f, 1f);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Viewport.ScrubTo(0.1);
                Assert.Equal("art/left.png", window.Viewport.PartImage(id, "Emoji_Part1"));
                Quad flipped = window.Viewport.PartQuad(id, "Emoji_Part1")!.Value;
                Assert.True(flipped.Center.X < 0f && right > 0f, $"{right} → {flipped.Center.X}"); // 자리는 뒤집혔다
                Assert.True(flipped.BottomRight.X > flipped.BottomLeft.X);                          // 그림은 안 뒤집혔다

                // 반전 판이 없으면 같은 그림이 뒤집혀 보인다.
                editor.UpdateEmoji("e", new EmojiAsset { Skin = "skin", HoldSeconds = 1f, OffsetX = 100f });
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Viewport.ScrubTo(0.1);
                Quad mirrored = window.Viewport.PartQuad(id, "Emoji_Part1")!.Value;
                Assert.Equal("art/a.png", window.Viewport.PartImage(id, "Emoji_Part1"));
                Assert.True(mirrored.BottomRight.X < mirrored.BottomLeft.X);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 이모지_탭에서_그_이모지의_스킨을_바로_만들고_고친다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                MotionEditor editor = window.Session.Editor;
                string id = window.CreateAttachedFromMenu(RigTemplates.EmojiKind, "s1"); // 리그도 그 자리에서 정의된다
                window.InspectorTab = 3;
                Assert.Equal("emoji", window.AddEmojiFromForm());
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Null(window.EmojiSkinPartBox("Emoji_Part1")); // 스킨이 아직 없다

                // 새 스킨 — 이모지의 이름으로 만들어 그림으로 삼는다, 한 칸. 조각 줄이 선다(오브젝트에 입히지 않아도).
                long revision = editor.Revision;
                Assert.Equal("emoji", window.NewEmojiSkinFromForm(flipped: false));
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal("emoji", editor.Document.FindEmoji("emoji")!.Skin);
                Assert.Null(editor.Document.ActiveSceneAsset!.Find(id)!.Skin);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                TextBox part = window.EmojiSkinPartBox("Emoji_Part1")!;

                // 그 자리에서 그림을 준다 — 뷰포트의 이모지가 바로 그 그림을 그린다.
                part.Focus();
                part.Text = "art/a.png | art/b.png";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal(new[] { "art/a.png", "art/b.png" }, editor.Document.FindSkin("emoji")!.ImagesOf("Emoji_Part1"));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Viewport.ScrubTo(0.05);
                Assert.Equal("art/a.png", window.Viewport.PartImage(id, "Emoji_Part1"));

                // 놓임도 여기서.
                var boxes = window.EmojiSkinSetupBoxes("Emoji_Part1")!;
                boxes.Y.Focus();
                boxes.Y.Text = "30";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal(30f, editor.Document.FindSkin("emoji")!.SetupOf("Emoji_Part1").Y);

                // 반전 판 — 따로 한 묶음의 줄이 선다.
                Assert.Equal("emoji_반전", window.NewEmojiSkinFromForm(flipped: true));
                Assert.Equal("emoji_반전", editor.Document.FindEmoji("emoji")!.SkinFlipped);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                TextBox flipped = window.EmojiSkinPartBox("Emoji_Part1", flipped: true)!;
                flipped.Focus();
                flipped.Text = "art/left.png";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal("art/left.png", editor.Document.FindSkin("emoji_반전")!.Parts["Emoji_Part1"]);
                Assert.Equal("art/a.png", editor.Document.FindSkin("emoji")!.Parts["Emoji_Part1"]); // 본판은 그대로

                // 되돌리면 스킨과 이모지의 참조가 함께 돌아간다.
                editor.Undo(); // 그림
                editor.Undo(); // 새 반전 판
                Assert.Null(editor.Document.FindEmoji("emoji")!.SkinFlipped);
                Assert.Null(editor.Document.FindSkin("emoji_반전"));
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
                string id = window.CreateAttachedFromMenu(RigTemplates.EmojiKind, "s1");
                window.InspectorTab = 3;
                window.AddEmojiFromForm();
                window.NewEmojiSkinFromForm(flipped: false);
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
                string id = window.CreateAttachedFromMenu(RigTemplates.EmojiKind, "s1");
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

                string art = Path.Combine(root!.FullName, "examples", "emoji", "art", "alert.png");
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
    public void 세트를_연_채_연출을_만들면_화면에_서는_연출이_되고_씬에_놓지_않아도_미리본다()
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
                window.SelectSetNode("Body");
                window.AddColorNodeFromForm();
                editor.AddMotion("in", "set", 0.4f, MotionEnd.Commit, MotionChannelMode.Absolute);
                editor.AddChannel("in", "Color1", MotionChannelKind.Position);
                editor.SetKeys("in", "Color1", MotionChannelKind.Position, Axis.X, new[] { TestDocuments.Key(0f, -500f), TestDocuments.Key(0.4f, 0f) });
                window.InspectorTab = 3; // 연출 탭
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // 세트를 연 채 "연출 추가" — 그 세트의 연출이, 화면에 서는 것으로, 세트의 이름으로 선다(한 칸). 연출을 고르면 세트 보기를 떠난다.
                long revision = editor.Revision;
                Assert.Equal("set", window.AddEmojiFromForm());
                Assert.Equal(revision + 1, editor.Revision);
                EmojiAsset cue = editor.Document.FindEmoji("set")!;
                Assert.Equal(("set", true), (cue.Rig, cue.Screen));
                Assert.Null(window.SelectedSet);
                Assert.Equal("set", window.SelectedEmoji);
                window.UpdateSelectedEmoji(e => { e.Enter = "in"; e.HoldSeconds = 1f; });
                Assert.Equal(0, window.ProblemRowsShown); // 색 오브젝트가 있어 스킨이 없어도 된다

                // 씬에 그 세트가 없다 — 보기 전용 오브젝트가 대신 서서 연기한다. 문서의 씬은 그대로다.
                Assert.Equal(ViewportView.CueObjectId, view.EmojiActor?.Id);
                Assert.Null(editor.Document.ActiveSceneAsset!.Find(ViewportView.CueObjectId));
                Assert.NotNull(view.Scene!.Find("s1")); // 캐릭터도 같이 보인다
                view.ScrubTo(0.2);
                Assert.Equal(-250f, view.CurrentPose()!.GetState($"{ViewportView.CueObjectId}/Color1").AnchoredPosition.X, 1);

                // 장면에 세트를 놓으면 그것이 연기한다(보기 전용 오브젝트는 물러난다).
                string placed = window.PlaceSetFromMenu("set");
                Assert.Null(editor.Document.ActiveSceneAsset!.ParentOf(editor.Document.ActiveSceneAsset!.Find(placed)!));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.SelectEmoji("set");
                Assert.Equal(placed, view.EmojiActor?.Id);
                Assert.Null(view.Scene!.Find(ViewportView.CueObjectId));

                // 캐릭터에 붙는 쪽으로 바꾸면 보통의 이모지처럼 그 리그의 오브젝트가 연기한다.
                window.UpdateSelectedEmoji(e => e.Screen = false);
                Assert.False(editor.Document.FindEmoji("set")!.Screen);
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
    public void 세트에_다른_세트를_넣으면_뷰포트에_펼쳐_서고_통째로_끌리고_원본을_고치면_따라온다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                MotionEditor editor = window.Session.Editor;
                ViewportView view = window.Viewport;

                // 세트 둘: car(색 사각형 하나) · city(빈 세트).
                Assert.Equal("set", window.NewSetFromForm());
                window.SelectSetNode("Body");
                window.AddColorNodeFromForm(); // set/Color1 — 400×300
                Assert.Equal("set_2", window.NewSetFromForm());
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // 세트 넣기 — 한 칸. 인스턴스가 골라지고, 뷰포트의 세트에 안쪽의 것이 경로 이름으로 선다.
                long revision = editor.Revision;
                window.SelectSetNode(null);
                Assert.Equal("set", window.AddSetInstanceFromMenu("set"));
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal("set", editor.Document.FindRig("set_2")!.Find("set")!.Set);
                Assert.Equal("set", window.SetEditorState.Node);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(view.CurrentPose()!.Contains("set/set.Color1"));
                Assert.True(view.IsLayoutMode);

                // 안쪽의 것을 눌러도 그것을 품은 오브젝트가 집힌다 — 끌면 통째로 옮겨진다(안쪽은 그대로).
                Point At(Vec2 p) => view.TranslatePoint(view.ToScreen(p), window) ?? throw new InvalidOperationException("뷰가 창에 붙어 있지 않다");
                Vec2 Inner() => view.CurrentPose()!.TransformPoint("set/set.Color1", Vec3.Zero).XY;
                Vec2 grab = Inner();
                var drop = new Vec2(grab.X + 80f, grab.Y + 40f);
                window.MouseDown(At(grab), MouseButton.Left);
                Assert.Equal("set", window.SetEditorState.Node);
                window.MouseMove(At(drop), RawInputModifiers.LeftMouseButton);
                window.MouseUp(At(drop), MouseButton.Left);
                RigNodeAsset instance = editor.Document.FindRig("set_2")!.Find("set")!;
                Assert.Equal((80f, 40f, "set"), (instance.X, instance.Y, instance.Set));
                Assert.Equal(0f, editor.Document.FindRig("set")!.Find("Color1")!.X); // 원본 세트는 안 바뀐다
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(grab.X + 80f, Inner().X, 1);

                // 원본을 고치면 품은 곳이 따라온다(참조).
                editor.UpdateRigNode("set", "Color1", new RigNodeAsset { X = 25f, Width = 400f, Height = 300f, Color = "#3D5A80", Alpha = true });
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(grab.X + 80f + 25f, Inner().X, 1);

                // 서로 품는 것은 막힌다 — 문서는 그대로다(메뉴에서는 상태줄에 까닭이 뜬다).
                window.SelectSet("set");
                revision = editor.Revision;
                Assert.Throws<InvalidOperationException>(() => window.AddSetInstanceFromMenu("set_2"));
                Assert.Equal(revision, editor.Revision);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 재생_블록_칸에서_품은_세트의_애니메이션을_지금_시각에_놓으면_뷰포트가_그것을_튼다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                MotionEditor editor = window.Session.Editor;
                ViewportView view = window.Viewport;

                // 세트 둘: set(색 사각형 + 애니메이션 slide: 0.4초 유지, x 0 → 100) · set_2(set을 품는다 + 애니메이션 city_in: 2초).
                window.NewSetFromForm();
                window.SelectSetNode("Body");
                window.AddColorNodeFromForm();
                editor.AddMotion("slide", "set", 0.4f, MotionEnd.Commit);
                editor.AddChannel("slide", "Color1", MotionChannelKind.Position);
                editor.SetKeys("slide", "Color1", MotionChannelKind.Position, Axis.X, new[]
                {
                    new KeyAsset { T = 0f, V = 0f, Tangent = TangentMode.Linear },
                    new KeyAsset { T = 0.4f, V = 100f, Tangent = TangentMode.Linear },
                });
                window.NewSetFromForm();
                window.SelectSetNode(null);
                window.AddSetInstanceFromMenu("set");
                editor.AddMotion("city_in", "set_2", 2f, MotionEnd.Commit);
                window.InspectorTab = 2; // 모션 탭
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // 품은 세트가 있는 세트의 애니메이션을 고르면 재생 블록 칸이 보인다.
                window.SelectMotion("city_in");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(window.PlayPanelShown);
                Assert.Equal(0, window.PlayRowCount);

                // "재생 블록 추가 (지금 시각)" — 한 칸. 지금 시각에 들어가는 첫 (인스턴스, 애니메이션)이 놓인다.
                window.Graph.CurrentTime = 0.5;
                long revision = editor.Revision;
                Assert.Equal(0, window.AddMotionPlayFromForm());
                Assert.Equal(revision + 1, editor.Revision);
                MotionPlayAsset play = Assert.Single(editor.Document.Find("city_in")!.Plays);
                Assert.Equal(("set", "slide", 0.5f, 1), (play.Node, play.Motion, play.At, play.Repeat));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, window.PlayRowCount);
                Assert.Equal(("0.5", "set", "slide", "1"), (window.PlayRow(0).Time.Text, window.PlayRow(0).Node.SelectedItem, window.PlayRow(0).Motion.SelectedItem, window.PlayRow(0).Repeat.Text));

                // 타임 슬라이더가 블록의 구간을 막대로 그린다(0.5 ~ 0.9초).
                (double start, double end, string label) = Assert.Single(view.PlaySpans());
                Assert.Equal((0.5, 0.9, "set ▸ slide"), (Math.Round(start, 3), Math.Round(end, 3), label));

                // 막대를 끌면 블록의 시작 시각이 옮겨진다 — 놓을 때 한 칸. 막대 너비(0.4초)만큼 오른쪽으로.
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Rect bar = window.TimeSlider.PlayBarRects()[0];
                Point Slider(Point p) => window.TimeSlider.TranslatePoint(p, window) ?? throw new InvalidOperationException("슬라이더가 창에 붙어 있지 않다");
                revision = editor.Revision;
                window.MouseDown(Slider(bar.Center), MouseButton.Left);
                window.MouseMove(Slider(new Point(bar.Center.X + bar.Width, bar.Center.Y)), RawInputModifiers.LeftMouseButton);
                window.MouseUp(Slider(new Point(bar.Center.X + bar.Width, bar.Center.Y)), MouseButton.Left);
                Assert.Equal(revision + 1, editor.Revision);
                Assert.InRange(editor.Document.Find("city_in")!.Plays[0].At, 0.8f, 1.0f);

                // 길이 밖으로 끌면 끝에 맞춰 선다(2 − 0.4 = 1.6초).
                bar = window.TimeSlider.PlayBarRects()[0];
                window.MouseDown(Slider(bar.Center), MouseButton.Left);
                window.MouseMove(Slider(new Point(bar.Center.X + 4000, bar.Center.Y)), RawInputModifiers.LeftMouseButton);
                window.MouseUp(Slider(new Point(bar.Center.X + 4000, bar.Center.Y)), MouseButton.Left);
                Assert.Equal(1.6f, editor.Document.Find("city_in")!.Plays[0].At, 2);
                editor.UpdateMotionPlay("city_in", 0, "set", "slide", 0.5f, 1);

                // 뷰포트가 품은 애니메이션을 튼다 — 블록 전에는 제자리, 가운데서 반, 끝난 뒤에는 남는다(유지).
                float X(double t)
                {
                    window.Graph.CurrentTime = t;
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    return view.CurrentPose()!.TransformPoint("set/set.Color1", Vec3.Zero).X;
                }

                float rest = X(0.2);
                Assert.Equal(50f, X(0.7) - rest, 0);
                Assert.Equal(100f, X(1.5) - rest, 0);

                // 반복 칸을 2로(Enter) — 한 칸. 둘째 바퀴가 쌓인다.
                TextBox repeat = window.PlayRow(0).Repeat;
                repeat.Focus();
                repeat.Text = "2";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal(2, editor.Document.Find("city_in")!.Plays[0].Repeat);
                Assert.Equal(200f, X(1.5) - rest, 0);

                // 길이를 넘는 시각은 막히고 문서는 그대로다.
                revision = editor.Revision;
                TextBox time = window.PlayRow(0).Time;
                time.Focus();
                time.Text = "1.9";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal(revision, editor.Revision);

                // 줄의 "떼기" — 블록이 사라지고 그 움직임이 제 키로 굳는다(한 칸). 뷰포트의 재생은 그대로다.
                window.Viewport.Focus();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                revision = editor.Revision;
                window.DetachPlayRow(0);
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Empty(editor.Document.Find("city_in")!.Plays);
                Assert.NotNull(editor.Document.Find("city_in")!.FindChannel("set.Color1", MotionChannelKind.Position));
                Assert.Equal(200f, X(1.5) - rest, 0);
                Assert.Equal(0, window.PlayRowCount);

                // 품은 세트가 없는 세트의 애니메이션에서는 칸이 숨는다.
                window.SelectSet("set");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.SelectMotion("slide");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.False(window.PlayPanelShown);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 하이어아키에서_새_세트로_빼면_인스턴스가_그_자리에_서고_세트_이름_칸에서_이름을_바꾼다()
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
                window.SelectSetNode(null);
                window.AddColorNodeFromForm(); // Color1 — 맨 위
                window.SelectSetNode(null);
                window.AddColorNodeFromForm(); // Color2
                editor.UpdateRigNode("set", "Color2", new RigNodeAsset { X = 200f, Y = 100f, Width = 400f, Height = 300f, Color = "#3D5A80", Alpha = true });
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Vec2 before = view.CurrentPose()!.TransformPoint("set/Color2", Vec3.Zero).XY;

                // Color1부터 Color2까지를 새 세트로 — 한 칸. 이름은 첫 오브젝트에서, 그 자리에 인스턴스가 서고 골라진다.
                long revision = editor.Revision;
                Assert.Equal("color1", window.ExtractSetNodesFromMenu("Color1", "Color2"));
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal("color1", editor.Document.FindRig("set")!.Find("color1")!.Set);
                Assert.Equal(new[] { "Color1", "Color2" }, editor.Document.FindRig("color1")!.Nodes.Select(n => n.Id));
                Assert.Equal("color1", window.SetEditorState.Node);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // 보이는 자리는 그대로다.
                Vec2 after = view.CurrentPose()!.TransformPoint("set/color1.Color2", Vec3.Zero).XY;
                Assert.Equal(before.X, after.X, 1);
                Assert.Equal(before.Y, after.Y, 1);

                // 세트 이름 칸 — 새 세트를 열어 이름을 고친다(Enter). 품은 곳이 따라오고 그 세트가 열린 채다.
                window.SelectSet("color1");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal("color1", window.SetNameBox.Text);
                window.SetNameBox.Focus();
                window.SetNameBox.Text = "panels";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal("panels", editor.Document.FindRig("set")!.Find("panels")!.Set); // 세트의 이름을 딴 인스턴스도 따라온다
                Assert.Equal("panels", window.SelectedSet);

                // 있는 이름은 막히고 칸이 돌아온다.
                window.SetNameBox.Focus();
                window.SetNameBox.Text = "set";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.NotNull(editor.Document.FindRig("panels"));
                Assert.Equal("panels", window.SetNameBox.Text);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 인스턴스를_고르면_품은_세트의_스킨을_고르고_안쪽_세트로_들어갔다_돌아온다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                MotionEditor editor = window.Session.Editor;
                window.NewSetFromForm();                 // set — 품길 세트(스킨 둘)
                editor.AddSkin("red", "set");
                editor.AddSkin("blue", "set");
                window.NewSetFromForm();                 // set_2 — 품는 세트
                window.SelectSetNode(null);
                window.AddSetInstanceFromMenu("set");
                window.InspectorTab = 1;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // 인스턴스를 고르면 스킨 칸이 선다 — 고르면 한 칸.
                Assert.True(window.RigInstanceFieldsShown);
                Assert.Equal(0, window.RigNodeSkinCombo.SelectedIndex);
                long revision = editor.Revision;
                window.RigNodeSkinCombo.SelectedItem = "blue";
                Assert.Equal("blue", editor.Document.FindRig("set_2")!.Find("set")!.SetSkin);
                Assert.Equal(revision + 1, editor.Revision);
                window.RigNodeSkinCombo.SelectedIndex = 0;
                Assert.Null(editor.Document.FindRig("set_2")!.Find("set")!.SetSkin);

                // 보통 오브젝트에서는 칸이 숨는다.
                window.SelectSetNode("Body");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.False(window.RigInstanceFieldsShown);

                // 안쪽 세트 열기 → 돌아가기 — 문서는 안 바뀌고, 돌아오면 그 인스턴스가 골라져 있다.
                window.SelectSetNode("set");
                revision = editor.Revision;
                Assert.False(window.SetBackShown);
                window.EnterSetFromForm();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal("set", window.SelectedSet);
                Assert.True(window.SetBackShown);
                window.BackToOuterSetFromForm();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(("set_2", "set"), (window.SelectedSet, window.SetEditorState.Node));
                Assert.False(window.SetBackShown);
                Assert.Equal(revision, editor.Revision);

                // 들어간 뒤 목록에서 직접 다른 세트를 고르면 돌아갈 길이 사라진다.
                window.EnterSetFromForm();
                window.SelectSet("set_2");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.False(window.SetBackShown);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 다른_문서의_세트를_들이면_애니메이션과_스킨과_연출이_함께_오고_그림을_찾는다()
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

                string path = Path.Combine(root!.FullName, "examples", "transitions", "pillar", "pillar.motion-authoring.json");

                // pillar 문서의 세트 둘(pillar · 그것이 품은 sun)이 애니메이션 · 스킨 · 연출과 함께 — 한 칸.
                long revision = editor.Revision;
                Assert.Equal(new[] { "pillar", "sun" }, window.ImportSetsFromFile(path).OrderBy(k => k, StringComparer.Ordinal));
                Assert.Equal(revision + 1, editor.Revision);
                Assert.NotNull(editor.Document.Find("pillar_in"));
                Assert.NotNull(editor.Document.Find("sun_rise"));
                Assert.NotNull(editor.Document.FindSkin("sun"));
                Assert.True(editor.Document.FindEmoji("pillar")!.Screen);
                Assert.NotNull(editor.Document.Find("pillar_in")!.Flattened); // 재생 블록도 따라와 구워진다

                // 그림은 그 문서의 자리를 가리킨다 — 전부 찾는다.
                Assert.All(window.Session.SkinImages(), image => Assert.True(image.Path is not null && File.Exists(image.Path), image.Image));

                // 세트 목록에 서고 열린다.
                window.SelectSet("sun");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal("sun", window.SelectedSet);

                // 같은 이름이 이미 있으면 막힌다 — 문서는 그대로다.
                revision = editor.Revision;
                Assert.Throws<InvalidOperationException>(() => window.ImportSetsFromFile(path));
                Assert.Equal(revision, editor.Revision);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 품은_세트를_더블클릭하면_바깥을_보는_채로_안쪽의_오브젝트를_끌어_그_세트를_고친다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                MotionEditor editor = window.Session.Editor;
                ViewportView view = window.Viewport;

                // set(색 사각형 Color1) · set_2(set을 (300, 100)에 품는다).
                window.NewSetFromForm();
                window.SelectSetNode("Body");
                window.AddColorNodeFromForm();
                window.NewSetFromForm();
                window.SelectSetNode(null);
                window.AddSetInstanceFromMenu("set");
                editor.UpdateRigNode("set_2", "set", new RigNodeAsset { X = 300f, Y = 100f, Alpha = true });
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Point At(Vec2 p) => view.TranslatePoint(view.ToScreen(p), window) ?? throw new InvalidOperationException("뷰가 창에 붙어 있지 않다");
                Vec2 Inner() => view.CurrentPose()!.TransformPoint("set/set.Color1", Vec3.Zero).XY;
                RigNodeAsset Color() => editor.Document.FindRig("set")!.Find("Color1")!;
                RigNodeAsset Instance() => editor.Document.FindRig("set_2")!.Find("set")!;

                // 더블클릭 — 안쪽으로 들어간다. 문서는 안 바뀐다.
                long revision = editor.Revision;
                Vec2 grab = Inner();
                window.MouseDown(At(grab), MouseButton.Left);
                window.MouseUp(At(grab), MouseButton.Left);
                window.MouseDown(At(grab), MouseButton.Left);
                window.MouseUp(At(grab), MouseButton.Left);
                Assert.Equal("set", view.EnteredInstance);
                Assert.Equal(revision, editor.Revision);

                // 안쪽의 것을 끈다 — 품은 세트의 오브젝트가 옮겨지고(한 칸) 인스턴스는 그대로다.
                var drop = new Vec2(grab.X + 60f, grab.Y + 30f);
                window.MouseDown(At(grab), MouseButton.Left);
                Assert.Equal("set.Color1", view.SelectedNode);
                window.MouseMove(At(drop), RawInputModifiers.LeftMouseButton);
                window.MouseUp(At(drop), MouseButton.Left);
                Assert.Equal(revision + 1, editor.Revision);
                Assert.Equal((60f, 30f), (Color().X, Color().Y));
                Assert.Equal((300f, 100f), (Instance().X, Instance().Y));

                // Esc — 나온다. 그 인스턴스가 골라지고, 이제 안쪽을 끌면 인스턴스가 통째로 옮겨진다.
                view.Focus();
                window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                Assert.Null(view.EnteredInstance);
                Assert.Equal("set", window.SetEditorState.Node);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                grab = Inner();
                drop = new Vec2(grab.X + 10f, grab.Y);
                window.MouseDown(At(grab), MouseButton.Left);
                window.MouseMove(At(drop), RawInputModifiers.LeftMouseButton);
                window.MouseUp(At(drop), MouseButton.Left);
                Assert.Equal((310f, 60f), (Instance().X, Color().X));

                // 칸의 단추로도 들어간다. 애니메이션을 고른 때(배치 모드가 아니다)는 안 들어가진다.
                window.SelectSetNode("set");
                Assert.True(window.EnterInstanceInPlaceFromForm());
                Assert.Equal("set", view.EnteredInstance);
                view.EnterInstance(null);
                editor.AddMotion("city_in", "set_2", 1f, MotionEnd.Commit);
                window.SelectMotion("city_in");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.SelectSetNode("set");
                Assert.False(window.EnterInstanceInPlaceFromForm());
                Assert.Null(view.EnteredInstance);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void 실제_연출_station에서_메뉴로_층을_새_세트로_빼고_이름을_바꿔도_연출의_재생이_그대로다()
    {
        HeadlessUi.Run(() =>
        {
            MainWindow window = NewWindow();
            window.Show();

            try
            {
                DirectoryInfo? root = new(AppContext.BaseDirectory);

                while (root is not null && !File.Exists(Path.Combine(root.FullName, "KedMotionTool.sln")))
                {
                    root = root.Parent;
                }

                window.Session.Open(Path.Combine(root!.FullName, "examples", "transitions", "station", "station.motion-authoring.json"));
                MotionEditor editor = window.Session.Editor;
                ViewportView view = window.Viewport;
                double[] times = { 0.2, 0.7, 1.0, 1.6, 2.0, 2.5, 2.9 };

                // 연출을 틀어 "끝의 빛"(등장·머묾·퇴장에 다 키가 있다)의 자리와 크기를 적어 둔다.
                List<(Vec2 Origin, Vec2 Edge)> Sample(string node)
                {
                    window.InspectorTab = 3;
                    window.SelectEmoji("station");
                    var samples = new List<(Vec2, Vec2)>();

                    foreach (double t in times)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                        view.ScrubTo(t);
                        RectNodeTree pose = view.CurrentPose()!;
                        string key = pose.Keys.Single(k => k.EndsWith("/" + node, StringComparison.Ordinal));
                        samples.Add((pose.TransformPoint(key, Vec3.Zero).XY, pose.TransformPoint(key, new Vec3(100f, 0f, 0f)).XY));
                    }

                    return samples;
                }

                List<(Vec2 Origin, Vec2 Edge)> before = Sample("light");

                // 세트를 열고 메뉴로 뺀다 → 이름을 lamp로. 두 칸.
                window.SelectSet("station");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                long revision = editor.Revision;
                Assert.Equal("light", window.ExtractSetNodesFromMenu("light"));
                window.SelectSet("light");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.SetNameBox.Focus();
                window.SetNameBox.Text = "lamp";
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Assert.Equal(revision + 2, editor.Revision);

                // 세트·애니메이션·스킨·인스턴스의 이름이 다 따라왔고, station의 세 애니메이션이 그것을 재생 블록으로 튼다.
                Assert.Equal("lamp", editor.Document.FindRig("station")!.Find("lamp")!.Set);
                Assert.Equal(new[] { "lamp_hold", "lamp_in", "lamp_out" }, editor.Document.Motions.Where(m => m.Rig == "lamp").Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal));
                Assert.NotNull(editor.Document.FindSkin("lamp"));
                Assert.All(new[] { "station_in", "station_hold", "station_out" }, m => Assert.Contains(editor.Document.Find(m)!.Plays, p => p.Node == "lamp" && p.Motion.StartsWith("lamp_", StringComparison.Ordinal)));
                Assert.Equal(0, window.ProblemRowsShown);

                // 연출의 재생은 그대로다.
                window.SelectSet(null);
                List<(Vec2 Origin, Vec2 Edge)> after = Sample("lamp.light");

                for (int i = 0; i < times.Length; i++)
                {
                    Assert.True(Math.Abs(before[i].Origin.X - after[i].Origin.X) < 1.5f && Math.Abs(before[i].Origin.Y - after[i].Origin.Y) < 1.5f, $"자리 @ {times[i]}");
                    Assert.True(Math.Abs(before[i].Edge.X - after[i].Edge.X) < 1.5f && Math.Abs(before[i].Edge.Y - after[i].Edge.Y) < 1.5f, $"크기 @ {times[i]}");
                }
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
