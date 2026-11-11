using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using ScreenTransition.App.Views;
using ScreenTransition.Authoring.Editing;
using ScreenTransition.Authoring.Model;

namespace ScreenTransition.App;

// 전환 — 층들의 계층 + 그림 + 타임라인 하나의 한 덩어리(TransitionAsset).
// 왼쪽 목록에서 전환을 열면 뷰포트·하이어아키가 그 전환만 보이고, 하이어아키에서 층을 더하고 지우고 옮긴다.
public partial class MainWindow
{
    private const string RootParentChoice = "(맨 위)";

    private readonly ListBox _transitionList = new() { MinHeight = 44, MaxHeight = 110, FontSize = 11 };
    private readonly StackPanel _layerPanel = new() { Spacing = 4, IsVisible = false };
    private readonly TextBlock _layerTitle = new() { FontSize = 10, Opacity = 0.75, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly StackPanel _layerFields = new() { Spacing = 4 };
    private readonly TextBox _layerId = new() { FontSize = 11 };
    private readonly ComboBox _layerParent = new() { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _layerX = new() { FontSize = 11 };
    private readonly TextBox _layerY = new() { FontSize = 11 };
    private readonly TextBox _layerW = new() { FontSize = 11 };
    private readonly TextBox _layerH = new() { FontSize = 11 };
    private readonly TextBox _layerRotation = new() { FontSize = 11 };
    private readonly TextBox _layerScaleX = new() { FontSize = 11 };
    private readonly TextBox _layerScaleY = new() { FontSize = 11 };
    private readonly TextBox _layerColor = new() { FontSize = 11, PlaceholderText = "(색 없음) #RRGGBB" };
    private readonly TextBox _layerImage = new() { FontSize = 11, PlaceholderText = "그림 없음" };
    private readonly CheckBox _layerAlpha = new() { Content = "불투명도를 받는다", FontSize = 11 };
    private readonly Button _layerSwatch = new() { Width = 22, Height = 20, Margin = new Avalonia.Thickness(3, 0, 0, 0), Padding = new Avalonia.Thickness(0), BorderThickness = new Avalonia.Thickness(1), BorderBrush = Avalonia.Media.Brushes.Gray };

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
        => _session.Editor.Document.Transitions.SelectMany(r => r.Layers).Select(n => n.Color).OfType<string>().Where(c => c.Length > 0)
            .Concat(DefaultSwatches).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private void ApplySwatch(string color)
    {
        if (EditedLayer is null)
        {
            return;
        }

        _layerColor.Text = color;
        Guard("색", CommitLayer);
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

        flyout.ShowAt(_layerSwatch);
    }

    private string? _layerSelected;

    /// <summary>고른 전환. 없으면 null — 뷰포트가 빈다.</summary>
    internal string? SelectedTransition => _transitionList.SelectedItem as string;

    /// <summary>테스트 손잡이 — 전환 목록에서 고른 것과 같다.</summary>
    internal void SelectTransition(string? transition) => _transitionList.SelectedItem = transition;

    private readonly Avalonia.Controls.Primitives.ToggleButton _layoutToggle = new() { Content = "배치", FontSize = 10, Padding = new Avalonia.Thickness(6, 2) };

    /// <summary>테스트 손잡이 — "배치" 토글.</summary>
    internal bool LayoutModeChecked
    {
        get => _layoutToggle.IsChecked == true;
        set => _layoutToggle.IsChecked = value;
    }

    /// <summary>테스트 손잡이 — 연 전환, 고른 층, 목록의 전환들, 층 칸이 열려 있는가.</summary>
    internal (string? Transition, string? Layer, IReadOnlyList<string> Transitions, bool Editable) TransitionEditorState
        => (SelectedTransition, _layerSelected, (_transitionList.ItemsSource as IEnumerable<string> ?? Array.Empty<string>()).ToList(), _layerPanel.IsVisible && _layerFields.IsEnabled);

    /// <summary>테스트 손잡이 — 층 칸들.</summary>
    internal (TextBox Id, ComboBox Parent, TextBox X, TextBox Y, TextBox W, TextBox H, TextBox Image, CheckBox Alpha) LayerBoxes
        => (_layerId, _layerParent, _layerX, _layerY, _layerW, _layerH, _layerImage, _layerAlpha);

    /// <summary>테스트 손잡이 — 가만히 선 자세의 회전·배율 칸.</summary>
    internal (TextBox Rotation, TextBox ScaleX, TextBox ScaleY) LayerPoseBoxes => (_layerRotation, _layerScaleX, _layerScaleY);

    /// <summary>테스트 손잡이 — 색 칸과 "색 사각형 더하기".</summary>
    internal TextBox LayerColorBox => _layerColor;

    internal string? AddColorLayerFromForm() => AddColorLayer(EditedLayer?.Id ?? "");

    /// <summary>테스트 손잡이 — 그림 파일을 그 자리(루트 공간)에 그림 층으로.</summary>
    internal string? AddImageObjectFromForm(string path, ScreenTransition.Core.Vec2 at) => AddImageObject(path, at);

    /// <summary>테스트 손잡이 — 하이어아키에서 전환의 층을 고른 것과 같다.</summary>
    internal void SelectLayer(string? id)
    {
        if (id is null)
        {
            _layerSelected = null;
            RefreshLayerPanel();
        }
        else
        {
            SelectInHierarchy(id);
        }
    }

    internal string NewTransitionFromForm() => NewTransition();

    /// <summary>테스트 손잡이 — 전환 이름 칸.</summary>
    internal TextBox SetNameBox => _transitionName;

    /// <summary>테스트 손잡이 — 하이어아키 메뉴의 "화면 밖에서 들어온다 / 밖으로 나간다"(지금 시각부터 0.5초).</summary>
    internal float? SlideLayerFromMenu(string node, StageSide side, bool leaving) => SlideLayer(node, side, leaving);

    /// <summary>들어오고 나가는 데 걸리는 시간(초) — 메뉴의 기본. 키를 끌어 늘리거나 줄인다.</summary>
    private const float SlideSeconds = 0.5f;

    private float? SlideLayer(string node, StageSide side, bool leaving)
    {
        if (_graph.Motion is not { } motion)
        {
            return null;
        }

        float start = (float)Math.Clamp(_graph.CurrentTime, 0, Math.Max(0, motion.Duration - 0.05));
        float distance = _session.Editor.SlideFromOutside(motion.Name, node, side, start, SlideSeconds, ScreenTransition.Authoring.Preview.StageTree.Stage, leaving);
        _session.SetStatus($"{node}: {start:0.##}초부터 {SlideSeconds:0.##}초 동안 무대 {SideName(side)} {(leaving ? "밖으로 나간다" : "밖에서 들어온다")}({Math.Abs(distance):0}px).");
        return distance;
    }

    private static string SideName(StageSide side) => side switch { StageSide.Left => "왼쪽", StageSide.Right => "오른쪽", StageSide.Top => "위", _ => "아래" };

    internal string? AddLayerFromForm() => AddLayerAt();

    internal void RemoveLayerFromForm() => RemoveSelectedLayer();

    internal void MoveLayerFromForm(int direction) => MoveSelectedLayer(direction);

    private TransitionAsset? EditedTransition => _session.Editor.Document.FindTransition(SelectedTransition);

    private LayerAsset? EditedLayer => _layerSelected is { } id ? EditedTransition?.Find(id) : null;

    private readonly TextBox _transitionName = new() { FontSize = 11, PlaceholderText = "(전환을 연다)" };

    private void BuildTransitionLeft()
    {
        // 전환이 맨 위다 — 무엇을 열어 작업하는지가 먼저다(그 아래의 하이어아키·타임라인이 그 전환의 것이 된다).
        LeftPanel.Children.Insert(0, Header("전환"));
        _transitionList.SelectionChanged += (_, _) => OnTransitionSelected();
        ToolTip.SetTip(_transitionList, "전환 — 층들의 계층 + 그림 + 타임라인 하나의 한 덩어리. 고르면 뷰포트와 하이어아키가 그 전환만 보인다");
        LeftPanel.Children.Insert(1, _transitionList);

        Button add = SmallButton("새 전환", (_, _) => Guard("새 전환", () => NewTransition()));
        Button remove = SmallButton("전환 삭제", (_, _) => Guard("전환 삭제", RemoveSelectedTransition));
        Button close = SmallButton("닫기", (_, _) => SelectTransition(null));
        Button import = SmallButton("그림 가져오기…", async (_, _) =>
        {
            try
            {
                if (SelectedTransition is not null && await PickOpenAsync("그림 PNG", new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } }) is { } path)
                {
                    Guard("그림 가져오기", () => AddImageObject(path, new ScreenTransition.Core.Vec2(0f, 0f)));
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
                if (SelectedTransition is not null && await PickOpenAsync("층 표 (layers.tsv)", new FilePickerFileType("층 표") { Patterns = new[] { "*.tsv" } }) is { } path)
                {
                    Guard("층 표 가져오기", () => ImportLayerSheet(path));
                }
            }
            catch (Exception exception)
            {
                _session.SetStatus($"층 표 가져오기 실패: {exception.Message}");
            }
        });
        ToolTip.SetTip(importLayers, "그림 한 묶음을 한 번에 — 층 표(layers.tsv: 파일 ⇥ 왼쪽 ⇥ 위 ⇥ 너비 ⇥ 높이, 뒤에서 앞으로)를 읽어 층마다 그림 층을 그 자리에 세운다. 표의 설명 줄에 #RRGGBB가 있으면 맨 뒤에 그 색의 바탕이 선다. 되돌리기 한 번");
        ToolTip.SetTip(import, "PNG를 연 전환의 그림 층으로 — 이름은 파일 이름에서, 크기는 그림의 크기. 되돌리기 한 번. 뷰포트에 파일을 끌어다 놓아도 된다");
        ToolTip.SetTip(add, "빈 전환(층 Body 하나)을 만들어 연다 — 이름 transition, transition_2…. 하이어아키에서 우클릭해 층을 더한다");
        ToolTip.SetTip(remove, "연 전환을 지운다 — 그 타임라인도 함께(되돌리기 한 번)");
        ToolTip.SetTip(close, "전환을 닫는다");
        ToolTip.SetTip(_layoutToggle, "배치 모드 — 켜면 무대가 움직임 없이 가만히 선 자세를 보이고, 끌기가 그 자세를 바꾼다(자리 · Shift 배율 · Ctrl 회전). 끄면 끌기가 타임라인의 키가 된다");
        _layoutToggle.IsCheckedChanged += (_, _) => _viewport.LayoutMode = _layoutToggle.IsChecked == true;
        LeftPanel.Children.Insert(2, new WrapPanel { Orientation = Orientation.Horizontal, Children = { add, remove, close, _layoutToggle, import, importLayers } });
        LeftPanel.Children.Insert(3, Labeled("전환 이름", _transitionName));
        ToolTip.SetTip(_transitionName, "연 전환의 이름 — 고치고 Enter(되돌리기 한 번). 타임라인의 이름도 이것이다. 영문 소문자로 시작하는 소문자·숫자·_");
        OnCommit(_transitionName, text =>
        {
            if (!_refreshing && SelectedTransition is { } transition && text.Trim() != transition)
            {
                Guard("전환 이름", () =>
                {
                    _session.Editor.RenameTransition(transition, text);
                    RefreshTransitions();
                    _transitionList.SelectedItem = text.Trim();
                });
                _transitionName.Text = SelectedTransition ?? "";
            }
        });
    }

    /// <summary>전환 탭에 서는 칸 — 전환을 열고 그 층을 골랐을 때의 이름·부모·자리·크기.</summary>
    private Control BuildLayerPanel()
    {
        _layerPanel.Children.Add(Header("전환의 층"));
        _layerPanel.Children.Add(_layerTitle);
        _layerFields.Children.Add(Labeled("이름", _layerId));
        _layerFields.Children.Add(Labeled("부모", _layerParent));
        _layerFields.Children.Add(Labeled("X", _layerX));
        _layerFields.Children.Add(Labeled("Y", _layerY));
        _layerFields.Children.Add(Labeled("너비", _layerW));
        _layerFields.Children.Add(Labeled("높이", _layerH));
        _layerFields.Children.Add(Labeled("회전", _layerRotation));
        _layerFields.Children.Add(Labeled("배율 X", _layerScaleX));
        _layerFields.Children.Add(Labeled("배율 Y", _layerScaleY));
        // 색 — 글로 적거나, 옆의 견본을 눌러 고른다(이 문서에서 쓴 색이 먼저, 그 뒤에 기본 견본).
        var colorRow = new DockPanel();
        DockPanel.SetDock(_layerSwatch, Dock.Right);
        colorRow.Children.Add(_layerSwatch);
        colorRow.Children.Add(_layerColor);
        _layerFields.Children.Add(Labeled("색", colorRow));
        ToolTip.SetTip(_layerSwatch, "색 고르기 — 이 문서에서 쓴 색과 기본 견본에서 고른다. 고르면 바로 들어간다(되돌리기 한 번). 정확한 값은 왼쪽 칸에 #RRGGBB로 적는다");
        _layerSwatch.Click += (_, _) => OpenColorSwatches();
        ToolTip.SetTip(_layerColor, "색 — #RRGGBB 또는 #RRGGBBAA. 적으면 단색 사각형이 된다(그림 없이 너비 × 높이를 이 색으로 채운다 — 화면 전환·소개의 패널). 비우면 색 층이 아니다");
        _layerFields.Children.Add(Labeled("그림", _layerImage));
        ToolTip.SetTip(_layerImage, "그림 — 문서 기준 상대 경로. 사각형 안에 종횡비로 맞춰 그린다. 비우면 그림 층이 아니다. 새 그림은 \"그림 가져오기…\"로");
        _layerFields.Children.Add(_layerAlpha);
        _layerPanel.Children.Add(_layerFields);
        ToolTip.SetTip(_layerId, "층 이름(영문자로 시작하는 영문·숫자·_) — 바꾸면 그 전환의 타임라인 채널이 따라온다");
        ToolTip.SetTip(_layerParent, "부모 — 밑의 층도 함께 옮겨진다");
        ToolTip.SetTip(_layerX, "부모 가운데에서의 자리(px) — 가만히 선 자세. 움직임은 여기에 얹힌다");
        ToolTip.SetTip(_layerW, "사각형의 크기(px) — 그림이면 그림이 이 안에 종횡비로 맞춰진다. 묶기만 하는 층은 0");

        OnCommit(_layerId, text =>
        {
            if (!_refreshing && SelectedTransition is { } transition && EditedLayer is { } node && text.Trim() != node.Id)
            {
                Guard("층 이름", () =>
                {
                    _session.Editor.RenameLayer(transition, node.Id, text);
                    _layerSelected = text.Trim();
                    _viewport.SelectedNode = _layerSelected;
                    RefreshLayerPanel();
                });
            }
        });
        _layerParent.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && SelectedTransition is { } transition && EditedLayer is { } node && _layerParent.SelectedItem is string chosen)
            {
                Guard("층 부모", () => _session.Editor.SetLayerParent(transition, node.Id, chosen == RootParentChoice ? null : chosen));
            }
        };

        ToolTip.SetTip(_layerRotation, "가만히 선 자세의 회전(도, 반시계 +) — 뷰포트에서 Ctrl+끌기");
        ToolTip.SetTip(_layerScaleX, "가만히 선 자세의 배율 — 뷰포트에서 Shift+끌기(둘 다 같이). 음수면 뒤집힌다");

        // 뷰포트에 PNG를 끌어다 놓으면 그 자리에 그림 층이 선다(전환을 열었을 때).
        DragDrop.SetAllowDrop(_viewport, true);
        _viewport.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (SelectedTransition is null)
            {
                return;
            }

            ScreenTransition.Core.Vec2 at = _viewport.ToRoot(e.GetPosition(_viewport));

            foreach (string path in (e.DataTransfer.TryGetFiles() ?? Array.Empty<IStorageItem>())
                         .Select(f => f.TryGetLocalPath()).OfType<string>().Where(p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
            {
                string captured = path;
                Guard("그림 놓기", () => AddImageObject(captured, at));
                at = new ScreenTransition.Core.Vec2(at.X + 24f, at.Y - 24f); // 여럿이면 조금씩 비켜 놓는다
            }

            e.Handled = true;
        });

        // 뷰포트에서 집은 층이 하이어아키와 칸에서도 골라진다.
        _viewport.LayoutLayerPicked += (_, id) =>
        {
            if (_layerSelected != id)
            {
                _layerSelected = id;
                RefreshLayerPanel();
                RebuildHierarchy();
            }
        };

        foreach (TextBox box in new[] { _layerX, _layerY, _layerW, _layerH, _layerRotation, _layerScaleX, _layerScaleY, _layerColor, _layerImage })
        {
            OnCommit(box, _ => CommitLayer());
        }

        _layerAlpha.IsCheckedChanged += (_, _) => CommitLayer();
        return _layerPanel;
    }

    private void OnTransitionSelected()
    {
        if (_refreshing)
        {
            return;
        }

        ApplyOpenTransition();
    }

    /// <summary>목록의 선택을 뷰포트에 — 그 전환을 무대에 세우고 그 타임라인을 묶는다. 하이어아키와 칸을 다시 세운다.</summary>
    private void ApplyOpenTransition()
    {
        if (!_transitionName.IsFocused)
        {
            _transitionName.Text = SelectedTransition ?? "";
        }

        _transitionName.IsEnabled = SelectedTransition is not null;

        if (_viewport.OpenTransition == SelectedTransition && _graph.MotionName == SelectedTransition)
        {
            return;
        }

        _layerSelected = null;
        _viewport.OpenTransition = SelectedTransition;
        BindMotion(SelectedTransition); // 전환의 타임라인
        RefreshAssets();
        RefreshObjects();
        RefreshLayerPanel();
        RefreshMotionInspector();
        RefreshKeyInspector();

        if (SelectedTransition is not null)
        {
            ExpandLayers(true); // 전환을 열면 그 층들이 바로 보인다
        }

        _viewport.ResetView(); // 무대 전체를 보인다
    }

    private void RefreshTransitions()
    {
        _refreshDepth++;

        try
        {
            string? selected = SelectedTransition;
            List<string> kinds = _session.Editor.Document.Transitions.Select(r => r.Name).OrderBy(k => k, StringComparer.Ordinal).ToList();
            _transitionList.ItemsSource = kinds;
            _transitionList.SelectedIndex = selected is null ? -1 : kinds.IndexOf(selected); // 지워졌으면(되돌리기 등) 풀린다
        }
        finally
        {
            _refreshDepth--;
        }

        ApplyOpenTransition();
        RefreshLayerPanel();
    }

    private string NewTransition()
    {
        string transition = "transition";

        for (int i = 2; _session.Editor.Document.FindTransition(transition) is not null; i++)
        {
            transition = $"transition_{i.ToString(CultureInfo.InvariantCulture)}";
        }

        _session.Editor.AddTransition(new TransitionAsset { Name = transition, Layers = { new LayerAsset { Id = "Body", Alpha = true } } });
        RefreshTransitions();
        _transitionList.SelectedItem = transition;
        LayoutModeChecked = true; // 새 전환은 층부터 놓는다
        SelectLayer("Body");
        return transition;
    }

    /// <summary>
    /// 그림 파일을 연 전환의 그림 층으로 — 층을 만들고(이름은 파일 이름에서, 크기는 그림의 픽셀 크기), 그 자리에 놓고, 그림을 준다.
    /// 통째로 되돌리기 한 칸. 고른 층이 묶는 것이면 그 밑에, 아니면 맨 위에 선다.
    /// </summary>
    private string? AddImageObject(string path, ScreenTransition.Core.Vec2 at)
    {
        if (SelectedTransition is not { } transition || EditedTransition is not { } asset)
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

        for (int i = 2; asset.Find(id) is not null; i++)
        {
            id = $"{stem}_{i.ToString(CultureInfo.InvariantCulture)}";
        }

        float width = MotionEditor.DefaultImageSize;
        float height = MotionEditor.DefaultImageSize;

        if (PngSizeOf(path) is { } size)
        {
            width = size.Width;
            height = size.Height;
        }

        string under = EditedLayer is { Image: null } group ? group.Id : "";
        at = _viewport.ToTransitionLocal(under, at); // 놓은 자리(루트 공간)를 그 부모 안의 좌표로
        MotionEditor editor = _session.Editor;
        string image = _session.RelativeToDocument(path);

        editor.Batch(MotionChangeKind.Document, () =>
        {
            editor.AddLayer(transition, id, under);
            editor.UpdateLayer(transition, id, new LayerAsset { X = MathF.Round(at.X), Y = MathF.Round(at.Y), Width = width, Height = height, Image = image, Alpha = true });
        });

        SelectLayer(id);
        return id;
    }

    /// <summary>테스트 손잡이 — "층 표 가져오기…"에서 파일을 고른 것과 같다.</summary>
    internal IReadOnlyList<string> ImportLayerSheetFromForm(string path) => ImportLayerSheet(path);

    /// <summary>층 표를 연 전환으로 들인다. 들인 뒤 보기를 전환에 맞춘다.</summary>
    private IReadOnlyList<string> ImportLayerSheet(string path)
    {
        if (SelectedTransition is not { } transition)
        {
            return Array.Empty<string>();
        }

        IReadOnlyList<string> created = _session.ImportLayers(transition, path);
        RefreshAssets();
        Avalonia.Threading.Dispatcher.UIThread.Post(_viewport.FitToTransition);
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

    private void RemoveSelectedTransition()
    {
        if (SelectedTransition is { } transition)
        {
            _session.Editor.RemoveTransition(transition);
        }
    }

    private void CommitLayer()
    {
        if (_refreshing || SelectedTransition is not { } transition || EditedLayer is not { } node)
        {
            return;
        }

        var value = new LayerAsset
        {
            X = TryFloat(_layerX.Text ?? "", out float x) ? x : node.X,
            Y = TryFloat(_layerY.Text ?? "", out float y) ? y : node.Y,
            Width = TryFloat(_layerW.Text ?? "", out float w) ? w : node.Width,
            Height = TryFloat(_layerH.Text ?? "", out float h) ? h : node.Height,
            Rotation = TryFloat(_layerRotation.Text ?? "", out float rotation) ? rotation : node.Rotation,
            ScaleX = TryFloat(_layerScaleX.Text ?? "", out float sx) ? sx : node.ScaleX,
            ScaleY = TryFloat(_layerScaleY.Text ?? "", out float sy) ? sy : node.ScaleY,
            Color = _layerColor.Text,
            Image = _layerImage.Text,
            Alpha = _layerAlpha.IsChecked == true,
        };
        Guard("층 값", () => _session.Editor.UpdateLayer(transition, node.Id, value));
    }

    /// <summary>층(묶음)을 더한다 — 고른 층 밑에(없으면 맨 위에). 이름은 Node1…. 그림 층은 "그림 가져오기…"로.</summary>
    private string? AddLayerAt(string? under = null)
    {
        if (SelectedTransition is not { } transition || EditedTransition is not { } asset)
        {
            return null;
        }

        string id = "Node1";

        for (int i = 2; asset.Find(id) is not null; i++)
        {
            id = "Node" + i.ToString(CultureInfo.InvariantCulture);
        }

        _session.Editor.AddLayer(transition, id, under ?? EditedLayer?.Id);
        SelectLayer(id);
        return id;
    }

    /// <summary>색 사각형을 더한다 — 층을 만들고 크기와 색을 주는 것이 한 칸. 이름은 Color1….</summary>
    private string? AddColorLayer(string under)
    {
        if (SelectedTransition is not { } transition || EditedTransition is not { } asset)
        {
            return null;
        }

        string id = "Color1";

        for (int i = 2; asset.Find(id) is not null; i++)
        {
            id = "Color" + i.ToString(CultureInfo.InvariantCulture);
        }

        MotionEditor editor = _session.Editor;
        editor.Batch(MotionChangeKind.Document, () =>
        {
            editor.AddLayer(transition, id, under);
            editor.UpdateLayer(transition, id, new LayerAsset { Width = 400f, Height = 300f, Color = "#3D5A80", Alpha = true });
        });
        SelectLayer(id);
        return id;
    }

    private void RemoveSelectedLayer()
    {
        if (SelectedTransition is { } transition && EditedLayer is { } node)
        {
            string? parent = node.Parent.Length > 0 ? node.Parent : null;
            _session.Editor.RemoveLayer(transition, node.Id);
            SelectLayer(parent);
        }
    }

    private void MoveSelectedLayer(int direction)
    {
        if (SelectedTransition is { } transition && EditedLayer is { } node)
        {
            _session.Editor.MoveLayer(transition, node.Id, direction);
        }
    }

    /// <summary>전환 보기의 하이어아키 우클릭 — 층 더하기(여기 밑에) · 지우기 · 순서 · 부모.</summary>
    private void AddLayerMenu(object? target)
    {
        string? nodeId = target is HierarchyNode { NodeId: { } id } ? id : null;
        TransitionAsset? asset = EditedTransition;

        MenuItem Item(ItemsControl into, string header, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, FontSize = 11, IsEnabled = enabled };
            item.Click += (_, _) => Guard(header, action);
            into.Items.Add(item);
            return item;
        }

        string where = nodeId is null ? "맨 위에" : $"{nodeId} 밑에";
        Item(_hierarchyMenu, $"층 더하기 — {where}", () => AddLayerAt(under: nodeId ?? ""));
        Item(_hierarchyMenu, $"색 사각형 더하기 — {where}", () => AddColorLayer(nodeId ?? ""));

        if (nodeId is null || asset?.Find(nodeId) is not { } node)
        {
            return;
        }

        void Pick(Action then)
        {
            SelectLayer(nodeId);
            then();
        }

        // 따로 보기 — 이 층만 보거나 숨긴다(보기 상태: 문서를 안 바꾼다).
        _hierarchyMenu.Items.Add(new Separator());
        bool solo = _viewport.SoloLayer == nodeId;
        bool hidden = _viewport.IsLayerHidden(nodeId);
        Item(_hierarchyMenu, solo ? "이것만 보기 풀기" : "이것만 보기 (솔로)", () => SetLayerView(nodeId, solo: !solo, hidden: null));
        Item(_hierarchyMenu, hidden ? "보이기" : "숨기기", () => SetLayerView(nodeId, solo: null, hidden: !hidden));
        Item(_hierarchyMenu, "전부 보이기", () => SetLayerView(null, null, null), enabled: _viewport.SoloLayer is not null || asset.Layers.Any(n => _viewport.IsLayerHidden(n.Id)));

        // 시차 — 타임라인에서 이 층의 움직임을 통째로 민다(키를 하나씩 옮기지 않는다).
        if (_graph.Motion is { } animation && animation.Name == asset.Name)
        {
            _hierarchyMenu.Items.Add(new Separator());
            var shift = new MenuItem { Header = $"움직임 밀기 — {animation.Name}", FontSize = 11, IsEnabled = animation.Channels.Any(c => c.Node == nodeId) };

            foreach (float delta in new[] { 0.05f, 0.1f, 0.2f, -0.05f, -0.1f, -0.2f })
            {
                float captured = delta;
                Item(shift, captured > 0 ? $"{captured:0.##}초 늦게" : $"{-captured:0.##}초 일찍", () => ShiftLayerKeys(nodeId, captured));
            }

            _hierarchyMenu.Items.Add(shift);
            var stagger = new MenuItem { Header = "여기서부터 차례로 (아래 형제들에 시차)", FontSize = 11 };

            foreach (float step in new[] { 0.05f, 0.1f, 0.2f })
            {
                float captured = step;
                Item(stagger, $"{captured:0.##}초씩", () => StaggerLayersFrom(nodeId, captured));
            }

            _hierarchyMenu.Items.Add(stagger);

            // 제 차례 전에는 숨긴다 — 가만히 선 자세는 "다 보이는" 것이라, 늦게 나오는 층을 숨겨 두는 불투명도 키를 대신 놓는다.
            // 화면 밖에서 들어오고 나간다 — 얼마나 옮겨야 화면 밖인지를 대신 센다(지금 시각부터 0.5초).
            foreach (bool leaving in new[] { false, true })
            {
                var slide = new MenuItem { Header = leaving ? "화면 밖으로 나간다 (지금 시각부터)" : "화면 밖에서 들어온다 (지금 시각부터)", FontSize = 11 };

                foreach (StageSide side in new[] { StageSide.Left, StageSide.Right, StageSide.Top, StageSide.Bottom })
                {
                    StageSide capturedSide = side;
                    bool capturedLeaving = leaving;
                    Item(slide, SideName(side) + (!leaving ? "에서" : side is StageSide.Top or StageSide.Bottom ? "로" : "으로"), () => SlideLayer(nodeId, capturedSide, capturedLeaving));
                }

                _hierarchyMenu.Items.Add(slide);
            }

            Item(_hierarchyMenu, "제 차례 전까지 숨기기", () => HideLayerForTurn(nodeId, until: true));
            Item(_hierarchyMenu, "움직임을 마치면 숨기기", () => HideLayerForTurn(nodeId, until: false));
            Item(_hierarchyMenu, "전부: 제 차례 전까지 숨기기", () => HideLayerForTurn(null, until: true));
        }

        _hierarchyMenu.Items.Add(new Separator());
        Item(_hierarchyMenu, "앞으로 (아래에 그려진다)", () => Pick(() => MoveSelectedLayer(-1)));
        Item(_hierarchyMenu, "뒤로 (위에 그려진다)", () => Pick(() => MoveSelectedLayer(+1)));

        var parents = new MenuItem { Header = "부모 바꾸기", FontSize = 11 };

        foreach (string choice in ParentChoicesOf(asset, node))
        {
            string captured = choice;
            Item(parents, choice + ((choice == RootParentChoice ? "" : choice) == node.Parent ? "  ✓" : ""), () => _session.Editor.SetLayerParent(asset.Name, node.Id, captured == RootParentChoice ? null : captured));
        }

        _hierarchyMenu.Items.Add(parents);

        _hierarchyMenu.Items.Add(new Separator());
        Item(_hierarchyMenu, "지우기 (밑의 층도)", () => Pick(RemoveSelectedLayer));
    }

    /// <summary>테스트 손잡이 — 하이어아키 메뉴의 "움직임 밀기" · "여기서부터 차례로".</summary>
    internal void ShiftLayerKeysFromMenu(string node, float delta) => ShiftLayerKeys(node, delta);

    internal void StaggerLayersFromMenu(string node, float step) => StaggerLayersFrom(node, step);

    /// <summary>테스트 손잡이 — 하이어아키 메뉴의 "제 차례 전까지 숨기기" · "움직임을 마치면 숨기기" · "전부"(node = null).</summary>
    internal void HideLayerForTurnFromMenu(string? node, bool until) => HideLayerForTurn(node, until);

    private void HideLayerForTurn(string? node, bool until)
    {
        if (_graph.Motion is not { } motion)
        {
            return;
        }

        if (node is null)
        {
            _session.SetStatus($"{motion.Name}: 층 {_session.Editor.HideAllUntilTurn(motion.Name)}개를 제 차례 전까지 숨겼다.");
            return;
        }

        if (EditedTransition?.Find(node) is { Alpha: false })
        {
            _session.SetStatus($"{node}: 불투명도를 받지 않는 층이다 — \"불투명도를 받는다\"를 켠다.");
            return;
        }

        bool done = until ? _session.Editor.HideUntilTurn(motion.Name, node) : _session.Editor.HideAfterTurn(motion.Name, node);

        if (!done)
        {
            _session.SetStatus(until ? $"{node}: 처음부터 움직인다(숨길 구간이 없다)." : $"{node}: 끝까지 움직인다(숨길 구간이 없다).");
        }
    }

    private void ShiftLayerKeys(string node, float delta)
    {
        if (_graph.Motion is { } motion && !_session.Editor.ShiftLayerKeys(motion.Name, node, delta))
        {
            _session.SetStatus($"{node}: {motion.Name}에 이 층의 키가 없다(밀 것이 없다).");
        }
    }

    /// <summary>그 층과 그 뒤의 형제들에 차례로 시차를 준다 — 첫째(고른 것)는 그대로, 다음은 step, 그다음은 2·step… 한 칸.</summary>
    private void StaggerLayersFrom(string node, float step)
    {
        if (_graph.Motion is not { } motion || EditedTransition is not { } asset || asset.Find(node) is not { } first)
        {
            return;
        }

        List<string> siblings = asset.Layers.Where(n => n.Parent == first.Parent).Select(n => n.Id).SkipWhile(id => id != node).ToList();
        _session.Editor.StaggerLayers(motion.Name, siblings, step);
    }

    /// <summary>테스트 손잡이 — 따로 보기(솔로 · 숨김). 셋 다 null이면 전부 보이기.</summary>
    internal void SetLayerViewFromMenu(string? node, bool? solo, bool? hidden) => SetLayerView(node, solo, hidden);

    private void SetLayerView(string? node, bool? solo, bool? hidden)
    {
        if (node is null)
        {
            _viewport.ShowAllLayers();
        }
        else
        {
            if (solo is { } s)
            {
                _viewport.SoloLayer = s ? node : null;
            }

            if (hidden is { } h)
            {
                _viewport.SetLayerHidden(node, h);
            }
        }

        RebuildHierarchy(); // 표시(◉ · 숨김)가 따라온다
    }

    /// <summary>부모로 고를 수 있는 것 — 제 식구(밑의 층)가 아닌 것.</summary>
    private static List<string> ParentChoicesOf(TransitionAsset asset, LayerAsset node)
    {
        var parents = new List<string> { RootParentChoice };
        var family = new HashSet<string>(StringComparer.Ordinal) { node.Id };

        foreach (LayerAsset n in asset.Layers)
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

    private void RefreshLayerPanel()
    {
        _refreshDepth++;

        try
        {
            TransitionAsset? asset = EditedTransition;

            if (asset?.Find(_layerSelected ?? "") is null)
            {
                _layerSelected = null;
            }

            LayerAsset? node = EditedLayer;
            _layerPanel.IsVisible = asset is not null;
            _layerFields.IsEnabled = node is not null;
            _layerTitle.Text = asset is null ? ""
                : node is null ? $"전환 {asset.Name} — 층 {asset.Layers.Count}개. 하이어아키에서 층을 고르면 여기서 고친다(우클릭: 더하기 · 지우기 · 순서 · 부모)"
                : $"전환 {asset.Name} › {node.Id}";

            void Write(TextBox box, string text)
            {
                if (!box.IsFocused)
                {
                    box.Text = text;
                }
            }

            Write(_layerId, node?.Id ?? "");
            Write(_layerX, node is null ? "" : F(node.X));
            Write(_layerY, node is null ? "" : F(node.Y));
            Write(_layerW, node is null ? "" : F(node.Width));
            Write(_layerH, node is null ? "" : F(node.Height));
            Write(_layerRotation, node is null ? "" : F(node.Rotation));
            Write(_layerScaleX, node is null ? "" : F(node.ScaleX));
            Write(_layerScaleY, node is null ? "" : F(node.ScaleY));
            Write(_layerColor, node?.Color ?? "");
            _layerSwatch.Background = node?.Color is { Length: > 0 } fill && Avalonia.Media.Color.TryParse(fill.Length == 9 ? fill[..7] : fill, out Avalonia.Media.Color shown)
                ? new Avalonia.Media.SolidColorBrush(shown)
                : Avalonia.Media.Brushes.Transparent;
            Write(_layerImage, node?.Image ?? "");
            _layerAlpha.IsChecked = node?.Alpha == true;

            List<string> parents = asset is not null && node is not null ? ParentChoicesOf(asset, node) : new List<string> { RootParentChoice };
            _layerParent.ItemsSource = parents;
            _layerParent.SelectedIndex = node is null ? -1 : node.Parent.Length == 0 ? 0 : parents.IndexOf(node.Parent);
        }
        finally
        {
            _refreshDepth--;
        }
    }

}
