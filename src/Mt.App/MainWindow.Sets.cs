using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Mt.App.Views;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;

namespace Mt.App;

// 세트(PLAN-set.md S1) — 오브젝트들의 계층 + 그림 + 애니메이션들의 한 덩어리. 데이터는 툴이 정의한 리그(RigAsset)다.
// 왼쪽 목록에서 세트를 열면 뷰포트·하이어아키가 그 세트만 보이고, 하이어아키에서 오브젝트를 더하고 지우고 옮긴다.
// 캐릭터 리그(덤프가 원본)는 세트 목록에 없다 — 구조를 여기서 못 고친다.
public partial class MainWindow
{
    private const string RootParentChoice = "(맨 위)";

    private readonly ListBox _setList = new() { MinHeight = 44, MaxHeight = 110, FontSize = 11 };
    private readonly StackPanel _setNodePanel = new() { Spacing = 4, IsVisible = false };
    private readonly TextBlock _setNodeTitle = new() { FontSize = 10, Opacity = 0.75, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly StackPanel _rigNodeFields = new() { Spacing = 4 };
    private readonly TextBox _rigNodeId = new() { FontSize = 11 };
    private readonly ComboBox _rigNodeParent = new() { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _rigNodeX = new() { FontSize = 11 };
    private readonly TextBox _rigNodeY = new() { FontSize = 11 };
    private readonly TextBox _rigNodeW = new() { FontSize = 11 };
    private readonly TextBox _rigNodeH = new() { FontSize = 11 };
    private readonly TextBox _rigNodeRotation = new() { FontSize = 11 };
    private readonly TextBox _rigNodeScaleX = new() { FontSize = 11 };
    private readonly TextBox _rigNodeScaleY = new() { FontSize = 11 };
    private readonly TextBox _rigNodeColor = new() { FontSize = 11, PlaceholderText = "(색 없음) #RRGGBB" };
    private readonly CheckBox _rigNodeImage = new() { Content = "그림을 그린다", FontSize = 11 };
    private readonly CheckBox _rigNodeAlpha = new() { Content = "불투명도를 받는다", FontSize = 11 };
    private readonly Button _rigNodeSwatch = new() { Width = 22, Height = 20, Margin = new Avalonia.Thickness(3, 0, 0, 0), Padding = new Avalonia.Thickness(0), BorderThickness = new Avalonia.Thickness(1), BorderBrush = Avalonia.Media.Brushes.Gray };

    /// <summary>기본 견본 — 종이 느낌의 차분한 색들(예시 그림의 색에서).</summary>
    private static readonly string[] DefaultSwatches =
    {
        "#FFFFFF", "#F4EDE0", "#E8D5B5", "#C9B79C", "#8A8176", "#3A3A3A", "#000000",
        "#243B6B", "#3D5A80", "#5C7FA8", "#98C1D9", "#6B4E8A", "#9A7FB8", "#C9B3DE",
        "#EE6C4D", "#F2A65A", "#F6C453", "#E8A0A0", "#C0504D", "#5E8C61", "#A8C5A0",
    };

    /// <summary>테스트 손잡이 — 색 견본들(문서에서 쓴 색 + 기본), 견본 하나를 고른다.</summary>
    internal IReadOnlyList<string> ColorSwatches() => SwatchColors();

    internal void PickColorSwatch(string color) => ApplySwatch(color);

    private List<string> SwatchColors()
        => _session.Editor.Document.Rigs.SelectMany(r => r.Nodes).Select(n => n.Color).OfType<string>().Where(c => c.Length > 0)
            .Concat(DefaultSwatches).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private void ApplySwatch(string color)
    {
        if (EditedRigNode is null)
        {
            return;
        }

        _rigNodeColor.Text = color;
        Guard("색", CommitRigNode);
    }

    private void OpenColorSwatches()
    {
        var grid = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 7 * 26 };
        var flyout = new Flyout { Content = grid };

        foreach (string color in SwatchColors())
        {
            if (!Avalonia.Media.Color.TryParse(color.Length == 9 ? color[..7] : color, out Avalonia.Media.Color parsed)) // #RRGGBBAA면 견본은 색만
            {
                continue;
            }

            string captured = color;
            var swatch = new Button { Width = 22, Height = 22, Margin = new Avalonia.Thickness(2), Padding = new Avalonia.Thickness(0), Background = new Avalonia.Media.SolidColorBrush(parsed) };
            ToolTip.SetTip(swatch, captured);
            swatch.Click += (_, _) =>
            {
                flyout.Hide();
                ApplySwatch(captured);
            };
            grid.Children.Add(swatch);
        }

        flyout.ShowAt(_rigNodeSwatch);
    }

    // 세트를 품은 오브젝트(인스턴스)의 칸 — 품은 세트에 입힐 스킨, 그 세트로 들어가기. 들어갔으면 돌아오는 단추가 선다.
    private const string FirstSkinChoice = "(그 세트의 첫 스킨)";
    private readonly ComboBox _rigNodeSkin = new() { FontSize = 11 };
    private readonly StackPanel _rigInstanceFields = new() { Spacing = 3 };
    private readonly Button _setBack = new() { FontSize = 11, Padding = new Avalonia.Thickness(6, 1), IsVisible = false };
    private readonly Stack<string> _setTrail = new();
    private bool _navigatingSets;

    /// <summary>테스트 손잡이 — 인스턴스의 스킨 칸, "안쪽 세트 열기" · "돌아가기".</summary>
    internal ComboBox RigNodeSkinCombo => _rigNodeSkin;

    internal bool RigInstanceFieldsShown => _rigInstanceFields.IsVisible;

    internal void EnterSetFromForm() => EnterInnerSet();

    /// <summary>테스트 손잡이 — "여기서 안쪽 고치기".</summary>
    internal bool EnterInstanceInPlaceFromForm() => EnterInstanceInPlace();

    private bool EnterInstanceInPlace()
    {
        if (EditedRigNode is not { Set.Length: > 0 } node)
        {
            return false;
        }

        if (!_viewport.EnterInstance(node.Id))
        {
            _session.SetStatus("안쪽 고치기는 배치 모드에서만 된다 — 애니메이션 고르기를 푼다(애니메이션은 \"안쪽 세트 열기\"로 그 세트에서 고친다).");
            return false;
        }

        return true;
    }

    internal bool SetBackShown => _setBack.IsVisible;

    internal void BackToOuterSetFromForm() => BackToOuterSet();

    private string? _rigNodeSelected;

    /// <summary>연 세트(rigKind). 없으면 null — 뷰포트는 문서의 씬을 보인다.</summary>
    internal string? SelectedSet => _setList.SelectedItem as string;

    /// <summary>테스트 손잡이 — 세트 목록에서 고른 것과 같다.</summary>
    internal void SelectSet(string? kind) => _setList.SelectedItem = kind;

    /// <summary>테스트 손잡이 — 연 세트, 고른 오브젝트(노드), 목록의 세트들, 오브젝트 칸이 열려 있는가.</summary>
    internal (string? Set, string? Node, IReadOnlyList<string> Sets, bool Editable) SetEditorState
        => (SelectedSet, _rigNodeSelected, (_setList.ItemsSource as IEnumerable<string> ?? Array.Empty<string>()).ToList(), _setNodePanel.IsVisible && _rigNodeFields.IsEnabled);

    /// <summary>테스트 손잡이 — 오브젝트 칸들.</summary>
    internal (TextBox Id, ComboBox Parent, TextBox X, TextBox Y, TextBox W, TextBox H, CheckBox Image, CheckBox Alpha) RigNodeBoxes
        => (_rigNodeId, _rigNodeParent, _rigNodeX, _rigNodeY, _rigNodeW, _rigNodeH, _rigNodeImage, _rigNodeAlpha);

    /// <summary>테스트 손잡이 — 가만히 선 자세의 회전·배율 칸.</summary>
    internal (TextBox Rotation, TextBox ScaleX, TextBox ScaleY) RigNodePoseBoxes => (_rigNodeRotation, _rigNodeScaleX, _rigNodeScaleY);

    /// <summary>테스트 손잡이 — 색 칸과 "색 사각형 더하기".</summary>
    internal TextBox RigNodeColorBox => _rigNodeColor;

    internal string? AddColorNodeFromForm() => AddColorNode(EditedRigNode?.Id ?? "");

    /// <summary>테스트 손잡이 — 그림 파일을 그 자리(루트 공간)에 그림 오브젝트로.</summary>
    internal string? AddImageObjectFromForm(string path, Ked.Presentation.Core.Vec2 at) => AddImageObject(path, at);

    /// <summary>테스트 손잡이 — 하이어아키에서 세트의 오브젝트를 고른 것과 같다.</summary>
    internal void SelectSetNode(string? id)
    {
        if (id is null)
        {
            _rigNodeSelected = null;
            RefreshSetNodePanel();
        }
        else
        {
            SelectInHierarchy(ViewportView.SetObjectId, id);
        }
    }

    internal string NewSetFromForm() => NewSet();

    /// <summary>테스트 손잡이 — 세트 이름 칸, 하이어아키 메뉴의 "새 세트로 빼기"(그 오브젝트부터 last까지의 형제들).</summary>
    internal TextBox SetNameBox => _setName;

    internal string? ExtractSetNodesFromMenu(string first, params string[] others) => ExtractSetNodes(new[] { first }.Concat(others).ToList());

    /// <summary>테스트 손잡이 — 하이어아키 메뉴의 "화면 밖에서 들어온다 / 밖으로 나간다"(지금 시각부터 0.5초).</summary>
    internal float? SlideSetNodeFromMenu(string node, StageSide side, bool leaving) => SlideSetNode(node, side, leaving);

    /// <summary>들어오고 나가는 데 걸리는 시간(초) — 메뉴의 기본. 키를 끌어 늘리거나 줄인다.</summary>
    private const float SlideSeconds = 0.5f;

    private float? SlideSetNode(string node, StageSide side, bool leaving)
    {
        if (_graph.Motion is not { } motion)
        {
            return null;
        }

        float start = (float)Math.Clamp(_graph.CurrentTime, 0, Math.Max(0, motion.Duration - 0.05));
        float distance = _session.Editor.SlideFromOutside(motion.Name, node, side, start, SlideSeconds, _session.StageSize, leaving);
        _session.SetStatus($"{node}: {start:0.##}초부터 {SlideSeconds:0.##}초 동안 무대 {SideName(side)} {(leaving ? "밖으로 나간다" : "밖에서 들어온다")}({Math.Abs(distance):0}px).");
        return distance;
    }

    private static string SideName(StageSide side) => side switch { StageSide.Left => "왼쪽", StageSide.Right => "오른쪽", StageSide.Top => "위", _ => "아래" };

    internal string? AddSetNodeFromForm(bool image) => AddSetNode(image);

    internal void RemoveSetNodeFromForm() => RemoveSelectedSetNode();

    internal void MoveSetNodeFromForm(int direction) => MoveSelectedSetNode(direction);

    private RigAsset? EditedRig => _session.Editor.Document.FindRig(SelectedSet);

    private RigNodeAsset? EditedRigNode => _rigNodeSelected is { } id ? EditedRig?.Find(id) : null;

    private readonly TextBox _setName = new() { FontSize = 11, PlaceholderText = "(세트를 연다)" };

    private void BuildSetLeft()
    {
        // 세트가 맨 위다 — 무엇을 열어 작업하는지가 먼저다(그 아래의 하이어아키·모션이 그 세트의 것이 된다).
        LeftPanel.Children.Insert(0, Header("세트"));
        _setList.SelectionChanged += (_, _) => OnSetSelected();
        ToolTip.SetTip(_setList, "세트 — 오브젝트들의 계층 + 그림 + 애니메이션들의 한 덩어리(화면 이동 하나, 이모지 무리 하나…). 고르면 뷰포트와 하이어아키가 그 세트만 보인다. 다시 눌러 풀면 씬으로 돌아간다");
        LeftPanel.Children.Insert(1, _setList);

        Button add = SmallButton("새 세트", (_, _) => Guard("새 세트", () => NewSet()));
        Button remove = SmallButton("세트 삭제", (_, _) => Guard("세트 삭제", RemoveSelectedSet));
        Button close = SmallButton("닫기", (_, _) => SelectSet(null));
        Button import = SmallButton("그림 가져오기…", async (_, _) =>
        {
            try
            {
                if (SelectedSet is not null && await PickOpenAsync("그림 PNG", new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } }) is { } path)
                {
                    Guard("그림 가져오기", () => AddImageObject(path, new Ked.Presentation.Core.Vec2(0f, 0f)));
                }
            }
            catch (Exception exception)
            {
                _session.SetStatus($"그림 가져오기 실패: {exception.Message}");
            }
        });
        Button importLayers = SmallButton("층 표 가져오기…", async (_, _) =>
        {
            try
            {
                if (SelectedSet is not null && await PickOpenAsync("층 표 (layers.tsv)", new FilePickerFileType("층 표") { Patterns = new[] { "*.tsv" } }) is { } path)
                {
                    Guard("층 표 가져오기", () => ImportLayerSheet(path));
                }
            }
            catch (Exception exception)
            {
                _session.SetStatus($"층 표 가져오기 실패: {exception.Message}");
            }
        });
        Button importSets = SmallButton("다른 문서에서…", async (_, _) =>
        {
            try
            {
                if (await PickOpenAsync("세트를 들일 저작 문서", new FilePickerFileType("저작 문서") { Patterns = new[] { "*.motion-authoring.json", "*.json" } }) is { } path)
                {
                    Guard("세트 들이기", () => ImportSetsFrom(path));
                }
            }
            catch (Exception exception)
            {
                _session.SetStatus($"세트 들이기 실패: {exception.Message}");
            }
        });
        ToolTip.SetTip(importSets, "다른 저작 문서의 세트를 이 문서로 들인다 — 세트(품은 세트까지)와 그 애니메이션·스킨·연출이 통째로 온다. 그림은 그 문서의 자리를 그대로 가리킨다(복사하지 않는다). 같은 이름이 이미 있으면 막힌다 — 한쪽의 이름을 먼저 바꾼다. 되돌리기 한 번");
        ToolTip.SetTip(importLayers, "그림 한 묶음을 한 번에 — 층 표(layers.tsv: 파일 ⇥ 왼쪽 ⇥ 위 ⇥ 너비 ⇥ 높이, 뒤에서 앞으로)를 읽어 층마다 그림 오브젝트를 그 자리에 세우고 스킨에 그림을 준다. 표의 설명 줄에 #RRGGBB가 있으면 맨 뒤에 그 색의 바탕이 선다. 되돌리기 한 번");
        ToolTip.SetTip(import, "PNG를 연 세트의 그림 오브젝트로 — 이름은 파일 이름에서, 크기는 그림의 크기, 지금 입혀 보는 스킨에 그림이 들어간다(스킨이 없으면 만든다). 되돌리기 한 번. 뷰포트에 파일을 끌어다 놓아도 된다");
        ToolTip.SetTip(add, "빈 세트(오브젝트 Body 하나)를 만들어 연다 — 이름 set, set_2…. 하이어아키에서 우클릭해 오브젝트를 더한다");
        ToolTip.SetTip(remove, "연 세트를 지운다. 그 세트의 애니메이션·스킨·연출은 남고 문제 목록이 알린다(되돌리기 한 번)");
        ToolTip.SetTip(close, "세트를 닫고 씬으로 돌아간다");
        LeftPanel.Children.Insert(2, new WrapPanel { Orientation = Orientation.Horizontal, Children = { add, remove, close, import, importLayers, importSets, SmallButton("rigs.json…", OnExportRigsClick) } });
        LeftPanel.Children.Insert(3, Labeled("세트 이름", _setName));
        LeftPanel.Children.Insert(4, _setBack);
        _setBack.Click += (_, _) => Guard("돌아가기", BackToOuterSet);
        ToolTip.SetTip(_setBack, "안쪽 세트를 닫고, 그것을 품은 세트로 돌아간다");
        ToolTip.SetTip(_setName, "연 세트의 이름 — 고치고 Enter. 그 세트의 애니메이션·스킨·연출, 그것을 품은 세트, 씬의 오브젝트가 따라온다(되돌리기 한 번). 영문자로 시작하는 영문·숫자·_");
        OnCommit(_setName, text =>
        {
            if (!_refreshing && SelectedSet is { } kind && text.Trim() != kind)
            {
                Guard("세트 이름", () =>
                {
                    _session.Editor.RenameRig(kind, text);
                    RefreshSets();
                    _setList.SelectedItem = text.Trim();
                });
                _setName.Text = SelectedSet ?? "";
            }
        });
    }

    /// <summary>"씬" 탭에 서는 칸 — 세트를 열고 그 오브젝트(노드)를 골랐을 때의 이름·부모·자리·크기.</summary>
    private Control BuildSetNodePanel()
    {
        _setNodePanel.Children.Add(Header("세트의 오브젝트"));
        _setNodePanel.Children.Add(_setNodeTitle);
        _rigNodeFields.Children.Add(Labeled("이름", _rigNodeId));
        _rigNodeFields.Children.Add(Labeled("부모", _rigNodeParent));
        _rigNodeFields.Children.Add(Labeled("X", _rigNodeX));
        _rigNodeFields.Children.Add(Labeled("Y", _rigNodeY));
        _rigNodeFields.Children.Add(Labeled("너비", _rigNodeW));
        _rigNodeFields.Children.Add(Labeled("높이", _rigNodeH));
        _rigNodeFields.Children.Add(Labeled("회전", _rigNodeRotation));
        _rigNodeFields.Children.Add(Labeled("배율 X", _rigNodeScaleX));
        _rigNodeFields.Children.Add(Labeled("배율 Y", _rigNodeScaleY));
        // 색 — 글로 적거나, 옆의 견본을 눌러 고른다(이 문서에서 쓴 색이 먼저, 그 뒤에 기본 견본).
        var colorRow = new DockPanel();
        DockPanel.SetDock(_rigNodeSwatch, Dock.Right);
        colorRow.Children.Add(_rigNodeSwatch);
        colorRow.Children.Add(_rigNodeColor);
        _rigNodeFields.Children.Add(Labeled("색", colorRow));
        ToolTip.SetTip(_rigNodeSwatch, "색 고르기 — 이 문서에서 쓴 색과 기본 견본에서 고른다. 고르면 바로 들어간다(되돌리기 한 번). 정확한 값은 왼쪽 칸에 #RRGGBB로 적는다");
        _rigNodeSwatch.Click += (_, _) => OpenColorSwatches();
        ToolTip.SetTip(_rigNodeColor, "색 — #RRGGBB 또는 #RRGGBBAA. 적으면 단색 사각형이 된다(그림 없이 너비 × 높이를 이 색으로 채운다 — 화면 전환·소개의 패널). 비우면 색 오브젝트가 아니다");
        _rigNodeFields.Children.Add(_rigNodeImage);
        _rigNodeFields.Children.Add(_rigNodeAlpha);

        Button enter = SmallButton("안쪽 세트 열기 ▸", (_, _) => Guard("안쪽 세트 열기", EnterInnerSet));
        ToolTip.SetTip(enter, "이 오브젝트가 품은 세트를 연다 — 안쪽의 자리·그림·애니메이션을 고친다. 세트 목록 아래의 \"◂ 돌아가기\"로 여기로 돌아온다. 참조라서 고친 것이 품은 곳 모두에 보인다");
        ToolTip.SetTip(_rigNodeSkin, "품은 세트에 입힐 스킨 — 같은 세트를 다른 그림으로 여러 번 품을 수 있다(빨간 차 · 파란 차)");
        _rigInstanceFields.Children.Add(Labeled("안쪽 스킨", _rigNodeSkin));
        Button inPlace = SmallButton("여기서 안쪽 고치기", (_, _) => Guard("안쪽 고치기", () => EnterInstanceInPlace()));
        ToolTip.SetTip(inPlace, "이 세트를 보는 채로, 품은 세트 안쪽의 오브젝트를 뷰포트에서 끌어 고친다 — 둘레의 것과 맞춰 보며 자리를 잡을 때. 뷰포트에서 품은 세트를 더블클릭해도 된다. Esc나 빈 곳 더블클릭으로 나간다. 애니메이션을 고르지 않은 때(배치)만");
        _rigInstanceFields.Children.Add(new WrapPanel { Orientation = Orientation.Horizontal, Children = { enter, inPlace } });
        _rigNodeFields.Children.Add(_rigInstanceFields);
        _rigNodeSkin.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && SelectedSet is { } kind && EditedRigNode is { Set.Length: > 0 } node && _rigNodeSkin.SelectedItem is string chosen)
            {
                Guard("품은 세트의 스킨", () => _session.Editor.SetRigInstanceSkin(kind, node.Id, chosen == FirstSkinChoice ? null : chosen));
            }
        };
        _setNodePanel.Children.Add(_rigNodeFields);
        ToolTip.SetTip(_rigNodeId, "오브젝트 이름(영문자로 시작하는 영문·숫자·_) — 바꾸면 그 세트의 애니메이션 채널·스킨 칸·연출의 자리 노드가 따라온다");
        ToolTip.SetTip(_rigNodeParent, "부모 — 밑의 오브젝트도 함께 옮겨진다");
        ToolTip.SetTip(_rigNodeX, "부모 가운데에서의 자리(px) — 가만히 선 자세. 애니메이션은 여기에 얹힌다");
        ToolTip.SetTip(_rigNodeW, "사각형의 크기(px) — 그림이면 그림이 이 안에 종횡비로 맞춰진다. 묶기만 하는 오브젝트는 0");

        OnCommit(_rigNodeId, text =>
        {
            if (!_refreshing && SelectedSet is { } kind && EditedRigNode is { } node && text.Trim() != node.Id)
            {
                Guard("오브젝트 이름", () =>
                {
                    _session.Editor.RenameRigNode(kind, node.Id, text);
                    _rigNodeSelected = text.Trim();
                    _viewport.SelectedNode = _rigNodeSelected;
                    RefreshSetNodePanel();
                });
            }
        });
        _rigNodeParent.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && SelectedSet is { } kind && EditedRigNode is { } node && _rigNodeParent.SelectedItem is string chosen)
            {
                Guard("오브젝트 부모", () => _session.Editor.SetRigNodeParent(kind, node.Id, chosen == RootParentChoice ? null : chosen));
            }
        };

        ToolTip.SetTip(_rigNodeRotation, "가만히 선 자세의 회전(도, 반시계 +) — 뷰포트에서 Ctrl+끌기");
        ToolTip.SetTip(_rigNodeScaleX, "가만히 선 자세의 배율 — 뷰포트에서 Shift+끌기(둘 다 같이). 음수면 뒤집힌다");

        // 뷰포트에 PNG를 끌어다 놓으면 그 자리에 그림 오브젝트가 선다(세트를 열었을 때).
        DragDrop.SetAllowDrop(_viewport, true);
        _viewport.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (SelectedSet is null)
            {
                return;
            }

            Ked.Presentation.Core.Vec2 at = _viewport.ToRoot(e.GetPosition(_viewport));

            foreach (string path in (e.DataTransfer.TryGetFiles() ?? Array.Empty<IStorageItem>())
                         .Select(f => f.TryGetLocalPath()).OfType<string>().Where(p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
            {
                string captured = path;
                Guard("그림 놓기", () => AddImageObject(captured, at));
                at = new Ked.Presentation.Core.Vec2(at.X + 24f, at.Y - 24f); // 여럿이면 조금씩 비켜 놓는다
            }

            e.Handled = true;
        });

        // 안쪽 고치기에 들어가고 나올 때 — 칸의 안내가 바뀌고, 나오면 그 인스턴스가 골라진다.
        _viewport.EnteredInstanceChanged += (_, _) =>
        {
            if (_viewport.EnteredInstance is null && _viewport.SelectedNode is { } back && EditedRig?.Find(back) is not null)
            {
                _rigNodeSelected = back;
            }

            RefreshSetNodePanel();
            RebuildHierarchy();
        };

        // 뷰포트에서 집은 오브젝트가 하이어아키와 칸에서도 골라진다.
        _viewport.LayoutNodePicked += (_, id) =>
        {
            if (_rigNodeSelected != id)
            {
                _rigNodeSelected = id;
                RefreshSetNodePanel();
                RebuildHierarchy();
            }
        };

        foreach (TextBox box in new[] { _rigNodeX, _rigNodeY, _rigNodeW, _rigNodeH, _rigNodeRotation, _rigNodeScaleX, _rigNodeScaleY, _rigNodeColor })
        {
            OnCommit(box, _ => CommitRigNode());
        }

        _rigNodeImage.IsCheckedChanged += (_, _) => CommitRigNode();
        _rigNodeAlpha.IsCheckedChanged += (_, _) => CommitRigNode();
        return _setNodePanel;
    }

    private void OnSetSelected()
    {
        if (_refreshing)
        {
            return;
        }

        // 목록에서 직접 다른 세트를 고르면 "돌아갈 길"은 뜻이 없다.
        if (!_navigatingSets)
        {
            _setTrail.Clear();
        }

        if (SelectedSet is { } kind)
        {
            // 세트를 열면 연출 보기를 떠난다. 그 세트의 것이 아닌 모션도 풀린다(이 세트가 연기할 수 없다).
            DeselectEmoji();

            if (SelectedMotion is { } motion && _session.Editor.Document.Find(motion)?.Rig != kind)
            {
                _motionList.SelectedItem = null;
            }
        }

        ApplySetView();
    }

    /// <summary>목록의 선택을 뷰포트에 — 세트 보기를 켜거나 끄고, 하이어아키와 칸을 다시 세운다.</summary>
    private void ApplySetView()
    {
        if (!_setName.IsFocused)
        {
            _setName.Text = SelectedSet ?? "";
        }

        _setName.IsEnabled = SelectedSet is not null;

        if (_viewport.SetView == SelectedSet)
        {
            return;
        }

        _rigNodeSelected = null;
        _viewport.SetView = SelectedSet;
        BindMotion(SelectedMotion); // 뷰포트의 씬이 바뀌었다 — 묶인 것을 그 씬에서 다시 본다
        RefreshAssets();
        RefreshObjects();
        RefreshSetNodePanel();
        RefreshMotionList(); // 세트의 애니메이션만 / 전부

        if (SelectedSet is not null)
        {
            ExpandObjectNodes(ViewportView.SetObjectId, true); // 세트를 열면 그 오브젝트들이 바로 보인다
            _viewport.FitToSet();                              // 작은 세트가 무대 한가운데의 점으로 서지 않게
        }
        else
        {
            _viewport.ResetView();
        }
    }

    /// <summary>세트 보기를 떠난다 — 연출을 골랐거나 다른 리그의 모션을 골랐을 때.</summary>
    private void DeselectSet()
    {
        if (SelectedSet is null)
        {
            return;
        }

        _refreshDepth++;

        try
        {
            _setList.SelectedItem = null;
        }
        finally
        {
            _refreshDepth--;
        }

        ApplySetView();
    }

    private void RefreshSets()
    {
        _refreshDepth++;

        try
        {
            string? selected = SelectedSet;
            List<string> kinds = _session.Editor.Document.Rigs.Select(r => r.Kind).OrderBy(k => k, StringComparer.Ordinal).ToList();
            _setList.ItemsSource = kinds;
            _setList.SelectedIndex = selected is null ? -1 : kinds.IndexOf(selected); // 지워졌으면(되돌리기 등) 풀린다
        }
        finally
        {
            _refreshDepth--;
        }

        ApplySetView();
        RefreshSetNodePanel();
    }

    /// <summary>
    /// 하이어아키에서 고른 오브젝트들(같은 부모 밑 — 떨어져 있어도 된다)과 그 밑을 새 세트로 뺀다 — 이름은 첫 오브젝트에서(세트 이름 칸에서 고친다).
    /// 자리·그림·키를 데리고 가고 그 자리에 인스턴스가 선다. 보이는 것은 그대로다.
    /// </summary>
    private string? ExtractSetNodes(IReadOnlyList<string> ids)
    {
        if (EditedRig is not { } rig || ids.Count == 0 || rig.Find(ids[0]) is null)
        {
            return null;
        }

        string first = ids[0];
        string stem = first.ToLowerInvariant();
        string kind = stem;

        for (int i = 2; _session.Rig?.RigKinds.Contains(kind) == true || _session.Editor.Document.FindRig(kind) is not null; i++)
        {
            kind = $"{stem}_{i.ToString(CultureInfo.InvariantCulture)}";
        }

        string instance = _session.Editor.ExtractRigNodes(rig.Kind, ids, kind);
        RefreshSets();
        SelectSetNode(instance);
        _session.SetStatus($"오브젝트 {ids.Count}개를 새 세트 '{kind}'로 뺐다 — 그 자리에 인스턴스 '{instance}'가 섰다. 세트 '{kind}'를 열어 이름을 고치거나 안쪽을 고친다.");
        return instance;
    }

    /// <summary>고른 인스턴스가 품은 세트를 연다 — 돌아올 길을 적어 둔다.</summary>
    private void EnterInnerSet()
    {
        if (SelectedSet is not { } outer || EditedRigNode is not { Set: { Length: > 0 } inner })
        {
            return;
        }

        _navigatingSets = true;

        try
        {
            _setList.SelectedItem = inner;
        }
        finally
        {
            _navigatingSets = false;
        }

        _setTrail.Push(outer);
        RefreshSetNodePanel();
    }

    private void BackToOuterSet()
    {
        if (_setTrail.Count == 0)
        {
            return;
        }

        string outer = _setTrail.Pop();
        string? inner = SelectedSet;
        _navigatingSets = true;

        try
        {
            _setList.SelectedItem = outer;
        }
        finally
        {
            _navigatingSets = false;
        }

        // 들어갔던 그 인스턴스를 다시 고른다.
        if (EditedRig?.Nodes.FirstOrDefault(n => n.Set == inner) is { } instance)
        {
            SelectSetNode(instance.Id);
        }

        RefreshSetNodePanel();
    }

    /// <summary>테스트 손잡이 — "다른 문서에서…"(파일 고르기 뒤의 일).</summary>
    internal IReadOnlyList<string> ImportSetsFromFile(string path) => ImportSetsFrom(path);

    private IReadOnlyList<string> ImportSetsFrom(string path)
    {
        IReadOnlyList<string> kinds = _session.ImportSets(path);
        RefreshSets();
        return kinds;
    }

    private string NewSet()
    {
        string kind = "set";

        for (int i = 2; _session.Rig?.RigKinds.Contains(kind) == true || _session.Editor.Document.FindRig(kind) is not null; i++)
        {
            kind = $"set_{i.ToString(CultureInfo.InvariantCulture)}";
        }

        _session.Editor.AddRig(new RigAsset { Kind = kind, Nodes = { new RigNodeAsset { Id = "Body", Alpha = true } } });
        RefreshSets();
        _setList.SelectedItem = kind;
        SelectSetNode("Body");
        return kind;
    }

    /// <summary>
    /// 그림 파일을 연 세트의 그림 오브젝트로 — 오브젝트를 만들고(이름은 파일 이름에서, 크기는 그림의 픽셀 크기), 그 자리에 놓고, 입혀 보는 스킨에 그림을 준다.
    /// 스킨이 없으면 세트의 이름으로 만든다. 통째로 되돌리기 한 칸. 고른 오브젝트가 묶는 것이면 그 밑에, 아니면 맨 위에 선다.
    /// </summary>
    private string? AddImageObject(string path, Ked.Presentation.Core.Vec2 at)
    {
        if (SelectedSet is not { } kind || EditedRig is not { } rig)
        {
            return null;
        }

        // 이름: 파일 이름의 영문·숫자만(첫 글자는 영문) — 한글 이름의 파일이면 Part1….
        string stem = new string(Path.GetFileNameWithoutExtension(path).Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');

        if (stem.Length == 0 || !char.IsAsciiLetter(stem[0]))
        {
            stem = "Part";
        }

        string id = stem;

        for (int i = 2; rig.Find(id) is not null; i++)
        {
            id = $"{stem}_{i.ToString(CultureInfo.InvariantCulture)}";
        }

        float width = RigTemplates.DefaultPartSize;
        float height = RigTemplates.DefaultPartSize;

        if (PngSizeOf(path) is { } size)
        {
            width = size.Width;
            height = size.Height;
        }

        string under = EditedRigNode is { Image: false } group ? group.Id : "";
        at = _viewport.ToSetLocal(under, at); // 놓은 자리(루트 공간)를 그 부모 안의 좌표로
        MotionEditor editor = _session.Editor;
        string? skin = _viewport.SetSkin;

        editor.Batch(MotionChangeKind.Document, () =>
        {
            if (skin is null)
            {
                skin = kind;

                for (int i = 2; editor.Document.FindSkin(skin) is not null; i++)
                {
                    skin = $"{kind}_{i.ToString(CultureInfo.InvariantCulture)}";
                }

                editor.AddSkin(skin, kind);
            }

            editor.AddRigNode(kind, id, under, image: true);
            editor.UpdateRigNode(kind, id, new RigNodeAsset { X = MathF.Round(at.X), Y = MathF.Round(at.Y), Width = width, Height = height, Image = true, Alpha = true });
            _session.SetSkinPart(skin, id, path);
        });

        _viewport.SetSkin = skin;
        SelectSetNode(id);
        return id;
    }

    /// <summary>테스트 손잡이 — "층 표 가져오기…"에서 파일을 고른 것과 같다.</summary>
    internal IReadOnlyList<string> ImportLayerSheetFromForm(string path) => ImportLayerSheet(path);

    /// <summary>층 표를 연 세트로 들인다 — 입혀 보는 스킨에(없으면 세트의 이름으로 만든다). 들인 뒤 보기를 세트에 맞춘다.</summary>
    private IReadOnlyList<string> ImportLayerSheet(string path)
    {
        if (SelectedSet is not { } kind)
        {
            return Array.Empty<string>();
        }

        string skin = _viewport.SetSkin ?? kind;

        for (int i = 2; _viewport.SetSkin is null && _session.Editor.Document.FindSkin(skin) is { } taken && taken.Rig != kind; i++)
        {
            skin = $"{kind}_{i.ToString(CultureInfo.InvariantCulture)}";
        }

        IReadOnlyList<string> created = _session.ImportLayers(kind, path, skin);
        _viewport.SetSkin = skin;
        RefreshAssets();
        Avalonia.Threading.Dispatcher.UIThread.Post(_viewport.FitToSet);
        return created;
    }

    /// <summary>PNG의 픽셀 크기 — 머리(IHDR)만 읽는다(그림을 풀지 않는다). PNG가 아니거나 못 읽으면 null(본의 크기로 선다).</summary>
    private static (int Width, int Height)? PngSizeOf(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            Span<byte> head = stackalloc byte[24];

            if (stream.Read(head) < 24 || head[0] != 0x89 || head[1] != (byte)'P' || head[2] != (byte)'N' || head[3] != (byte)'G')
            {
                return null;
            }

            int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(head[16..20]);
            int height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(head[20..24]);
            return width > 0 && height > 0 ? (width, height) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void RemoveSelectedSet()
    {
        if (SelectedSet is { } kind)
        {
            _session.Editor.RemoveRig(kind);
        }
    }

    private void CommitRigNode()
    {
        if (_refreshing || SelectedSet is not { } kind || EditedRigNode is not { } node)
        {
            return;
        }

        var value = new RigNodeAsset
        {
            X = TryFloat(_rigNodeX.Text ?? "", out float x) ? x : node.X,
            Y = TryFloat(_rigNodeY.Text ?? "", out float y) ? y : node.Y,
            Width = TryFloat(_rigNodeW.Text ?? "", out float w) ? w : node.Width,
            Height = TryFloat(_rigNodeH.Text ?? "", out float h) ? h : node.Height,
            Rotation = TryFloat(_rigNodeRotation.Text ?? "", out float rotation) ? rotation : node.Rotation,
            ScaleX = TryFloat(_rigNodeScaleX.Text ?? "", out float sx) ? sx : node.ScaleX,
            ScaleY = TryFloat(_rigNodeScaleY.Text ?? "", out float sy) ? sy : node.ScaleY,
            Color = _rigNodeColor.Text,
            Image = _rigNodeImage.IsChecked == true,
            Alpha = _rigNodeAlpha.IsChecked == true,
        };
        Guard("오브젝트 값", () => _session.Editor.UpdateRigNode(kind, node.Id, value));
    }

    /// <summary>오브젝트를 더한다 — 고른 오브젝트 밑에(없으면 맨 위에). 이름은 Part1…(그림) · Node1…(묶음).</summary>
    private string? AddSetNode(bool image, string? under = null)
    {
        if (SelectedSet is not { } kind || EditedRig is not { } rig)
        {
            return null;
        }

        string stem = image ? "Part" : "Node";
        string id = stem + "1";

        for (int i = 2; rig.Find(id) is not null; i++)
        {
            id = stem + i.ToString(CultureInfo.InvariantCulture);
        }

        _session.Editor.AddRigNode(kind, id, under ?? EditedRigNode?.Id, image);
        SelectSetNode(id);
        return id;
    }

    /// <summary>테스트 손잡이 — 하이어아키의 "세트 넣기 ▸ (세트)".</summary>
    internal string? AddSetInstanceFromMenu(string setKind) => AddSetInstance(setKind, EditedRigNode?.Id ?? "");

    /// <summary>다른 세트를 품는다 — 인스턴스 오브젝트의 이름은 그 세트의 이름에서(겹치면 번호).</summary>
    private string? AddSetInstance(string setKind, string under)
    {
        if (SelectedSet is not { } kind || EditedRig is not { } rig)
        {
            return null;
        }

        string stem = new string(setKind.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());
        stem = stem.Length > 0 && char.IsAsciiLetter(stem[0]) ? stem : "Set";
        string id = stem;

        for (int i = 2; rig.Find(id) is not null; i++)
        {
            id = $"{stem}_{i.ToString(CultureInfo.InvariantCulture)}";
        }

        _session.Editor.AddRigInstance(kind, id, setKind, under);
        RefreshAssets();
        SelectSetNode(id);
        return id;
    }

    /// <summary>색 사각형을 더한다 — 오브젝트를 만들고 크기와 색을 주는 것이 한 칸. 이름은 Color1….</summary>
    private string? AddColorNode(string under)
    {
        if (SelectedSet is not { } kind || EditedRig is not { } rig)
        {
            return null;
        }

        string id = "Color1";

        for (int i = 2; rig.Find(id) is not null; i++)
        {
            id = "Color" + i.ToString(CultureInfo.InvariantCulture);
        }

        MotionEditor editor = _session.Editor;
        editor.Batch(MotionChangeKind.Document, () =>
        {
            editor.AddRigNode(kind, id, under, image: false);
            editor.UpdateRigNode(kind, id, new RigNodeAsset { Width = 400f, Height = 300f, Color = "#3D5A80", Alpha = true });
        });
        SelectSetNode(id);
        return id;
    }

    private void RemoveSelectedSetNode()
    {
        if (SelectedSet is { } kind && EditedRigNode is { } node)
        {
            string? parent = node.Parent.Length > 0 ? node.Parent : null;
            _session.Editor.RemoveRigNode(kind, node.Id);
            SelectSetNode(parent);
        }
    }

    private void MoveSelectedSetNode(int direction)
    {
        if (SelectedSet is { } kind && EditedRigNode is { } node)
        {
            _session.Editor.MoveRigNode(kind, node.Id, direction);
        }
    }

    /// <summary>세트 보기의 하이어아키 우클릭 — 오브젝트 더하기(여기 밑에) · 지우기 · 순서 · 부모.</summary>
    private void AddSetMenu(object? target)
    {
        string? nodeId = target is HierarchyNode { NodeId: { } id } ? id : null;
        RigAsset? rig = EditedRig;

        MenuItem Item(ItemsControl into, string header, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, FontSize = 11, IsEnabled = enabled };
            item.Click += (_, _) => Guard(header, action);
            into.Items.Add(item);
            return item;
        }

        string where = nodeId is null ? "맨 위에" : $"{nodeId} 밑에";
        Item(_hierarchyMenu, $"오브젝트 더하기 — {where}", () => AddSetNode(image: false, under: nodeId ?? ""));
        Item(_hierarchyMenu, $"그림 오브젝트 더하기 — {where}", () => AddSetNode(image: true, under: nodeId ?? ""));
        Item(_hierarchyMenu, $"색 사각형 더하기 — {where}", () => AddColorNode(nodeId ?? ""));

        // 다른 세트를 품는다 — 그 세트의 인스턴스(참조). 서로 품게 되는 것은 편집기가 막는다.
        if (rig is not null && _session.Editor.Document.Rigs.Count > 1 && (nodeId is null || !nodeId.Contains(RigAssetCodec.PathSeparator, StringComparison.Ordinal)))
        {
            var insert = new MenuItem { Header = $"세트 넣기 — {where}", FontSize = 11 };

            foreach (string other in _session.Editor.Document.Rigs.Select(r => r.Kind).Where(k => k != rig.Kind).OrderBy(k => k, StringComparer.Ordinal))
            {
                string captured = other;
                Item(insert, captured, () => AddSetInstance(captured, nodeId ?? ""));
            }

            _hierarchyMenu.Items.Add(insert);
        }

        if (nodeId is null || rig?.Find(nodeId) is not { } node)
        {
            return;
        }

        void Pick(Action then)
        {
            SelectSetNode(nodeId);
            then();
        }

        // 따로 보기 — 이 오브젝트만 보거나 숨긴다(보기 상태: 문서를 안 바꾼다).
        _hierarchyMenu.Items.Add(new Separator());
        bool solo = _viewport.SoloSetNode == nodeId;
        bool hidden = _viewport.IsSetNodeHidden(nodeId);
        Item(_hierarchyMenu, solo ? "이것만 보기 풀기" : "이것만 보기 (솔로)", () => SetSetNodeView(nodeId, solo: !solo, hidden: null));
        Item(_hierarchyMenu, hidden ? "보이기" : "숨기기", () => SetSetNodeView(nodeId, solo: null, hidden: !hidden));
        Item(_hierarchyMenu, "전부 보이기", () => SetSetNodeView(null, null, null), enabled: _viewport.SoloSetNode is not null || rig.Nodes.Any(n => _viewport.IsSetNodeHidden(n.Id)));

        // 시차 — 고른 애니메이션에서 이 오브젝트의 움직임을 통째로 민다(키를 하나씩 옮기지 않는다).
        if (_graph.Motion is { } animation && animation.Rig == rig.Kind)
        {
            _hierarchyMenu.Items.Add(new Separator());
            var shift = new MenuItem { Header = $"움직임 밀기 — {animation.Name}", FontSize = 11, IsEnabled = animation.Channels.Any(c => c.Node == nodeId) };

            foreach (float delta in new[] { 0.05f, 0.1f, 0.2f, -0.05f, -0.1f, -0.2f })
            {
                float captured = delta;
                Item(shift, captured > 0 ? $"{captured:0.##}초 늦게" : $"{-captured:0.##}초 일찍", () => ShiftSetNodeKeys(nodeId, captured));
            }

            _hierarchyMenu.Items.Add(shift);
            var stagger = new MenuItem { Header = "여기서부터 차례로 (아래 형제들에 시차)", FontSize = 11 };

            foreach (float step in new[] { 0.05f, 0.1f, 0.2f })
            {
                float captured = step;
                Item(stagger, $"{captured:0.##}초씩", () => StaggerSetNodesFrom(nodeId, captured));
            }

            _hierarchyMenu.Items.Add(stagger);

            // 제 차례 전에는 숨긴다 — 가만히 선 자세는 "다 보이는" 것이라, 늦게 나오는 오브젝트를 숨겨 두는 불투명도 키를 대신 놓는다.
            // 화면 밖에서 들어오고 나간다 — 얼마나 옮겨야 화면 밖인지를 대신 센다(지금 시각부터 0.5초).
            foreach (bool leaving in new[] { false, true })
            {
                var slide = new MenuItem { Header = leaving ? "화면 밖으로 나간다 (지금 시각부터)" : "화면 밖에서 들어온다 (지금 시각부터)", FontSize = 11 };

                foreach (StageSide side in new[] { StageSide.Left, StageSide.Right, StageSide.Top, StageSide.Bottom })
                {
                    StageSide capturedSide = side;
                    bool capturedLeaving = leaving;
                    Item(slide, SideName(side) + (!leaving ? "에서" : side is StageSide.Top or StageSide.Bottom ? "로" : "으로"), () => SlideSetNode(nodeId, capturedSide, capturedLeaving));
                }

                _hierarchyMenu.Items.Add(slide);
            }

            Item(_hierarchyMenu, "제 차례 전까지 숨기기", () => HideSetNodeForTurn(nodeId, until: true));
            Item(_hierarchyMenu, "움직임을 마치면 숨기기", () => HideSetNodeForTurn(nodeId, until: false));
            Item(_hierarchyMenu, "전부: 제 차례 전까지 숨기기", () => HideSetNodeForTurn(null, until: true));
        }

        _hierarchyMenu.Items.Add(new Separator());
        Item(_hierarchyMenu, "앞으로 (아래에 그려진다)", () => Pick(() => MoveSelectedSetNode(-1)));
        Item(_hierarchyMenu, "뒤로 (위에 그려진다)", () => Pick(() => MoveSelectedSetNode(+1)));

        var parents = new MenuItem { Header = "부모 바꾸기", FontSize = 11 };

        foreach (string choice in ParentChoicesOf(rig, node))
        {
            string captured = choice;
            Item(parents, choice + ((choice == RootParentChoice ? "" : choice) == node.Parent ? "  ✓" : ""), () => _session.Editor.SetRigNodeParent(rig.Kind, node.Id, captured == RootParentChoice ? null : captured));
        }

        _hierarchyMenu.Items.Add(parents);

        // 새 세트로 빼기 — 이 오브젝트(와 그 밑), 또는 여기서부터 아래 형제 몇까지를 따로 세트로. 그 자리에는 인스턴스가 선다.
        if (!nodeId.Contains(RigAssetCodec.PathSeparator, StringComparison.Ordinal))
        {
            var extract = new MenuItem { Header = "새 세트로 빼기", FontSize = 11 };
            ToolTip.SetTip(extract, "고른 오브젝트들을 따로 세트로 뺀다 — 자리·그림·키를 데리고 가고, 그 자리에는 새 세트의 인스턴스가 서고, 애니메이션에는 그것을 트는 재생 블록이 선다. 보이는 것은 그대로다(되돌리기 한 번)");
            Item(extract, "이것만 (밑의 오브젝트까지)", () => ExtractSetNodes(new[] { nodeId }));

            // 함께 뺄 형제를 고른다 — 떨어져 있어도 된다(체크하고 "함께 뺀다"). 메뉴는 체크하는 동안 열려 있다.
            List<string> siblings = rig.Nodes.Where(n => n.Parent == node.Parent && n.Id != nodeId).Select(n => n.Id).ToList();

            if (siblings.Count > 0)
            {
                extract.Items.Add(new Separator());
                var picks = new List<MenuItem>();

                foreach (string sibling in siblings)
                {
                    var pick = new MenuItem { Header = sibling, FontSize = 11, ToggleType = MenuItemToggleType.CheckBox, StaysOpenOnClick = true };
                    picks.Add(pick);
                    extract.Items.Add(pick);
                }

                extract.Items.Add(new Separator());
                Item(extract, "체크한 것과 함께 뺀다", () => ExtractSetNodes(new[] { nodeId }.Concat(picks.Where(p => p.IsChecked).Select(p => (string)p.Header!)).ToList()));
            }

            _hierarchyMenu.Items.Add(extract);
        }

        _hierarchyMenu.Items.Add(new Separator());
        Item(_hierarchyMenu, "지우기 (밑의 오브젝트도)", () => Pick(RemoveSelectedSetNode));
    }

    /// <summary>테스트 손잡이 — 하이어아키 메뉴의 "움직임 밀기" · "여기서부터 차례로".</summary>
    internal void ShiftSetNodeKeysFromMenu(string node, float delta) => ShiftSetNodeKeys(node, delta);

    internal void StaggerSetNodesFromMenu(string node, float step) => StaggerSetNodesFrom(node, step);

    /// <summary>테스트 손잡이 — 하이어아키 메뉴의 "제 차례 전까지 숨기기" · "움직임을 마치면 숨기기" · "전부"(node = null).</summary>
    internal void HideSetNodeForTurnFromMenu(string? node, bool until) => HideSetNodeForTurn(node, until);

    private void HideSetNodeForTurn(string? node, bool until)
    {
        if (_graph.Motion is not { } motion)
        {
            return;
        }

        if (node is null)
        {
            _session.SetStatus($"{motion.Name}: 오브젝트 {_session.Editor.HideAllUntilTurn(motion.Name)}개를 제 차례 전까지 숨겼다.");
            return;
        }

        if (EditedRig?.Find(node) is { Alpha: false })
        {
            _session.SetStatus($"{node}: 불투명도를 받지 않는 오브젝트다 — \"불투명도를 받는다\"를 켠다.");
            return;
        }

        bool done = until ? _session.Editor.HideUntilTurn(motion.Name, node) : _session.Editor.HideAfterTurn(motion.Name, node);

        if (!done)
        {
            _session.SetStatus(until ? $"{node}: 처음부터 움직인다(숨길 구간이 없다)." : $"{node}: 끝까지 움직인다(숨길 구간이 없다).");
        }
    }

    private void ShiftSetNodeKeys(string node, float delta)
    {
        if (_graph.Motion is { } motion && !_session.Editor.ShiftNodeKeys(motion.Name, node, delta))
        {
            _session.SetStatus($"{node}: {motion.Name}에 이 오브젝트의 키가 없다(밀 것이 없다).");
        }
    }

    /// <summary>그 오브젝트와 그 뒤의 형제들에 차례로 시차를 준다 — 첫째(고른 것)는 그대로, 다음은 step, 그다음은 2·step… 한 칸.</summary>
    private void StaggerSetNodesFrom(string node, float step)
    {
        if (_graph.Motion is not { } motion || EditedRig is not { } rig || rig.Find(node) is not { } first)
        {
            return;
        }

        List<string> siblings = rig.Nodes.Where(n => n.Parent == first.Parent).Select(n => n.Id).SkipWhile(id => id != node).ToList();
        _session.Editor.StaggerNodes(motion.Name, siblings, step);
    }

    /// <summary>테스트 손잡이 — 따로 보기(솔로 · 숨김). 셋 다 null이면 전부 보이기.</summary>
    internal void SetSetNodeViewFromMenu(string? node, bool? solo, bool? hidden) => SetSetNodeView(node, solo, hidden);

    private void SetSetNodeView(string? node, bool? solo, bool? hidden)
    {
        if (node is null)
        {
            _viewport.ShowAllSetNodes();
        }
        else
        {
            if (solo is { } s)
            {
                _viewport.SoloSetNode = s ? node : null;
            }

            if (hidden is { } h)
            {
                _viewport.SetSetNodeHidden(node, h);
            }
        }

        RebuildHierarchy(); // 표시(◉ · 숨김)가 따라온다
    }

    /// <summary>부모로 고를 수 있는 것 — 제 식구(밑의 오브젝트)가 아닌 것.</summary>
    private static List<string> ParentChoicesOf(RigAsset rig, RigNodeAsset node)
    {
        var parents = new List<string> { RootParentChoice };
        var family = new HashSet<string>(StringComparer.Ordinal) { node.Id };

        foreach (RigNodeAsset n in rig.Nodes)
        {
            if (family.Contains(n.Parent))
            {
                family.Add(n.Id);
            }
            else if (n.Id != node.Id)
            {
                parents.Add(n.Id);
            }
        }

        return parents;
    }

    private void RefreshSetNodePanel()
    {
        _refreshDepth++;

        try
        {
            RigAsset? rig = EditedRig;

            if (rig?.Find(_rigNodeSelected ?? "") is null)
            {
                _rigNodeSelected = null;
            }

            RigNodeAsset? node = EditedRigNode;
            _setNodePanel.IsVisible = rig is not null;
            _rigNodeFields.IsEnabled = node is not null;
            _setNodeTitle.Text = rig is null ? ""
                : _viewport.EnteredInstance is { } inside ? $"세트 {rig.Kind} › {inside} 안쪽을 고치는 중 — 뷰포트에서 안쪽의 오브젝트를 끌어 자리·배율·회전을 잡는다(품은 세트가 바뀐다 — 품은 곳 모두에 보인다). 숫자로 고치려면 \"안쪽 세트 열기\". Esc로 나간다"
                : node is null ? $"세트 {rig.Kind} — 오브젝트 {rig.Nodes.Count}개. 하이어아키에서 오브젝트를 고르면 여기서 고친다(우클릭: 더하기 · 지우기 · 순서 · 부모)"
                : string.IsNullOrEmpty(node.Set) ? $"세트 {rig.Kind} › {node.Id}"
                : $"세트 {rig.Kind} › {node.Id} → 세트 {node.Set}를 품었다(참조 — 안쪽은 그 세트를 열어 고친다. 이 칸의 자리·배율·회전은 통째로 옮긴다)";

            void Write(TextBox box, string text)
            {
                if (!box.IsFocused)
                {
                    box.Text = text;
                }
            }

            Write(_rigNodeId, node?.Id ?? "");
            Write(_rigNodeX, node is null ? "" : F(node.X));
            Write(_rigNodeY, node is null ? "" : F(node.Y));
            Write(_rigNodeW, node is null ? "" : F(node.Width));
            Write(_rigNodeH, node is null ? "" : F(node.Height));
            Write(_rigNodeRotation, node is null ? "" : F(node.Rotation));
            Write(_rigNodeScaleX, node is null ? "" : F(node.ScaleX));
            Write(_rigNodeScaleY, node is null ? "" : F(node.ScaleY));
            Write(_rigNodeColor, node?.Color ?? "");
            _rigNodeSwatch.Background = node?.Color is { Length: > 0 } fill && Avalonia.Media.Color.TryParse(fill.Length == 9 ? fill[..7] : fill, out Avalonia.Media.Color shown)
                ? new Avalonia.Media.SolidColorBrush(shown)
                : Avalonia.Media.Brushes.Transparent;
            _rigNodeImage.IsChecked = node?.Image == true;
            _rigNodeAlpha.IsChecked = node?.Alpha == true;

            // 인스턴스의 칸 — 품은 세트의 스킨들.
            bool instance = node is { Set.Length: > 0 };
            _rigInstanceFields.IsVisible = instance;

            if (instance)
            {
                var skins = new List<string> { FirstSkinChoice };
                skins.AddRange(_session.Editor.Document.Skins.Where(k => k.Rig == node!.Set).Select(k => k.Name));
                _rigNodeSkin.ItemsSource = skins;
                _rigNodeSkin.SelectedIndex = node!.SetSkin is null ? 0 : Math.Max(0, skins.IndexOf(node.SetSkin));
            }

            // 돌아가기 — 안쪽 세트로 들어온 길이 지금 연 세트에서 끝나면 선다.
            while (_setTrail.Count > 0 && (SelectedSet is null || _session.Editor.Document.FindRig(_setTrail.Peek()) is null))
            {
                _setTrail.Clear();
            }

            _setBack.IsVisible = _setTrail.Count > 0;
            _setBack.Content = _setTrail.Count > 0 ? $"◂ {_setTrail.Peek()}로 돌아가기" : "";

            List<string> parents = rig is not null && node is not null ? ParentChoicesOf(rig, node) : new List<string> { RootParentChoice };
            _rigNodeParent.ItemsSource = parents;
            _rigNodeParent.SelectedIndex = node is null ? -1 : node.Parent.Length == 0 ? 0 : parents.IndexOf(node.Parent);
        }
        finally
        {
            _refreshDepth--;
        }
    }

    private async void OnExportRigsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? path = await PickSaveAsync("rigs.json 내보내기", "rigs.json", new FilePickerFileType("rigs.json") { Patterns = new[] { "*.json" } });

            if (path is not null)
            {
                _session.ExportRigs(path);
            }
        }
        catch (Exception exception)
        {
            _session.SetStatus($"리그 내보내기 실패: {exception.Message}");
        }
    }
}
