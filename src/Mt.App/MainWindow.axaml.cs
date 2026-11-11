using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Ked.Presentation.Core;
using Mt.App.Services;
using Mt.App.Views;
using Mt.Authoring;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Settings;
using Mt.Authoring.Validation;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Mt.App;

/// <summary>
/// 셸 — 왼쪽 모션·채널 목록, 가운데 그래프, 오른쪽 인스펙터, 아래 상태·문제.
/// 모든 편집은 <see cref="MotionEditor"/> 명령이고, 화면은 <see cref="MotionEditor.Changed"/>를 듣고 다시 읽는다.
/// 파일은 <see cref="DocumentSession"/>을 지난다.
/// </summary>
public partial class MainWindow : Window
{

    private readonly DocumentSession _session = new();
    private readonly GraphEditorView _graph = new();
    private readonly DopeSheetView _dope = new();
    private readonly ViewportView _viewport = new();

    /// <summary>그래프와 도프시트가 같은 선택을 든다 — 한 모델, 두 뷰.</summary>
    private readonly KeySelection _selection = new();

    /// <summary>잠금·뮤트 — 그래프·도프시트·뷰포트가 같은 객체를 든다.</summary>
    private readonly CurveFlags _flags = new();
    private readonly KeyClipboardSlot _clipboard = new();
    private readonly ToolSettings _settings;
    private readonly string _settingsPath;
    private readonly DispatcherTimer _autoSaveTimer = new();
    private readonly Button _recentButton = new() { Content = "최근…", FontSize = 11, Padding = new Avalonia.Thickness(8, 3) };
    private readonly ComboBox _snapStepCombo = new() { ItemsSource = new[] { "1/24초 (1프레임)", "1/60초", "1/120초" }, SelectedIndex = 0, FontSize = 11 };
    private readonly ComboBox _autoSaveCombo = new() { ItemsSource = new[] { "끔", "1분", "5분", "10분" }, SelectedIndex = 2, FontSize = 11 };
    private static readonly double[] SnapSteps = { 1.0 / 24.0, 1.0 / 60.0, 1.0 / 120.0 }; // 차례 = _snapStepCombo 항목
    private static readonly int[] AutoSaveMinutes = { 0, 1, 5, 10 };

    // 트랜스포트
    private readonly Button _playButton = new() { Content = "▶", FontSize = 11, Padding = new Avalonia.Thickness(8, 3) };
    private readonly ComboBox _playMode = new() { ItemsSource = new[] { "한 번", "루프", "왕복" }, SelectedIndex = 1, FontSize = 11 };
    private readonly ComboBox _playSpeed = new() { ItemsSource = new[] { "×0.25", "×0.5", "×1", "×2" }, SelectedIndex = 2, FontSize = 11 };
    private readonly ComboBox _playStep = new()
    {
        ItemsSource = new[]
        {
            PlayStepItem("연속", "실제 경과 시간에 해당하는 자세를 표시합니다."),
            PlayStepItem($"프레임 맞춤 ({TimeSnap.FramesPerSecond:0} FPS)", "표시 시각을 가장 가까운 프레임에 맞춥니다. 느린 환경에서는 중간 프레임을 건너뛸 수 있습니다."),
        },
        SelectedIndex = 0,
        FontSize = 11,
    };
    private readonly TextBox _rangeStart = new() { Text = "0", Width = 56, FontSize = 11 };
    private readonly TextBox _rangeEnd = new() { Text = "1", Width = 56, FontSize = 11 };
    private readonly TextBox _animEnd = new() { Width = 56, FontSize = 11 };
    private readonly TimeSliderView _timeSlider = new();
    private readonly RangeSliderView _rangeSlider = new();
    private readonly ToggleButton _autoKeyToggle = new() { Content = "● Auto Key", FontSize = 10, Padding = new Avalonia.Thickness(6, 2) };
    private readonly ToggleButton _stageOnlyToggle = new() { Content = "무대만", FontSize = 10, Padding = new Avalonia.Thickness(6, 2) };

    /// <summary>테스트 손잡이 — "무대만" 토글.</summary>
    internal bool StageOnlyChecked
    {
        get => _stageOnlyToggle.IsChecked == true;
        set => _stageOnlyToggle.IsChecked = value;
    }
    private static readonly IBrush AutoKeyOnBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0x3C, 0x3C));
    private bool _syncingTime;

    // 채널 박스(마야) — 선택 노드의 Translate/Rotate/Scale. 칸에 값을 넣으면 매니퓰레이터를 놓은 것과 같다.
    private readonly TextBlock _channelBoxTitle = new() { FontSize = 11, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Avalonia.Thickness(0, 2) };
    private readonly StackPanel _channelBoxPanel = new() { Spacing = 1 };
    private readonly Dictionary<(MotionChannelKind Kind, Axis Axis), (Border Row, TextBox Box)> _channelBoxRows = new();
    private static readonly (MotionChannelKind Kind, Axis Axis, string Label)[] ChannelBoxComponents =
    {
        (MotionChannelKind.Position, Axis.X, "Translate X"),
        (MotionChannelKind.Position, Axis.Y, "Translate Y"),
        (MotionChannelKind.Euler, Axis.X, "Rotate X"),
        (MotionChannelKind.Euler, Axis.Y, "Rotate Y"),
        (MotionChannelKind.Euler, Axis.Z, "Rotate Z"),
        (MotionChannelKind.Scale, Axis.X, "Scale X"),
        (MotionChannelKind.Scale, Axis.Y, "Scale Y"),
    };
    private static readonly IBrush ChannelBoxKeyedHere = new SolidColorBrush(Color.FromArgb(150, 0xC8, 0x3C, 0x3C)); // 이 시각에 키(마야 빨강)
    private static readonly IBrush ChannelBoxKeyed = new SolidColorBrush(Color.FromArgb(110, 0xB4, 0x5A, 0x5A)); // 어딘가에 키(마야 연한 빨강)
    private static readonly IBrush ChannelBoxPending = new SolidColorBrush(Color.FromArgb(120, 0xD0, 0x8A, 0x28)); // 키 없는 포즈
    /// <summary>선택 키의 노드 따라가기 — 하이어아키·아웃라이너에서 고르면 꺼지고, 모션을 바꾸면 켜진다(설정 아님).</summary>
    private bool _followSelection = true;
    private readonly TabControl _inspectorTabs = new() { Padding = new Avalonia.Thickness(0) };
    private Window? _settingsWindow;
    private Window? _shortcutsWindow;
    private readonly List<Panel> _settingsRows = new();
    private readonly TextBlock _pendingText = new() { FontSize = 11, Foreground = Brushes.Orange, TextWrapping = TextWrapping.Wrap };
    private Button? _regionButton;
    private Button? _bufferButton;
    private readonly CheckBox _valueSnap = new() { Content = "값 스냅", FontSize = 11 };
    private static readonly string[] InfinityTokens = { "상수", "사이클", "사이클+오프셋", "왕복", "직선" };
    private readonly ComboBox _preInfinity = new() { ItemsSource = InfinityTokens, SelectedIndex = 0, FontSize = 11 };
    private readonly ComboBox _postInfinity = new() { ItemsSource = InfinityTokens, SelectedIndex = 0, FontSize = 11 };
    private static readonly TangentMode[] NewKeyTangentChoices = { TangentMode.Auto, TangentMode.Spline, TangentMode.Plateau, TangentMode.Flat, TangentMode.Linear };
    private readonly ComboBox _newKeyTangent = new() { ItemsSource = new[] { "auto", "spline", "plateau", "flat", "linear" }, SelectedIndex = 0, FontSize = 11 };

    /// <summary>새 키(뷰포트 키·성분 토글)의 탄젠트 — 설정. 곡선 위에 끼우는 I·더블클릭은 모양을 지키는 free다.</summary>
    private TangentMode DefaultTangent => NewKeyTangentChoices[Math.Max(0, _newKeyTangent.SelectedIndex)];

    // 왼쪽 — 세트·모션
    // 높이 제한 없음 — 왼쪽 패널이 스크롤한다. 오브젝트가 없는 가지는 기본 접힘이라 길지 않다.
    private readonly TreeView _hierarchy = new() { MinHeight = 160, FontSize = 11 };

    /// <summary>펼침 상태 기억 — 기본이 펼침인 항목(폴더·노드)의 접힘, 기본이 접힘인 항목(오브젝트)의 펼침.</summary>
    private readonly HashSet<string> _collapsedHierarchy = new();
    private readonly HashSet<string> _expandedHierarchy = new();
    private readonly Dictionary<TreeViewItem, TreeViewItem?> _hierarchyParents = new();
    private readonly ContextMenu _hierarchyMenu = new();
    private object? _menuTarget;
    private static readonly IBrush ActingNodeBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xE0, 0xFF));

    // 오른쪽 — 오브젝트(채널 박스)




    // 왼쪽
    private readonly StackPanel _channelRows = new() { Spacing = 0 };
    private Grid? _centerGrid;
    private readonly TextBox _outlinerSearch = new() { PlaceholderText = "검색…", FontSize = 11, Margin = new Avalonia.Thickness(2) };
    private static readonly IBrush OutlinerBackground = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B));
    private static readonly IBrush OutlinerRowSelected = new SolidColorBrush(Color.FromArgb(170, 0x4B, 0x6E, 0xAF));
    private readonly ComboBox _timeDisplayCombo = new() { ItemsSource = new[] { "프레임 (24fps)", "초" }, SelectedIndex = 0, FontSize = 11 };

    /// <summary>시간 글자 단위(설정) — 마야처럼 프레임이 기본.</summary>
    private TimeDisplay Display => _settings.TimeDisplay;

    // 오른쪽 — 모션
    private readonly TextBox _motionDuration = new() { FontSize = 11 };

    // 오른쪽 — 키
    private readonly TextBox _keyValue = new() { FontSize = 11 };

    // 오른쪽 — 보기
    private readonly CheckBox _snap = new() { Content = "시간 스냅", IsChecked = true, FontSize = 11 };
    private readonly TextBox _currentTime = new() { Text = "0", FontSize = 11, Width = 72 };

    private int _refreshDepth;

    private bool _refreshing => _refreshDepth > 0;

    public MainWindow() : this(ToolSettings.DefaultPath())
    {
    }

    /// <param name="settingsPath">설정 파일 자리 — 테스트가 임시 경로를 준다.</param>
    internal MainWindow(string settingsPath)
    {
        _settingsPath = settingsPath;
        _settings = ToolSettings.Load(settingsPath);
        InitializeComponent();
        BuildToolbar();
        BuildLeft();
        BuildRight();
        _graph.Selection = _selection;
        _dope.Selection = _selection;
        _viewport.Selection = _selection;
        _graph.Flags = _flags;
        _dope.Flags = _flags;
        _viewport.Flags = _flags;
        _graph.Clipboard = _clipboard;
        _dope.Clipboard = _clipboard;
        _timeSlider.Viewport = _viewport;
        _timeSlider.Selection = _selection;
        _timeSlider.Clipboard = _clipboard;
        _rangeSlider.Viewport = _viewport;
        _flags.Changed += (_, _) => RefreshChannels();
        GraphHost.Child = BuildCenter();
        ApplyLayout();

        _session.DocumentReplaced += (_, _) => OnDocumentReplaced();
        _session.StatusChanged += (_, _) => StatusText.Text = _session.Status;
        _session.AssetsChanged += (_, _) => RefreshAssets();
        _selection.Changed += (_, _) =>
        {
            RefreshKeyInspector();
            RefreshSelectedNode();
        };
        _graph.CurrentTimeChanged += (_, _) =>
        {
            _currentTime.Text = TimeText.Format(_graph.CurrentTime, Display);
            SyncTime(() =>
            {
                _viewport.ScrubTo(_graph.CurrentTime);
                _dope.CurrentTime = _graph.CurrentTime;
            });
        };
        _dope.CurrentTimeChanged += (_, _) => SyncTime(() =>
        {
            _graph.CurrentTime = _dope.CurrentTime;
            _viewport.ScrubTo(_dope.CurrentTime);
        });
        _graph.CurvesChanged += (_, _) =>
        {
            RefreshChannels();
            RefreshSelectedNode();
        };
        _viewport.TimeChanged += (_, _) =>
        {
            SyncTime(() =>
            {
                _graph.CurrentTime = _viewport.Time;
                _dope.CurrentTime = _viewport.Time;
            });
            RefreshChannelBox();
        };
        _viewport.PlayStateChanged += (_, _) => _playButton.Content = _viewport.IsPlaying ? "❚❚" : "▶";

        OnDocumentReplaced();
        ApplySettings();

        if (_settings.LoadProblem is { } problem)
        {
            _session.SetStatus(problem);
        }

        _autoSaveTimer.Tick += (_, _) => Guard("자동 저장", () => _session.AutoSave());
        Closing += (_, _) => SaveSettings();
        AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Tunnel);
    }

    // ── 전역 단축키 (마야 타임 슬라이더 관습) ───────────────────────────
    //
    // 1 / 2 이전·다음 키 · Alt+1 / Alt+2 이전·다음 프레임 · ` 첫 프레임 · Alt+` 처음부터 재생 · Alt+Q 재생 토글
    // Shift+W / E / F 현재 시각의 키 복사·붙여넣기·지우기 · Shift+1 / 2 인비트윈 빼기·넣기
    // 창 어디에 포커스가 있든 듣는다(터널). 글 입력 칸에서는 안 듣는다.

    private void OnShellKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox || e.Handled)
        {
            return;
        }

        KeyModifiers mods = e.KeyModifiers;
        bool none = mods == KeyModifiers.None;
        bool alt = mods == KeyModifiers.Alt;
        bool shift = mods == KeyModifiers.Shift;

        switch (e.PhysicalKey)
        {
            case PhysicalKey.Digit1 when none:
                PreviousKey();
                break;
            case PhysicalKey.Digit2 when none:
                NextKey();
                break;
            case PhysicalKey.Digit1 when alt:
                StepFrame(-1);
                break;
            case PhysicalKey.Digit2 when alt:
                StepFrame(+1);
                break;
            case PhysicalKey.Q when alt:
                _viewport.TogglePlay();
                break;
            case PhysicalKey.F1 when none:
                OpenShortcuts();
                break;
            case PhysicalKey.Backquote when alt:
                _viewport.PlayFromStart();
                break;
            case PhysicalKey.Backquote when none:
                _viewport.Stop();
                break;
            case PhysicalKey.W when shift:
                Guard("키 복사", CopyKeysAtCurrentTime);
                break;
            case PhysicalKey.E when shift:
                Guard("키 붙여넣기", PasteKeysAtCurrentTime);
                break;
            case PhysicalKey.F when shift:
                Guard("키 지우기", ClearKeysAtCurrentTime);
                break;
            case PhysicalKey.Digit1 when shift:
                Guard("인비트윈 빼기", RemoveInbetween);
                break;
            case PhysicalKey.Digit2 when shift:
                Guard("인비트윈 넣기", AddInbetween);
                break;
            // 되돌리기·다시하기 — 창 어디에 포커스가 있든 한 경로(툴바 버튼도 같다). 글 입력 칸은 위에서 빠졌으니 거기서는 글자 되돌리기다.
            case PhysicalKey.Z when mods == (KeyModifiers.Control | KeyModifiers.Shift):
            case PhysicalKey.Y when mods == KeyModifiers.Control:
                RedoCommand();
                break;
            case PhysicalKey.Z when mods == KeyModifiers.Control:
                UndoCommand();
                break;
            // 키 복사·잘라내기·붙여넣기 — 창 어디에 포커스가 있든(그래프·도프 시트의 선택 키). 붙이는 커브는 그래프가 정한다(고른 커브 › 보이는 커브 › 원래 커브).
            case PhysicalKey.C when mods == KeyModifiers.Control:
                _graph.CopySelection();
                break;
            case PhysicalKey.X when mods == KeyModifiers.Control:
                _graph.CutSelection();
                break;
            case PhysicalKey.V when mods == KeyModifiers.Control:
                _graph.Paste();
                break;
            // 매니퓰레이터(마야): Q 선택 · W 이동 · E 회전 · R 배율 — 창 어디에 포커스가 있든. 그래프 안에서는 그래프 도구(Q/W/R)가 먼저다.
            case PhysicalKey.Q when none && !InsideGraph(e.Source):
                SetManipulator(ManipulatorMode.Select);
                break;
            // S = Set Key(마야): 키 없는 포즈가 있으면 그것을, 없으면 고른 노드의 지금 값을 현재 시각에.
            case PhysicalKey.S when none:
                SetKeySelected();
                break;
            case PhysicalKey.W when none && !InsideGraph(e.Source):
                SetManipulator(ManipulatorMode.Move);
                break;
            case PhysicalKey.E when none:
                SetManipulator(ManipulatorMode.Rotate);
                break;
            case PhysicalKey.R when none && !InsideGraph(e.Source):
                SetManipulator(ManipulatorMode.Scale);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private bool InsideGraph(object? source)
        => source is Visual v && (ReferenceEquals(v, _graph) || v.FindAncestorOfType<GraphEditorView>() is not null);

    /// <summary>되돌리기 — Ctrl+Z와 툴바의 한 경로. 뷰포트가 포즈를 든 채 스크럽 중이면 그것부터 놓는다.</summary>
    private void UndoCommand()
    {
        _viewport.EndHoldScrub();
        Guard("되돌리기", _session.Editor.Undo);
    }

    private void RedoCommand()
    {
        _viewport.EndHoldScrub();
        Guard("다시하기", _session.Editor.Redo);
    }

    /// <summary>테스트 손잡이 — 하이어아키 트리(포커스를 두고 단축키를 보내는 데).</summary>
    internal Control Hierarchy => _hierarchy;

    /// <summary>테스트 손잡이 — 그래프 툴바의 값 칸.</summary>
    internal TextBox KeyValueBox => _keyValue;

    /// <summary>테스트 손잡이 — 하단 바의 범위 시작 칸(항상 보인다 — 글 입력 중 단축키 경계 검사).</summary>
    internal TextBox RangeStartBox => _rangeStart;

    private void SetManipulator(ManipulatorMode mode)
    {
        _viewport.Manipulator = mode;
        _viewport.InvalidateVisual();
    }

    /// <summary>테스트 손잡이.</summary>
    internal ManipulatorMode Manipulator => _viewport.Manipulator;

    /// <summary>한 프레임(24fps) — 뷰포트 ←/→와 같은 규약.</summary>
    internal static float FrameSeconds => (float)(1.0 / TimeSnap.FramesPerSecond);

    /// <summary>재생 표시 콤보의 항목 — 항목마다 툴팁. 차례 = 연속(0) · 프레임 맞춤(1) = ToolSettings.PlayWholeFrames.</summary>
    private static ComboBoxItem PlayStepItem(string text, string tip)
    {
        var item = new ComboBoxItem { Content = text, FontSize = 11 };
        ToolTip.SetTip(item, tip);
        return item;
    }

    internal void PreviousKey() => _viewport.JumpToPreviousKey();

    internal void NextKey() => _viewport.JumpToNextKey();

    internal void StepFrame(int frames)
    {
        _viewport.Pause();
        _viewport.ScrubTo(_viewport.Time + frames * (double)FrameSeconds);
    }

    /// <summary>마야 timeSliderCopyKey — 현재 시각의 키 전부(모든 커브)를 클립보드에.</summary>
    internal void CopyKeysAtCurrentTime()
    {
        if (_graph.Motion is not { } motion)
        {
            return;
        }

        List<KeyRef> keys = TimeSliderOps.KeysAt(motion, _viewport.Time);
        KeyClipboard? clipboard = KeyClipboard.Copy(motion, keys);

        if (clipboard is null || clipboard.Count == 0)
        {
            _session.SetStatus("현재 시각에 키가 없다.");
            return;
        }

        _clipboard.Current = clipboard;
        _session.SetStatus($"키 {clipboard.Count}개 복사 ({_viewport.Time:0.###}s).");
    }

    /// <summary>마야 timeSliderPasteKey — 클립보드를 현재 시각에(같은 시각은 덮어씀, 없는 채널은 만든다).</summary>
    internal void PasteKeysAtCurrentTime()
    {
        if (_graph.MotionName is not { } name)
        {
            return;
        }

        if (_clipboard.Current is not { } clipboard || clipboard.Count == 0)
        {
            _session.SetStatus("클립보드에 키가 없다.");
            return;
        }

        float at = (float)_viewport.Time;
        Func<float, float>? snap = _settings.SnapEnabled ? t => (float)TimeSnap.Snap(t, _settings.SnapStep) : null;
        List<KeyRef> pasted = _session.Editor.PasteKeys(name, clipboard, snap?.Invoke(at) ?? at, snap);
        _selection.Set(pasted);
    }

    /// <summary>마야 timeSliderClearKey — 현재 시각의 키를 모든 커브에서 지운다.</summary>
    internal void ClearKeysAtCurrentTime()
    {
        if (_graph.MotionName is { } name && !_session.Editor.RemoveKeysAt(name, _viewport.Time))
        {
            _session.SetStatus("현재 시각에 키가 없다.");
        }
    }

    /// <summary>마야 removeInbetween — 현재 시각 뒤의 키를 한 프레임 당긴다.</summary>
    internal void RemoveInbetween()
    {
        if (_graph.MotionName is { } name && !_session.Editor.ShiftKeysAfter(name, (float)_viewport.Time, -FrameSeconds))
        {
            _session.SetStatus("현재 시각 뒤에 키가 없다.");
        }
    }

    /// <summary>마야 addInbetween — 현재 시각 뒤의 키를 한 프레임 민다.</summary>
    internal void AddInbetween()
    {
        if (_graph.MotionName is { } name && !_session.Editor.ShiftKeysAfter(name, (float)_viewport.Time, FrameSeconds))
        {
            _session.SetStatus("현재 시각 뒤에 키가 없다.");
        }
    }

    internal ToolSettings Settings => _settings;

    /// <summary>설정 → 화면·뷰. 설정 파일에서 읽은 값을 한 번에 입힌다.</summary>
    /// <summary>창 배치(패널 폭·뷰포트/그래프 비율)를 설정에서 입힌다. 스플리터가 바꾼 값은 저장할 때 읽는다.</summary>
    private void ApplyLayout()
    {
        Shell.ColumnDefinitions[0].Width = new GridLength(_settings.LeftPanelWidth);
        Shell.ColumnDefinitions[4].Width = new GridLength(_settings.RightPanelWidth);
        Shell.ColumnDefinitions[0].MinWidth = ToolSettings.MinPanelWidth;
        Shell.ColumnDefinitions[4].MinWidth = ToolSettings.MinPanelWidth;
        Shell.ColumnDefinitions[2].MinWidth = 600; // 트랜스포트·레인지 줄이 겹치지 않는 폭

        if (_centerGrid is { } center)
        {
            center.RowDefinitions[0].Height = new GridLength(_settings.ViewportRowStar, GridUnitType.Star);
            center.RowDefinitions[2].Height = new GridLength(_settings.GraphRowStar, GridUnitType.Star);
        }
    }

    private void CaptureLayout()
    {
        _settings.LeftPanelWidth = Shell.ColumnDefinitions[0].Width.Value;
        _settings.RightPanelWidth = Shell.ColumnDefinitions[4].Width.Value;

        if (_centerGrid is { } center && center.RowDefinitions[0].Height.IsStar && center.RowDefinitions[2].Height.IsStar)
        {
            _settings.ViewportRowStar = center.RowDefinitions[0].Height.Value;
            _settings.GraphRowStar = center.RowDefinitions[2].Height.Value;
        }
    }

    private void ApplySettings()
    {
        _refreshDepth++;

        try
        {
            _snap.IsChecked = _settings.SnapEnabled;
            int snapIndex = Array.FindIndex(SnapSteps, v => Math.Abs(v - _settings.SnapStep) < 1e-9);
            _snapStepCombo.SelectedIndex = snapIndex < 0 ? 0 : snapIndex;
            int tangentIndex = Array.IndexOf(NewKeyTangentChoices, _settings.DefaultTangent);
            _newKeyTangent.SelectedIndex = tangentIndex < 0 ? 0 : tangentIndex;
            _autoKeyToggle.IsChecked = _settings.AutoKey;
            _autoKeyToggle.Foreground = _settings.AutoKey ? AutoKeyOnBrush : null;
            _inspectorTabs.SelectedIndex = Math.Clamp(_settings.InspectorTab, 0, 2);
            int autoSaveIndex = Array.IndexOf(AutoSaveMinutes, _settings.AutoSaveMinutes);
            _autoSaveCombo.SelectedIndex = autoSaveIndex < 0 ? 2 : autoSaveIndex;
            _timeDisplayCombo.SelectedIndex = (int)_settings.TimeDisplay;
            _playStep.SelectedIndex = _settings.PlayWholeFrames ? 1 : 0;
        }
        finally
        {
            _refreshDepth--;
        }

        PushSettingsToViews();
        RefreshRecentMenu();
    }

    private void PushSettingsToViews()
    {
        _graph.SnapEnabled = _settings.SnapEnabled;
        _dope.SnapEnabled = _settings.SnapEnabled;
        _viewport.SnapEnabled = _settings.SnapEnabled;
        _graph.SnapStep = _settings.SnapStep;
        _dope.SnapStep = _settings.SnapStep;
        _viewport.SnapStep = _settings.SnapStep;
        _viewport.DefaultTangent = _settings.DefaultTangent;
        _viewport.AutoKey = _settings.AutoKey;
        _viewport.PlayWholeFrames = _settings.PlayWholeFrames;
        _graph.TimeDisplay = _settings.TimeDisplay;
        _dope.TimeDisplay = _settings.TimeDisplay;
        _timeSlider.SnapEnabled = _settings.SnapEnabled;
        _timeSlider.SnapStep = _settings.SnapStep;
        _timeSlider.TimeDisplay = _settings.TimeDisplay;
        _rangeSlider.TimeDisplay = _settings.TimeDisplay;
        _timeSlider.InvalidateVisual();
        _rangeSlider.InvalidateVisual();
        _graph.InvalidateVisual();
        _dope.InvalidateVisual();
        RefreshTimeTexts();
        _autoSaveTimer.Stop();

        if (_settings.AutoSaveMinutes > 0)
        {
            _autoSaveTimer.Interval = TimeSpan.FromMinutes(_settings.AutoSaveMinutes);
            _autoSaveTimer.Start();
        }

        _viewport.InvalidateVisual();
    }

    private void SaveSettings()
    {
        try
        {
            CaptureLayout();
            _settings.Save(_settingsPath);
        }
        catch (Exception exception)
        {
            _session.SetStatus($"설정을 저장하지 못했다: {exception.Message}");
        }
    }

    private void RefreshRecentMenu()
    {
        var flyout = new MenuFlyout();

        foreach (string path in _settings.RecentFiles)
        {
            var item = new MenuItem { Header = path, FontSize = 11 };
            string captured = path;
            item.Click += (_, _) => Guard("최근 파일", () => OpenDocument(captured));
            flyout.Items.Add(item);
        }

        if (_settings.RecentFiles.Count == 0)
        {
            flyout.Items.Add(new MenuItem { Header = "(없음)", IsEnabled = false, FontSize = 11 });
        }

        _recentButton.Flyout = flyout;
    }

    internal void OpenDocument(string path)
    {
        _session.Open(path);
        _settings.Touch(path);
        RefreshRecentMenu();
        SaveSettings();
    }

    internal DocumentSession Session => _session;

    internal GraphEditorView Graph => _graph;

    internal ViewportView Viewport => _viewport;

    internal DopeSheetView Dope => _dope;

    internal KeySelection SharedSelection => _selection;

    internal bool AutoKeyChecked
    {
        get => _autoKeyToggle.IsChecked == true;
        set => _autoKeyToggle.IsChecked = value;
    }


    /// <summary>테스트 손잡이 — 채널을 바로 더한다(UI에는 "채널 추가"가 없다: 채널 박스·매니퓰레이터가 첫 키에서 만든다).</summary>
    internal void AddChannelFromForm(string node, MotionChannelKind kind)
    {
        TimelineAsset motion = _graph.Motion ?? throw new InvalidOperationException("먼저 모션을 고른다.");
        _session.Editor.AddChannel(motion.Name, node, kind);
    }

    /// <summary>테스트 손잡이 — 채널 박스 칸에 값을 넣고 Enter.</summary>
    internal void SetChannelBoxValue(MotionChannelKind kind, Axis axis, string text) => CommitChannelBox(kind, axis, text);

    /// <summary>테스트 손잡이 — 채널 박스 칸의 글자.</summary>
    internal string ChannelBoxValue(MotionChannelKind kind, Axis axis) => _channelBoxRows[(kind, axis)].Box.Text ?? "";

    /// <summary>테스트 손잡이 — 채널 박스 ●(이 성분을 지금 키).</summary>
    internal void KeyChannelBoxComponent(MotionChannelKind kind, Axis axis) => KeyChannelBox(kind, axis);

    /// <summary>채널 행의 성분 토글과 같은 길 — 없는 성분을 현재 시각의 중립값 키 하나로 만든다.</summary>
    internal void EnsureComponent(string node, MotionChannelKind kind, Axis axis)
    {
        TimelineAsset motion = _graph.Motion ?? throw new InvalidOperationException("먼저 모션을 고른다.");

        if (motion.FindChannel(node, kind)?.Get(axis) is not null)
        {
            return;
        }

        _session.Editor.AddKey(motion.Name, node, kind, axis, new KeyAsset
        {
            T = (float)_graph.CurrentTime,
            V = MotionMath.Neutral(kind),
            Tangent = DefaultTangent,
        });
    }

    /// <summary>인스펙터의 값 칸에 적고 Enter 한 것과 같다.</summary>
    internal void CommitKeyValueFromInspector(string text)
    {
        _keyValue.Text = text;
        WithSingleKey((key, k) => { if (TryFloat(text, out float v) && v != k.V) UpdateKey(key, k, value: v); });
    }

    internal IReadOnlyList<MotionProblem> ProblemsShown => _session.Problems;

    // ── 구성 ──────────────────────────────────────────────────────────

    /// <summary>가운데: 위 뷰포트, 트랜스포트 줄, 아래 그래프. 사이에 스플리터.</summary>
    private Control BuildCenter()
    {
        // 위: 뷰포트 + 트랜스포트 + 레인지 줄(한 덩어리, 아래 두 줄은 늘 제 높이) · 스플리터 · 아래: 그래프/도프 시트.
        // 스플리터는 덩어리와 탭의 별 높이만 바꾼다 — 레인지 줄이 늘어나지 않는다.
        var grid = new Grid { RowDefinitions = new RowDefinitions("2*,4,3*") };
        grid.RowDefinitions[0].MinHeight = 200;
        grid.RowDefinitions[2].MinHeight = 160;
        _centerGrid = grid;

        var scene = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto") };
        Grid.SetRow(scene, 0);
        grid.Children.Add(scene);

        Grid.SetRow(_viewport, 0);
        scene.Children.Add(_viewport);

        _playButton.Click += (_, _) => _viewport.TogglePlay();
        _playMode.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _playMode.SelectedIndex >= 0)
            {
                _viewport.Mode = (PlaybackMode)_playMode.SelectedIndex;
            }
        };
        _playStep.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _playStep.SelectedIndex >= 0)
            {
                _settings.PlayWholeFrames = _playStep.SelectedIndex == 1;
                PushSettingsToViews();
            }
        };
        ToolTip.SetTip(_playStep, "재생 표시 — 둘 다 실제 시간으로 재생한다. 연속: 실제 경과 시간에 해당하는 자세 · 프레임 맞춤: 표시 시각을 가장 가까운 프레임에 맞춘다");
        _playSpeed.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _playSpeed.SelectedIndex >= 0)
            {
                _viewport.Speed = new[] { 0.25, 0.5, 1, 2 }[_playSpeed.SelectedIndex];
            }
        };
        OnCommit(_rangeStart, text => Guard("재생 범위", () => { if (TimeText.TryParse(text, Display, out double v)) _viewport.SetRange(v, _viewport.Clock.RangeEnd); }));
        OnCommit(_rangeEnd, text => Guard("재생 범위", () => { if (TimeText.TryParse(text, Display, out double v)) _viewport.SetRange(_viewport.Clock.RangeStart, v); }));
        _viewport.RangeChanged += (_, _) => RefreshTransport();
        // 마야 트랜스포트: [현재 프레임] ⏮ |◀ ◁ ◀ ▶ ▷ ▶| ⏭  — 타임 슬라이더가 나머지를 채운다.
        _playButton.Content = "▶";
        _playButton.FontSize = 10;
        _playButton.Padding = new Avalonia.Thickness(6, 2);
        ToolTip.SetTip(_playButton, "재생 / 일시정지 (Alt+Q) · 처음부터 재생 (Alt+`)");
        Button playBack = SmallButton("◀", (_, _) =>
        {
            if (_viewport.IsPlaying)
            {
                _viewport.Pause();
            }
            else
            {
                _viewport.PlayBackward();
            }
        });
        ToolTip.SetTip(playBack, "거꾸로 재생");
        OnCommit(_animEnd, text => WithMotion(m =>
        {
            if (TimeText.TryParse(text, Display, out double v) && Math.Abs(v - m.Duration) > 1e-6)
            {
                _session.Editor.SetDuration(m.Name, (float)v);
            }
        }));
        ToolTip.SetTip(_currentTime, "현재 프레임 — 숫자를 넣으면 그 시각으로(초는 0.5s처럼)");
        ToolTip.SetTip(_rangeStart, "재생 범위 시작");
        ToolTip.SetTip(_rangeEnd, "재생 범위 끝");
        ToolTip.SetTip(_animEnd, "모션 길이(애니메이션 끝) — 바꾸면 길이가 바뀐다");
        (string Label, string Tip, EventHandler<RoutedEventArgs> Click)[] transportSpec =
        {
            ("⏮", "범위 처음으로 (`)", (_, _) => _viewport.Stop()),
            ("|◀", "이전 키 (1 · ,)", (_, _) => _viewport.JumpToPreviousKey()),
            ("◁", "이전 프레임 (Alt+1 · ←)", (_, _) => StepFrame(-1)),
        };
        var transportButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Avalonia.Thickness(6, 0, 0, 0) };
        _currentTime.Width = 56;
        transportButtons.Children.Add(_currentTime);

        foreach ((string label, string tip, EventHandler<RoutedEventArgs> click) in transportSpec)
        {
            Button b = SmallButton(label, click);
            ToolTip.SetTip(b, tip);
            transportButtons.Children.Add(b);
        }

        transportButtons.Children.Add(playBack);
        transportButtons.Children.Add(_playButton);
        (string Label, string Tip, EventHandler<RoutedEventArgs> Click)[] transportTail =
        {
            ("▷", "다음 프레임 (Alt+2 · →)", (_, _) => StepFrame(+1)),
            ("▶|", "다음 키 (2 · .)", (_, _) => _viewport.JumpToNextKey()),
            ("⏭", "범위 끝으로", (_, _) => { _viewport.Pause(); _viewport.ScrubTo(_viewport.Clock.RangeEnd); }),
        };

        foreach ((string label, string tip, EventHandler<RoutedEventArgs> click) in transportTail)
        {
            Button b = SmallButton(label, click);
            ToolTip.SetTip(b, tip);
            transportButtons.Children.Add(b);
        }

        var transportRow = new DockPanel { Margin = new Avalonia.Thickness(4, 2, 4, 0) };
        DockPanel.SetDock(transportButtons, Dock.Right);
        transportRow.Children.Add(transportButtons);
        transportRow.Children.Add(_timeSlider);
        Grid.SetRow(transportRow, 1);
        scene.Children.Add(transportRow);

        // 레인지 슬라이더 줄: [0][범위 시작] ═바═ [범위 끝][길이] · 모드 · 속도 · fps · Auto Key.
        var rangeLeft = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Avalonia.Thickness(0, 0, 4, 0),
            Children = { new TextBlock { Text = "0", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, Width = 20, TextAlignment = TextAlignment.Center }, _rangeStart },
        };
        var rangeRight = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Avalonia.Thickness(4, 0, 0, 0),
            Children =
            {
                _rangeEnd,
                _animEnd,
                _playMode,
                _playSpeed,
                _playStep,
                new TextBlock { Text = "24 fps", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 },
                _autoKeyToggle,
                _stageOnlyToggle,
            },
        };
        var rangeRow = new DockPanel { Margin = new Avalonia.Thickness(4, 2, 4, 2) };
        DockPanel.SetDock(rangeLeft, Dock.Left);
        DockPanel.SetDock(rangeRight, Dock.Right);
        rangeRow.Children.Add(rangeLeft);
        rangeRow.Children.Add(rangeRight);
        rangeRow.Children.Add(_rangeSlider);
        Grid.SetRow(rangeRow, 2);
        scene.Children.Add(rangeRow);

        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, Background = Brushes.Transparent };
        Grid.SetRow(splitter, 1);
        grid.Children.Add(splitter);

        _editorTabs.ItemsSource = new[]
        {
            new TabItem { Header = "그래프", Content = BuildGraphTab(), FontSize = 12 },
            new TabItem { Header = "도프 시트", Content = _dope, FontSize = 12 },
        };

        Grid.SetRow(_editorTabs, 2);
        grid.Children.Add(_editorTabs);
        return grid;
    }

    /// <summary>가운데 아래 탭 — 그래프 · 도프 시트.</summary>
    private readonly TabControl _editorTabs = new() { Padding = new Avalonia.Thickness(0) };

    /// <summary>마야 그래프 에디터 배치: 왼쪽 아웃라이너(검색 · 행 클릭 = 그 커브만) · 위 툴바 · 플롯.</summary>
    private Control BuildGraphTab()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("210,4,*") };

        var outliner = new DockPanel { Background = OutlinerBackground };
        DockPanel.SetDock(_outlinerSearch, Dock.Top);
        outliner.Children.Add(_outlinerSearch);
        outliner.Children.Add(new ScrollViewer { Content = _channelRows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _outlinerSearch.TextChanged += (_, _) => RefreshChannels();
        Grid.SetColumn(outliner, 0);
        grid.Children.Add(outliner);

        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Brushes.Transparent };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);

        var right = new DockPanel();
        Control toolbar = BuildGraphToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        right.Children.Add(toolbar);
        right.Children.Add(_graph);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>마야 그래프 에디터 툴바: 툴 · 맞춤 · 탄젠트 · 버퍼 · 스냅/보기 · Stats.</summary>
    private Control BuildGraphToolbar()
    {
        // 툴 버튼은 리전 하나 — 선택·이동·스케일은 Q/W/R, 끼우기 I, 맞춤 A/F(소유자, 2026-10-28). 리전은 토글: 다시 누르면 선택 툴로.
        _regionButton = SmallButton("리전", (_, _) =>
        {
            _graph.Tool = _graph.Tool == GraphTool.Region ? GraphTool.Select : GraphTool.Region;
            RefreshToolButtons();
        });
        _bufferButton = SmallButton("버퍼 찍기", (_, _) =>
        {
            if (_graph.HasBuffer)
            {
                _graph.SwapBuffer();
            }
            else
            {
                _graph.SnapshotBuffer();
            }

            RefreshToolButtons();
        });
        ToolTip.SetTip(_bufferButton, "버퍼 커브 — 없으면 보이는 커브를 흐린 점선으로 찍어 두고(Ctrl+B), 있으면 찍어 둔 것과 지금을 바꾼다(Ctrl+Shift+B). 새로 찍으려면 먼저 지운다");

        OnCommit(_keyValue, text => ApplyValueToSelectedKeys(text));
        ToolTip.SetTip(_snap, "키 배치·이동 및 타임라인 탐색 시, 시간을 지정한 간격의 배수에 맞춥니다. 간격은 설정 창의 \"시간 스냅 간격\"");
        _valueSnap.IsCheckedChanged += (_, _) =>
        {
            _graph.ValueSnapEnabled = _valueSnap.IsChecked == true;
        };
        _preInfinity.SelectionChanged += (_, _) =>
        {
            if (_preInfinity.SelectedIndex >= 0)
            {
                _graph.PreInfinity = (InfinityMode)_preInfinity.SelectedIndex;
                _graph.InvalidateVisual();
            }
        };
        _postInfinity.SelectionChanged += (_, _) =>
        {
            if (_postInfinity.SelectedIndex >= 0)
            {
                _graph.PostInfinity = (InfinityMode)_postInfinity.SelectedIndex;
                _graph.InvalidateVisual();
            }
        };
        ToolTip.SetTip(_preInfinity, "Pre Infinity — 첫 키 앞을 어떻게 이어 보이나(보기만 — 재생은 길이를 지나면 마지막 키가 남는다)");
        ToolTip.SetTip(_postInfinity, "Post Infinity — 마지막 키 뒤를 어떻게 이어 보이나(보기만)");
        ToolTip.SetTip(_valueSnap, "끌 때 키 값을 정수로(마야 Snap Values)");
        ToolTip.SetTip(_regionButton, "리전 툴(토글) — 상자로 고르고, 안을 끌면 이동, 모서리를 끌면 그 축만 반대쪽 기준 스케일. 다시 누르면 선택 툴(Q)로");
        _keyValue.Width = 64;
        ToolTip.SetTip(_keyValue, "선택 키의 값. 여러 키면 +=3 · *=2 식 상대 입력");

        var bar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Avalonia.Thickness(2, 2, 2, 4) };

        void Add(params Control[] controls)
        {
            foreach (Control c in controls)
            {
                c.Margin = new Avalonia.Thickness(1, 1);
                c.VerticalAlignment = VerticalAlignment.Center;
                bar.Children.Add(c);
            }
        }

        Add(SmallButton("재생 범위", (_, _) => _graph.FitRange()), Separator());
        Add(SmallButton("auto", (_, _) => _graph.SetTangentMode(TangentMode.Auto)),
            SmallButton("spline", (_, _) => _graph.SetTangentMode(TangentMode.Spline)),
            SmallButton("linear", (_, _) => _graph.SetTangentMode(TangentMode.Linear)),
            SmallButton("flat", (_, _) => _graph.SetTangentMode(TangentMode.Flat)),
            SmallButton("plateau", (_, _) => _graph.SetTangentMode(TangentMode.Plateau)),
            SmallButton("끊기", (_, _) => _graph.SetBroken(true)),
            SmallButton("붙이기", (_, _) => _graph.SetBroken(false)),
            Separator());
        Add(_bufferButton, SmallButton("버퍼 지우기", (_, _) =>
        {
            _graph.ClearBuffer();
            RefreshToolButtons();
        }), Separator());
        Add(_regionButton, Separator());
        Add(_snap, _valueSnap, Separator());
        Add(new TextBlock { Text = "무한", FontSize = 10, Opacity = 0.7 }, _preInfinity, _postInfinity, Separator());
        Add(new TextBlock { Text = "값", FontSize = 10, Opacity = 0.7 }, _keyValue);
        ToolTip.SetTip(bar, "툴 Q 선택 · W 이동 · R 스케일 · I 현재 시각에 키 끼우기 · A 전체 맞춤 · F 선택 맞춤 · 탄젠트 Ctrl+1 auto · 2 flat · 3 linear · 4 free · 5 spline · 6 plateau · 버퍼 Ctrl+B 찍기 / Ctrl+Shift+B 바꾸기");
        RefreshToolButtons();
        return bar;
    }

    private void RefreshTimeTexts()
    {
        _currentTime.Text = TimeText.Format(_graph.CurrentTime, Display);
        RefreshTransport();
        RefreshKeyInspector();
    }

    /// <summary>리전 버튼의 켜짐 표시와 버퍼 버튼의 라벨(찍기/바꾸기) — 툴·버퍼가 단축키로 바뀌어도 따라가도록 키 인스펙터 갱신 때마다 부른다.</summary>
    private void RefreshToolButtons()
    {
        if (_regionButton is not null)
        {
            bool active = _graph.Tool == GraphTool.Region;
            _regionButton.Opacity = active ? 1 : 0.55;
            _regionButton.FontWeight = active ? FontWeight.Bold : FontWeight.Normal;
        }

        if (_bufferButton is not null)
        {
            _bufferButton.Content = _graph.HasBuffer ? "버퍼 바꾸기" : "버퍼 찍기";
        }
    }


    private void RefreshPending()
    {
        _pendingText.Text = _viewport.IsHoldingPose
            ? "든 포즈(MMB 스크럽) — S로 이 프레임에 키를 놓거나 Esc·보통 스크럽으로 버린다."
            : _viewport.HasPending ? "키 없는 포즈 — S로 키를 놓거나 Esc로 버린다. 시각을 옮기면 사라진다." : "";
        _pendingText.IsVisible = _viewport.HasPending;
    }

    /// <summary>
    /// 채널 박스 — 고른 **노드**의 성분을 보인다.
    /// 칸 색: 이 시각에 키 = 빨강, 어딘가에 키 = 연한 빨강, 키 없는 포즈 = 주황.
    /// </summary>
    private void RefreshChannelBox()
    {
        RefreshAlphaPanel(); // 고른 노드가 바뀌면 불투명도 칸도
        _refreshDepth++;

        try
        {
            string? shown = _viewport.ShownSet;
            TimelineAsset? motion = _graph.Motion;
            EnsureChannelBoxRows();
            float time = (float)(_viewport.SnapEnabled ? TimeSnap.Snap(_viewport.Time, _viewport.SnapStep) : _viewport.Time);

            string? node = _viewport.SelectedNode;
            bool hasNode = node is not null && shown is not null && _viewport.HasStage;
            _channelBoxTitle.Text = !hasNode
                ? "(하이어아키에서 층을 고른다)"
                : $"{shown} · {node}";

            foreach (((MotionChannelKind kind, Axis axis), (Border row, TextBox box)) in _channelBoxRows)
            {
                float? value = hasNode ? _viewport.ComponentValue(node!, kind, axis) : null;
                box.IsEnabled = value is not null && motion is not null;
                string text = value is null ? "" : F(value.Value);

                if (!box.IsFocused && box.Text != text)
                {
                    box.Text = text;
                }

                ComponentCurve? curve = hasNode && motion is not null ? motion.FindChannel(node!, kind)?.Get(axis) : null;
                bool keyedHere = curve?.Keys.Any(k => Math.Abs(k.T - time) <= MotionRules.Tolerance) == true;
                bool pending = hasNode && _viewport.IsPending(node!, kind, axis);
                row.Background = pending ? ChannelBoxPending : keyedHere ? ChannelBoxKeyedHere : curve is not null ? ChannelBoxKeyed : Brushes.Transparent;
            }
        }
        finally
        {
            _refreshDepth--;
        }
    }

    /// <summary>채널 박스의 줄을 세운다(한 번) — 노드 성분 일곱 줄.</summary>
    private void EnsureChannelBoxRows()
    {
        if (_channelBoxRows.Count > 0)
        {
            return;
        }

        foreach ((MotionChannelKind kind, Axis axis, string label) in ChannelBoxComponents)
        {
            (Border row, TextBox box) = ChannelBoxRow(label,
                text => CommitChannelBox(kind, axis, text),
                () => KeyChannelBox(kind, axis),
                additive =>
                {
                    if (_viewport.SelectedNode is { } node && _graph.Motion?.FindChannel(node, kind)?.Get(axis) is not null)
                    {
                        ShowCurves(new[] { new CurveRef(node, kind, axis) }, additive);
                    }
                });
            _channelBoxRows[(kind, axis)] = (row, box);
        }
    }

    /// <summary>채널 박스 한 줄: 이름(클릭 = 그 커브를 그래프에) · 값 칸 · ●.</summary>
    private (Border Row, TextBox Box) ChannelBoxRow(string label, Action<string> commit, Action key, Action<bool> showCurves)
    {
        var box = new TextBox { FontSize = 11, MinWidth = 70, MinHeight = 22, Padding = new Avalonia.Thickness(4, 1), IsEnabled = false };
        var title = new TextBlock { Text = label, FontSize = 11, Width = 76, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand) };

        title.PointerPressed += (_, e) =>
        {
            showCurves(e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control));
            e.Handled = true;
        };
        Button keyButton = SmallButton("●", (_, _) => key());
        ToolTip.SetTip(keyButton, "이 성분을 지금 값으로 지금 시각에 키(마야 Key Selected)");
        var inner = new DockPanel();
        DockPanel.SetDock(title, Dock.Left);
        DockPanel.SetDock(keyButton, Dock.Right);
        inner.Children.Add(title);
        inner.Children.Add(keyButton);
        inner.Children.Add(box);
        var row = new Border { Padding = new Avalonia.Thickness(2, 1), Child = inner };
        OnCommit(box, commit);
        _channelBoxPanel.Children.Add(row);
        return (row, box);
    }

    /// <summary>노드 칸 입력: 숫자는 절대값, <c>+=</c>·<c>-=</c>·<c>*=</c>·<c>/=</c>는 지금 값에 상대.</summary>
    private void CommitChannelBox(MotionChannelKind kind, Axis axis, string text)
    {
        if (_refreshing || _viewport.SelectedNode is not { } node || _graph.Motion is null || !RelativeInput.TryParse(text, out Func<float, float> apply))
        {
            RefreshChannelBox();
            return;
        }

        float? current = _viewport.ComponentValue(node, kind, axis);

        if (current is null)
        {
            return;
        }

        float value = apply(current.Value);
        Guard("채널 박스", () => _viewport.SetComponent(node, kind, axis, value));
        RefreshChannelBox();
    }

    private void KeyChannelBox(MotionChannelKind kind, Axis axis)
    {
        if (_viewport.SelectedNode is { } node && _graph.Motion is not null)
        {
            Guard("키 놓기", () => _viewport.KeyComponent(node, kind, axis));
        }
    }

    private void RefreshTransport()
    {
        _refreshDepth++;

        try
        {
            _playMode.SelectedIndex = (int)_viewport.Mode;
            double[] speeds = { 0.25, 0.5, 1, 2 };
            int speedIndex = Array.FindIndex(speeds, v => Math.Abs(v - _viewport.Speed) < 1e-9);
            _playSpeed.SelectedIndex = speedIndex < 0 ? 2 : speedIndex;
            _rangeStart.Text = TimeText.Format(_viewport.Clock.RangeStart, Display);
            _rangeEnd.Text = TimeText.Format(_viewport.Clock.RangeEnd, Display);
            _animEnd.Text = TimeText.Format(_viewport.Clock.Duration, Display);
            _graph.RangeStart = _viewport.Clock.RangeStart;
            _graph.RangeEnd = _viewport.Clock.RangeEnd;
            _graph.InvalidateVisual();
        }
        finally
        {
            _refreshDepth--;
        }
    }

    /// <summary>그래프와 뷰포트의 현재 시각은 하나다 — 서로를 되부르지 않게 한쪽만 민다.</summary>
    private void SyncTime(Action push)
    {
        if (_syncingTime)
        {
            return;
        }

        _syncingTime = true;

        try
        {
            push();
        }
        finally
        {
            _syncingTime = false;
        }
    }

    private void BindMotion(string? name)
    {
        _followSelection = true; // 모션을 바꾸면 다시 따라간다
        _graph.Bind(_session.Editor, name);
        _dope.Bind(_session.Editor, name);

        _viewport.Bind(_session.Editor, name);

        _timeSlider.Bind(_session.Editor, name);
        RefreshSelectedNode();
    }

    /// <summary>뷰포트가 축을 그릴 노드 — 선택한 키의 노드, 없으면 첫 채널의 노드.</summary>
    private void RefreshSelectedNode()
    {
        TimelineAsset? motion = _graph.Motion;
        string? node = _graph.Selection.Count > 0
            ? _graph.Selection.First().Curve.Node
            : motion?.Channels.FirstOrDefault()?.Node;

        if (_followSelection || _viewport.SelectedNode is null)
        {
            _viewport.SelectedNode = node ?? _viewport.SelectedNode;
        }

        _viewport.InvalidateVisual();
        RefreshChannelBox();
        RefreshChannels();
    }

    /// <summary>테스트 손잡이 — 자산(세트·그림)을 뷰포트에 다시 실은 횟수. 키 편집에서는 오르지 않아야 한다.</summary>
    internal int AssetRefreshCount { get; private set; }

    private void RefreshAssets()
    {
        AssetRefreshCount++;
        _viewport.ClearImages(); // 세트의 모양도 다시 세운다

        // 층의 그림 — 파일이 없거나 못 읽으면 null을 넣는다(자리표시로 그려진다).
        foreach ((string image, string? imagePath) in _session.LayerImages())
        {
            Bitmap? bitmap = null;

            try
            {
                bitmap = imagePath is null ? null : new Bitmap(imagePath);
            }
            catch (Exception exception)
            {
                _session.SetStatus($"그림 {image}를 읽지 못했다: {exception.Message}");
            }

            _viewport.SetImage(image, bitmap);
        }

        RefreshChannelBox();
        RefreshObjects();
        RefreshProblems();
    }

    private void BuildToolbar()
    {
        Toolbar.Children.Add(ToolButton("새로", (_, _) => Guard("새 문서", _session.New)));
        Toolbar.Children.Add(ToolButton("열기…", OnOpenClick));
        Toolbar.Children.Add(_recentButton);
        Toolbar.Children.Add(ToolButton("저장", OnSaveClick));
        Toolbar.Children.Add(ToolButton("다른 이름으로…", OnSaveAsClick));
        Toolbar.Children.Add(Separator());
        Toolbar.Children.Add(ToolButton("되돌리기", (_, _) => UndoCommand()));
        Toolbar.Children.Add(ToolButton("다시하기", (_, _) => RedoCommand()));
        Toolbar.Children.Add(Separator());
        Toolbar.Children.Add(ToolButton("설정…", (_, _) => OpenSettings()));
        Toolbar.Children.Add(ToolButton("단축키 (F1)", (_, _) => OpenShortcuts()));
    }

    private void BuildLeft()
    {
        LeftPanel.Children.Add(Header("층"));
        _hierarchy.SelectionChanged += (_, _) => OnHierarchySelected();
        // 더블클릭은 노드 선택과 같다(채널 박스가 그 노드를 보인다). "채널 추가"는 없다.
        _hierarchy.DoubleTapped += (_, _) =>
        {
            switch (_hierarchy.SelectedItem)
            {
                case TreeViewItem { Tag: HierarchyNode node }:
                    SelectNodeInHierarchy(node);
                    break;
            }
        };
        _hierarchy.ContextMenu = _hierarchyMenu;
        _hierarchyMenu.Opening += (_, _) => RebuildHierarchyMenu();
        _hierarchy.AddHandler(PointerPressedEvent, OnHierarchyPointerPressed, RoutingStrategies.Tunnel);
        LeftPanel.Children.Add(_hierarchy);
        ToolTip.SetTip(_hierarchy, "무대에 선 세트의 층. 클릭 = 고르기(채널 박스·매니퓰레이터가 그 층을 잡는다). 세트를 열었으면 우클릭으로 층을 짓고 고친다");

        BuildSetLeft();
    }

    /// <summary>
    /// 오른쪽 = 탭: 채널 박스(기본) · 세트(층) · 모션(속성). 설정·단축키는 툴바의 창으로,
    /// 단축키의 그림자 버튼(Q/W/E/R·S·Esc·복사·붙여넣기…)은 없다. 마지막 탭·접힌 묶음은 설정에 남는다.
    /// </summary>
    private void BuildRight()
    {
        _inspectorTabs.ItemsSource = new[]
        {
            new TabItem { Header = "채널 박스", Content = BuildChannelBoxTab(), FontSize = 12 },
            new TabItem { Header = "세트", Content = BuildSetTab(), FontSize = 12 },
            new TabItem { Header = "타임라인", Content = BuildMotionTab(), FontSize = 12 },
        };
        _inspectorTabs.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _inspectorTabs.SelectedIndex >= 0)
            {
                _settings.InspectorTab = _inspectorTabs.SelectedIndex;
            }
        };
        RightPanel.Children.Add(_inspectorTabs);
        WireSettingsControls();
    }

    private Control BuildChannelBoxTab()
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(0, 6, 0, 0) };
        panel.Children.Add(_channelBoxTitle);
        ToolTip.SetTip(_channelBoxTitle, "고른 노드의 Translate·Rotate·Scale. 칸에 값을 넣으면(+=·-=·*=·/= 는 지금 값에 상대) 매니퓰레이터를 놓은 것과 같다: Auto Key거나 그 시각에 키가 있으면 키, 아니면 키 없는 포즈(S로 키·Esc로 버림). 묶음 제목 클릭 = 접기/펼치기 · 글자 클릭 = 그 커브를 그래프에(Shift/Ctrl: 더하기) · ● = 이 성분을 지금 키");
        panel.Children.Add(_channelBoxPanel);
        panel.Children.Add(_pendingText);

        _autoKeyToggle.IsCheckedChanged += (_, _) =>
        {
            _viewport.AutoKey = _autoKeyToggle.IsChecked == true;
            _autoKeyToggle.Foreground = _viewport.AutoKey ? AutoKeyOnBrush : null;

            if (!_refreshing)
            {
                _settings.AutoKey = _viewport.AutoKey;
            }
        };
        _stageOnlyToggle.IsCheckedChanged += (_, _) => _viewport.StageOnly = _stageOnlyToggle.IsChecked == true;
        ToolTip.SetTip(_stageOnlyToggle, "무대만 보기 — 무대 밖을 다 가리고 격자를 끈다: 게임에서 보이는 그대로. 끄면 무대 밖의 것이 흐리게 비친다(어디서 들어오는지 보며 고친다)");
        ToolTip.SetTip(_autoKeyToggle, "Auto Key — 매니퓰레이터를 놓거나 채널 박스에 값을 넣는 순간 키가 들어간다(마야 빨간 키). 꺼져 있으면 키 없는 포즈 → S");
        _viewport.PendingChanged += (_, _) =>
        {
            RefreshPending();
            RefreshChannelBox();
        };
        _viewport.PoseKeyed += (_, _) => RefreshChannelBox();
        return panel;
    }

    /// <summary>세트 탭 — 연 세트의 층 칸.</summary>
    private Control BuildSetTab()
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(0, 6, 0, 0) };
        _viewport.StageChanged += (_, _) => RefreshObjects();
        panel.Children.Add(BuildSetNodePanel());
        return panel;
    }

    private Control BuildMotionTab()
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(0, 6, 0, 0) };
        panel.Children.Add(Labeled("길이(초)", _motionDuration));

        panel.Children.Add(BuildAlphaPanel());
        panel.Children.Add(BuildEventPanel());

        OnCommit(_motionDuration, text => WithMotion(m => { if (TryFloat(text, out float v) && v != m.Duration) _session.Editor.SetDuration(m.Name, v); }));
        return panel;
    }

    /// <summary>설정 창의 컨트롤들은 미리 묶어 두고(창을 열 때마다 같은 인스턴스), 바꾸는 즉시 적용한다.</summary>
    private void WireSettingsControls()
    {
        _snap.IsCheckedChanged += (_, _) =>
        {
            if (_refreshing)
            {
                return;
            }

            _settings.SnapEnabled = _snap.IsChecked == true;
            PushSettingsToViews();
        };
        _snapStepCombo.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _snapStepCombo.SelectedIndex >= 0)
            {
                _settings.SnapStep = SnapSteps[_snapStepCombo.SelectedIndex];
                PushSettingsToViews();
            }
        };
        _autoSaveCombo.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _autoSaveCombo.SelectedIndex >= 0)
            {
                _settings.AutoSaveMinutes = AutoSaveMinutes[_autoSaveCombo.SelectedIndex];
                PushSettingsToViews();
            }
        };
        _newKeyTangent.SelectionChanged += (_, _) =>
        {
            if (!_refreshing)
            {
                _settings.DefaultTangent = DefaultTangent;
                PushSettingsToViews();
            }
        };
        OnCommit(_currentTime, text => { if (TimeText.TryParse(text, Display, out double v)) _graph.CurrentTime = v; });
        _timeDisplayCombo.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _timeDisplayCombo.SelectedIndex >= 0)
            {
                _settings.TimeDisplay = (TimeDisplay)_timeDisplayCombo.SelectedIndex;
                PushSettingsToViews();
            }
        };
    }

    /// <summary>설정 창(마야 Preferences) — 툴바 "설정…". 바꾸는 즉시 적용, 닫을 때 저장. 컨트롤은 같은 인스턴스를 붙였다 뗀다.</summary>
    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var panel = new StackPanel { Margin = new Avalonia.Thickness(12), Spacing = 8 };
        _settingsRows.Clear();

        void Row(string label, Control control)
        {
            var row = (Panel)Labeled(label, control);
            _settingsRows.Add(row);
            panel.Children.Add(row);
        }

        panel.Children.Add(Header("하이어아키"));
        panel.Children.Add(Header("키"));
        ToolTip.SetTip(_snapStepCombo, "키 배치·이동 및 타임라인 탐색 시, 시간을 지정한 간격의 배수에 맞춥니다.");
        Row("시간 스냅 간격", _snapStepCombo);
        Row("새 키 탄젠트", _newKeyTangent);
        panel.Children.Add(Header("표시 · 저장"));
        Row("시간 표시", _timeDisplayCombo);
        Row("자동 저장", _autoSaveCombo);
        panel.Children.Add(new TextBlock { Text = "바꾸는 즉시 적용되고, 창을 닫을 때 설정 파일에 남는다.", FontSize = 10, Opacity = 0.6, Margin = new Avalonia.Thickness(0, 6, 0, 0) });

        _settingsWindow = new Window { Title = "설정", Width = 400, Height = 380, CanResize = false, Content = panel };
        _settingsWindow.Closed += (_, _) =>
        {
            foreach (Panel row in _settingsRows)
            {
                row.Children.Clear();
            }

            panel.Children.Clear();
            _settingsWindow = null;
            SaveSettings();
        };
        _settingsWindow.Show();
    }

    /// <summary>단축키 창 — 툴바 "단축키" · F1. 읽기만.</summary>
    private void OpenShortcuts()
    {
        if (_shortcutsWindow is not null)
        {
            _shortcutsWindow.Activate();
            return;
        }

        var text = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Text = ShortcutsText };
        _shortcutsWindow = new Window
        {
            Title = "단축키",
            Width = 560,
            Height = 620,
            Content = new ScrollViewer { Content = text, Padding = new Avalonia.Thickness(14) },
        };
        _shortcutsWindow.Closed += (_, _) => _shortcutsWindow = null;
        _shortcutsWindow.Show();
    }

    internal const string ShortcutsText =
        "전역 (창 어디서나 · 글 입력 칸 제외)\n" +
        "  1 / 2  이전·다음 키        Alt+1 / Alt+2  이전·다음 프레임\n" +
        "  `  첫 프레임               Alt+`  처음부터 재생        Alt+Q  재생/일시정지\n" +
        "  S  키 놓기 — 키 없는 포즈가 있으면 그것을, 없으면 고른 노드의 지금 값을 현재 시각에\n" +
        "  Esc  키 없는 포즈 버리기\n" +
        "  Q / W / E / R  뷰포트 선택·이동·회전·배율\n" +
        "  Shift+W / E / F  현재 시각의 키 복사·붙여넣기·지우기      Shift+1 / 2  인비트윈 빼기·넣기\n" +
        "  Ctrl+C / X / V · Del  키 복사·잘라내기·붙여넣기·삭제      Ctrl+Z / Y  되돌리기·다시 하기\n" +
        "  F1  이 창\n\n" +
        "뷰포트\n" +
        "  휠  커서 축으로 확대·축소      MMB 드래그  보기 이동      F  보기 맞춤\n" +
        "  배치 토글을 켰을 때 — 오브젝트 드래그  자리      Shift+드래그  배율      Ctrl+드래그  회전   (가만히 선 자세다)\n" +
        "  LMB  기즈모 끌기(Auto Key면 놓는 순간 키)      Home / End  처음·끝      ← / →  한 프레임\n\n" +
        "그래프 에디터 (마야)\n" +
        "  LMB  키 선택 · 빈 자리 드래그 박스 선택(키가 들면 키만, 커브만 지나면 커브) · Ctrl+드래그 라소\n" +
        "  MMB 드래그  선택 키 이동(핸들을 골랐으면 핸들 회전)      MMB 클릭  고른 커브에 키 끼우기\n" +
        "  RMB 드래그  선택 키 둘레면 한 축만 이동, 빈 자리면 팬      Alt+MMB  팬      Alt+RMB  줌 드래그      휠  줌\n" +
        "  더블클릭  커브 위에 키      I  현재 시각에 키      A / F  전체 · 선택 맞춤\n" +
        "  Q / W / R  선택·이동·스케일 툴(그래프 안)      Ctrl+1…6  탄젠트 auto·flat·linear·free·spline·plateau\n" +
        "  Ctrl+B / Ctrl+Shift+B  버퍼 커브 찍기·바꾸기\n\n" +
        "도프 시트\n" +
        "  블록 드래그  시간 이동      Alt+드래그  복제      범위 띠 양끝  시간 스케일      더블클릭  키      노드 행 클릭  접기\n\n" +
        "타임 슬라이더\n" +
        "  클릭·드래그  스크럽      Shift+드래그  프레임 범위 선택      범위 안 MMB  범위의 키 이동      범위 밖 MMB  포즈 들고 스크럽(S로 키)\n" +
        "  우클릭  복사·붙여넣기·삭제·스냅 · 재생 범위 · 재생 모드 메뉴\n\n" +
        "하이어아키 (층)\n" +
        "  클릭  세트의 오브젝트 고르기\n" +
        "  우클릭(세트를 열었을 때)  오브젝트 더하기 · 부모 바꾸기 · 앞뒤 순서 · 숨기기 · 시차 · 화면 밖에서 들어온다/나간다";

    /// <summary>테스트 손잡이.</summary>
    internal int InspectorTabCount => _inspectorTabs.ItemCount;

    internal int InspectorTab
    {
        get => _inspectorTabs.SelectedIndex;
        set => _inspectorTabs.SelectedIndex = value;
    }

    internal void OpenSettingsFromMenu() => OpenSettings();

    internal void OpenShortcutsFromMenu() => OpenShortcuts();

    // ── 문서·목록 갱신 ─────────────────────────────────────────────────

    private void OnDocumentReplaced()
    {
        _session.Editor.Changed += (_, _) => OnEditorChanged();
        BindMotion(null);
        RefreshAll();
        RefreshAssets();
    }

    private void OnEditorChanged()
    {
        RefreshSets();
        ApplySetView(); // 세트가 지워지거나(되돌리기) 이름이 바뀌었으면 무대와 타임라인이 따라온다
        RefreshMotionInspector();
        RefreshKeyInspector();
        RefreshProblems();
        RefreshObjects();
        RefreshChannelBox();
    }

    /// <summary>하이어아키를 무대의 세트에 맞춘다.</summary>
    private void RefreshObjects()
    {
        _refreshDepth++;

        try
        {
            RebuildHierarchy();
        }
        finally
        {
            _refreshDepth--;
        }

        RefreshChannelBox();
    }

    // ── 하이어아키 ─────────────────────────────────────────────────────

    /// <summary>트리 항목의 꼬리표 — 세트의 층(노드).</summary>
    private sealed record HierarchyNode(string NodeId);

    /// <summary>세트 항목의 꼬리표 — 하이어아키의 맨 위.</summary>
    private sealed record HierarchyFolder;

    /// <summary>무대에 선 세트 하나와 그 층들(부모 › 자식). 펼침 상태는 키로 기억한다.</summary>
    private void RebuildHierarchy()
    {
        bool nodeWasSelected = _hierarchy.SelectedItem is TreeViewItem { Tag: HierarchyNode };
        _hierarchy.Items.Clear();
        _hierarchyParents.Clear();

        if (_viewport.ShownSet is not { } kind || _session.Editor.Document.FindTransition(kind) is not { } set)
        {
            return;
        }

        TreeViewItem? selectedItem = null;
        var setRoot = Folder($"세트  {kind}", "set:" + kind);
        setRoot.Tag = new HierarchyFolder();
        Attach(setRoot, null);
        var itemsByNode = new Dictionary<string, TreeViewItem>(StringComparer.Ordinal);

        foreach (LayerAsset node in set.Layers)
        {
            TreeViewItem nodeParent = string.IsNullOrEmpty(node.Parent) || !itemsByNode.TryGetValue(node.Parent, out TreeViewItem? found) ? setRoot : found;
            // 따로 보기(숨김 · 솔로)의 표시.
            bool shown = _viewport.IsSetNodeShown(node.Id);
            string mark = _viewport.SetView is null ? "" : _viewport.SoloSetNode == node.Id ? "   ◉ 이것만" : _viewport.IsSetNodeHidden(node.Id) ? "   (숨김)" : "";
            TreeViewItem nodeItem = Folder(node.Id + mark, $"node:{kind}/{node.Id}");
            nodeItem.Opacity = shown ? 1 : 0.45;
            nodeItem.Tag = new HierarchyNode(node.Id);
            nodeItem.FontWeight = FontWeight.Normal;
            nodeItem.Foreground = ActingNodeBrush;
            ToolTip.SetTip(nodeItem, node.Id);
            Attach(nodeItem, nodeParent);
            itemsByNode[node.Id] = nodeItem;

            if (nodeWasSelected && node.Id == _viewport.SelectedNode)
            {
                selectedItem = nodeItem;
            }
        }

        _hierarchy.Items.Add(setRoot);

        // 고른 항목이 보이도록 조상을 펼친다.
        for (TreeViewItem? up = selectedItem is null ? null : _hierarchyParents.GetValueOrDefault(selectedItem); up is not null; up = _hierarchyParents.GetValueOrDefault(up))
        {
            up.IsExpanded = true;
        }

        _hierarchy.SelectedItem = selectedItem;
    }

    private void Attach(TreeViewItem item, TreeViewItem? parent)
    {
        _hierarchyParents[item] = parent;
        parent?.Items.Add(item);
    }

    private TreeViewItem Folder(string header, string key, bool defaultExpanded = true)
    {
        bool expanded = defaultExpanded ? !_collapsedHierarchy.Contains(key) : _expandedHierarchy.Contains(key);
        var item = new TreeViewItem { Header = header, FontSize = 11, IsExpanded = expanded };
        item.PropertyChanged += (_, e) =>
        {
            if (e.Property == TreeViewItem.IsExpandedProperty)
            {
                if (item.IsExpanded == defaultExpanded)
                {
                    _collapsedHierarchy.Remove(key);
                    _expandedHierarchy.Remove(key);
                }
                else if (defaultExpanded)
                {
                    _collapsedHierarchy.Add(key);
                }
                else
                {
                    _expandedHierarchy.Add(key);
                }
            }
        };
        return item;
    }

    /// <summary>세트의 층 항목을 전부 펼치거나 접는다.</summary>
    private void ExpandSetNodes(bool expand)
    {
        foreach (TreeViewItem item in _hierarchyParents.Keys.Where(i => i.Tag is HierarchyNode or HierarchyFolder).ToList())
        {
            item.IsExpanded = expand;
        }
    }

    // ── 하이어아키 메뉴·단축키 ──────────────────────────────────────────

    private static TreeViewItem? ItemUnder(object? source)
        => (source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true);

    private void OnHierarchyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPointProperties props = e.GetCurrentPoint(_hierarchy).Properties;
        TreeViewItem? item = ItemUnder(e.Source);

        if (props.IsRightButtonPressed)
        {
            _menuTarget = item?.Tag;

            if (item?.Tag is HierarchyNode)
            {
                _hierarchy.SelectedItem = item;
            }

            return;
        }
    }

    /// <summary>유니티 하이어아키 우클릭 메뉴. 우클릭한 자리(_menuTarget)에 따라 달라진다.</summary>
    private void RebuildHierarchyMenu()
    {
        _hierarchyMenu.Items.Clear();

        // 세트를 열었으면 층을 짓는 메뉴.
        if (_viewport.SetView is not null)
        {
            AddSetMenu(_menuTarget);
        }
    }

    private void OnHierarchySelected()
    {
        if (_refreshing)
        {
            return;
        }

        switch (_hierarchy.SelectedItem)
        {
            case TreeViewItem { Tag: HierarchyNode node }:
                SelectNodeInHierarchy(node);
                break;
        }
    }

    /// <summary>노드 항목: 채널 박스·매니퓰레이터의 노드를 그 노드로. 키 따라가기는 끈다(사용자가 직접 골랐다).</summary>
    private void SelectNodeInHierarchy(HierarchyNode node)
    {
        // 세트 보기에서는 그 오브젝트가 세트 탭의 세트 칸에 뜬다.
        if (_viewport.SetView is not null)
        {
            _rigNodeSelected = node.NodeId;
            RefreshSetNodePanel();
        }

        _followSelection = false;
        _viewport.SelectedNode = node.NodeId;
        ShowCurves(_graph.Curves.Select(s => s.Curve).Where(cr => cr.Node == node.NodeId).ToList(), additive: false);
        _viewport.InvalidateVisual();
        RefreshChannelBox();
        RefreshChannels();
    }

    /// <summary>테스트 손잡이 — 하이어아키에서 층을 고른 것과 같다.</summary>
    internal void SelectInHierarchy(string nodeId) => SelectNodeInHierarchy(new HierarchyNode(nodeId));

    /// <summary>테스트 손잡이 — 하이어아키 항목의 경로(세트부터 헤더 순). 없으면 빈 목록.</summary>
    internal IReadOnlyList<string> HierarchyPathOf(string nodeId)
        => PathOf(_hierarchyParents.Keys.FirstOrDefault(i => i.Tag is HierarchyNode n && n.NodeId == nodeId));

    private IReadOnlyList<string> PathOf(TreeViewItem? item)
    {
        var path = new List<string>();

        for (TreeViewItem? up = item; up is not null; up = _hierarchyParents.GetValueOrDefault(up))
        {
            path.Insert(0, up.Header?.ToString() ?? "");
        }

        return path;
    }

    private static TextBlock Small(string text) => new() { Text = text, FontSize = 10, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };

    private void RefreshAll()
    {
        RefreshSets();
        RefreshChannels();
        RefreshMotionInspector();
        RefreshKeyInspector();
        RefreshProblems();
        StatusText.Text = _session.Status;
    }

    /// <summary>
    /// 마야 그래프 에디터 아웃라이너 — 노드 › 채널 › 성분. 행 클릭 = 그 커브만 보인다, Shift/Ctrl+클릭 = 더하기/빼기.
    /// 없는 성분은 흐리고, 클릭하면 현재 시각의 중립값 키 하나로 생긴다.
    /// </summary>
    /// <summary>
    /// 아웃라이너(그래프 왼쪽) — 모션의 채널을 노드로 보인다. 흐린 성분을 클릭해도 성분을 만들지 않는다(마야식) — 성분은 키를 놓을 때만 생긴다.
    /// </summary>
    private void RefreshChannels()
    {
        _channelRows.Children.Clear();
        TimelineAsset? motion = _graph.Motion;

        if (motion is null)
        {
            _channelRows.Children.Add(new TextBlock { Text = "(모션 없음)", FontSize = 11, Opacity = 0.6, Margin = new Avalonia.Thickness(6, 4) });
            return;
        }

        string query = (_outlinerSearch.Text ?? "").Trim();
        RefreshChannelsByNode(motion, query);
    }

    /// <summary>모션의 채널을 노드 · 종류로, 성분은 종류의 축 전부(없는 것은 흐리게).</summary>
    private void RefreshChannelsByNode(TimelineAsset motion, string query)
    {
        foreach (ChannelAsset channel in motion.Channels)
        {
            string node = channel.Node;
            MotionChannelKind kind = channel.Kind;
            if (query.Length > 0 && !node.Contains(query, StringComparison.OrdinalIgnoreCase) && !ChannelKindLabel.Of(kind).Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Axis[] possibleAxes = kind == MotionChannelKind.Euler ? new[] { Axis.X, Axis.Y, Axis.Z } : new[] { Axis.X, Axis.Y };
            CurveRef[] axesOfChannel = possibleAxes.Select(a => new CurveRef(node, kind, a)).ToArray();
            CurveRef[] existing = axesOfChannel.Where(cr => cr.Resolve(motion) is not null).ToArray();
            bool anyLocked = existing.Any(_flags.IsLocked);
            bool anyMuted = existing.Any(_flags.IsMuted);

            var header = new DockPanel { Margin = new Avalonia.Thickness(2, 3, 2, 0) };
            Button remove = SmallButton("×", (_, _) => Guard("채널 삭제", () => _session.Editor.RemoveChannel(motion.Name, node, kind)));
            ToolTip.SetTip(remove, "채널 삭제(성분 전부)");
            Button lockButton = SmallButton(anyLocked ? "🔒" : "🔓", (_, _) =>
            {
                foreach (CurveRef cr in existing)
                {
                    _flags.SetLocked(cr, !anyLocked);
                }
            });
            ToolTip.SetTip(lockButton, anyLocked ? "잠금 풀기 — 다시 고르고 끌 수 있다" : "잠금 — 보이되 고르거나 끌 수 없다");
            Button muteButton = SmallButton(anyMuted ? "🔇" : "🔊", (_, _) =>
            {
                foreach (CurveRef cr in existing)
                {
                    _flags.SetMuted(cr, !anyMuted);
                }
            });
            ToolTip.SetTip(muteButton, anyMuted ? "뮤트 풀기" : "뮤트 — 뷰포트 재생에서 이 채널을 뺀다(저장되지 않는다)");

            foreach (Button b in new[] { remove, muteButton, lockButton })
            {
                DockPanel.SetDock(b, Dock.Right);
                header.Children.Add(b);
            }

            var title = new TextBlock
            {
                Text = $"{node} · {ChannelKindLabel.Of(kind)}",
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(title, $"{node}\n{kind}\n클릭: 이 채널의 커브만 · Shift/Ctrl: 더하기");
            title.PointerPressed += (_, e) => ShowCurves(existing, additive: e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control));
            header.Children.Add(title);
            _channelRows.Children.Add(header);

            foreach (CurveRef curveRef in axesOfChannel)
            {
                _channelRows.Children.Add(ComponentRow(motion, curveRef, $"{ChannelKindLabel.Of(kind)} {curveRef.Axis.ToString().ToUpperInvariant()}"));
            }
        }

        AddUnkeyedRows(motion, query);

        if (motion.Channels.Count == 0)
        {
            _channelRows.Children.Add(new TextBlock { Text = "(채널 없음 — 하이어아키에서 노드를 고르고 채널 박스에 값을 넣거나 매니퓰레이터로 끌면 생긴다)", FontSize = 11, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Avalonia.Thickness(6, 4) });
        }
    }

    /// <summary>노드가 적은 세트에서만 키 없는 채널을 늘어놓는다 — 노드가 많으면 목록이 묻힌다.</summary>
    private const int UnkeyedRowsMaxNodes = 16;

    /// <summary>테스트 손잡이 — 그래프 목록의 "키 없음" 줄들(노드 · 종류).</summary>
    internal IReadOnlyList<(string Node, MotionChannelKind Kind)> UnkeyedRows => _unkeyedRows;

    internal void KeyUnkeyedRow(string node, MotionChannelKind kind) => KeyUnkeyed(node, kind);

    private readonly List<(string Node, MotionChannelKind Kind)> _unkeyedRows = new();

    /// <summary>
    /// 아직 키가 없는 채널들을 흐리게 — 이 모션이 안 건드리는 노드·종류(위치·배율·회전·불투명도)도 보이고, 눌러서 그 노드를 고르거나 ＋로 지금 값에 키를 놓는다.
    /// "배율만 있는 모션에 살짝 위치도" 같은 손을 목록에서 바로 간다.
    /// </summary>
    private void AddUnkeyedRows(TimelineAsset motion, string query)
    {
        _unkeyedRows.Clear();

        if (_session.Editor.Document.FindTransition(motion.Name) is not { } set || set.Layers.Count > UnkeyedRowsMaxNodes)
        {
            return;
        }

        bool headed = false;

        foreach (LayerAsset layer in set.Layers)
        {
            string node = layer.Id;

            foreach (MotionChannelKind kind in new[] { MotionChannelKind.Position, MotionChannelKind.Scale, MotionChannelKind.Euler, MotionChannelKind.Alpha })
            {
                if (motion.FindChannel(node, kind) is not null || (kind == MotionChannelKind.Alpha && !layer.Alpha))
                {
                    continue;
                }

                if (query.Length > 0 && !node.Contains(query, StringComparison.OrdinalIgnoreCase) && !ChannelKindLabel.Of(kind).Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!headed)
                {
                    headed = true;
                    _channelRows.Children.Add(new TextBlock { Text = "키 없음", FontSize = 10, Opacity = 0.55, Margin = new Avalonia.Thickness(4, 8, 2, 0) });
                }

                _unkeyedRows.Add((node, kind));
                string capturedNode = node;
                MotionChannelKind capturedKind = kind;
                var row = new DockPanel { Margin = new Avalonia.Thickness(2, 1, 2, 0), Opacity = 0.5 };
                Button add = SmallButton("＋", (_, _) => Guard("키 놓기", () => KeyUnkeyed(capturedNode, capturedKind)));
                ToolTip.SetTip(add, "지금 값을 현재 시각에 키 — 채널이 생긴다. 그 뒤 그래프·뷰포트에서 고친다");
                DockPanel.SetDock(add, Dock.Right);
                row.Children.Add(add);
                var title = new TextBlock
                {
                    Text = $"{node} · {ChannelKindLabel.Of(kind)}",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                ToolTip.SetTip(title, $"{node}\n{kind} — 이 모션에 아직 키가 없다\n클릭: 이 노드를 고른다(뷰포트의 손잡이·채널 박스가 이 노드를 본다) · ＋: 지금 값에 키");
                title.PointerPressed += (_, e) =>
                {
                    _viewport.SelectedNode = capturedNode;
                    RefreshChannelBox();
                    e.Handled = true;
                };
                row.Children.Add(title);
                _channelRows.Children.Add(row);
            }
        }
    }

    /// <summary>키 없는 채널에 지금 값을 지금 시각의 키로 — 채널이 생긴다. 뷰포트의 길로 놓는다.</summary>
    private void KeyUnkeyed(string node, MotionChannelKind kind)
    {
        _viewport.SelectedNode = node;

        if (kind == MotionChannelKind.Alpha)
        {
            _viewport.SetAlphaValue(node, _viewport.AlphaValue(node) ?? 1f);
            return;
        }

        _viewport.KeyComponents(node, kind, kind == MotionChannelKind.Euler ? new[] { Axis.Z } : new[] { Axis.X, Axis.Y });
    }

    /// <summary>성분 행: 색 네모 + 이름 + (있으면) ×. 보이는 행은 파랗게(마야 선택). 없는 성분은 흐리고, 클릭해도 만들지 않는다 — <paramref name="onMissingClick"/>만.</summary>
    private Border ComponentRow(TimelineAsset motion, CurveRef curveRef, string label)
    {
        CurveSlot? slot = _graph.Curves.FirstOrDefault(s => s.Curve == curveRef);
        bool exists = curveRef.Resolve(motion) is not null;
        bool visible = slot?.Visible == true;
        Color color = slot?.Color ?? Color.FromRgb(0x80, 0x80, 0x80);
        var content = new DockPanel { Opacity = exists ? 1 : 0.45 };

        if (exists)
        {
            Button remove = SmallButton("×", (_, e) =>
            {
                Guard("성분 삭제", () => RemoveComponent(_graph.Motion ?? motion, curveRef));
                e.Handled = true;
            });
            ToolTip.SetTip(remove, "이 성분의 키만 삭제 — 다른 성분은 남는다(마지막 성분이면 채널이 없어진다)");
            DockPanel.SetDock(remove, Dock.Right);
            content.Children.Add(remove);
        }

        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new Border { Width = 8, Height = 8, Background = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = label, FontSize = 11, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center },
            },
        });

        var row = new Border
        {
            Padding = new Avalonia.Thickness(18, 1, 4, 1),
            Background = visible ? OutlinerRowSelected : Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = curveRef,
            Child = content,
        };
        ToolTip.SetTip(row, exists
            ? "클릭: 이 커브만 · Shift/Ctrl+클릭: 더하기/빼기 · ×: 이 성분의 키 삭제"
            : "성분이 없다 — 채널 박스에 값을 넣거나 매니퓰레이터로 끌거나 S로 키를 놓으면 생긴다(클릭은 만들지 않는다)");
        row.PointerPressed += (_, e) =>
        {
            bool additive = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
            OutlinerClick(curveRef, additive);
            e.Handled = true;
        };
        return row;
    }

    /// <summary>성분 하나의 키를 지운다 — 다른 성분은 남고, 마지막 성분이면 채널이 없어진다(빈 채널은 규약에 없다).</summary>
    private void RemoveComponent(TimelineAsset motion, CurveRef curve)
    {
        if (motion.FindChannel(curve.Node, curve.Kind) is not { } channel || channel.Get(curve.Axis) is null)
        {
            return;
        }

        int remaining = new[] { Axis.X, Axis.Y, Axis.Z }.Count(a => channel.Get(a) is not null);

        if (remaining <= 1)
        {
            _session.Editor.RemoveChannel(motion.Name, curve.Node, curve.Kind);
            return;
        }

        _session.Editor.SetKeys(motion.Name, curve.Node, curve.Kind, curve.Axis, Array.Empty<KeyAsset>());
    }

    /// <summary>아웃라이너 성분 행 클릭 — 있는 성분은 그 커브만(additive면 토글). 없는 성분은 **만들지 않는다**(마야식).</summary>
    internal void OutlinerClick(CurveRef curve, bool additive)
    {
        TimelineAsset? motion = _graph.Motion;

        if (motion is null)
        {
            return;
        }

        if (curve.Resolve(motion) is null)
        {
            return;
        }

        if (additive)
        {
            bool wasVisible = _graph.Curves.FirstOrDefault(s => s.Curve == curve)?.Visible == true;
            _graph.SetVisible(curve, !wasVisible);
            return;
        }

        _graph.SetVisibleAll(c => c == curve);
    }

    /// <summary>S(Set Key) — 키 없는 포즈(드래그·채널 박스·MMB 스크럽)가 있으면 그것을, 없으면 뷰포트의 선택 노드를 지금 값으로 현재 시각에 키.</summary>
    private void SetKeySelected()
    {
        Guard("키 놓기", () => _session.Editor.Batch(MotionChangeKind.Keys, _viewport.SetKey)); // 키 없는 포즈 전부가 되돌리기 한 칸
    }

    /// <summary>테스트 손잡이 — 성분 행의 ×.</summary>
    internal void RemoveComponentFromOutliner(CurveRef curve)
    {
        if (_graph.Motion is { } motion)
        {
            RemoveComponent(motion, curve);
        }
    }

    /// <summary>테스트 손잡이 — S.</summary>
    internal void SetKeyFromShell() => SetKeySelected();

    private void ShowCurves(IReadOnlyCollection<CurveRef> curves, bool additive)
    {
        if (additive)
        {
            _graph.SetVisibleAll(c => curves.Contains(c) || (_graph.Curves.FirstOrDefault(s => s.Curve == c)?.Visible == true));
            return;
        }

        _graph.SetVisibleAll(curves.Contains);
    }

    private void RefreshMotionInspector()
    {
        _refreshDepth++;

        try
        {
            TimelineAsset? motion = _graph.Motion;
            bool has = motion is not null;
            _motionDuration.IsEnabled = has;

            _motionDuration.Text = motion is null ? "" : motion.Duration.ToString("0.###", CultureInfo.InvariantCulture);
        }
        finally
        {
            _refreshDepth--;
            RefreshEventPanel();
        }
    }

    private void RefreshKeyInspector()
    {
        _refreshDepth++;

        try
        {
            TimelineAsset? motion = _graph.Motion;
            int count = _graph.Selection.Count;
            KeyAsset? single = count == 1 && motion is not null ? _graph.Selection.First().Resolve(motion) : null;
            _keyValue.IsEnabled = count > 0;

            _keyValue.Text = single is null ? (count > 0 ? "+=0" : "") : F(single.V);
            RefreshToolButtons();
        }
        finally
        {
            _refreshDepth--;
        }
    }

    private void RefreshProblems()
    {
        IReadOnlyList<MotionProblem> problems = _session.Problems;
        ProblemsPanel.Children.Clear();
        ProblemsPanel.IsVisible = problems.Count > 0;

        foreach (MotionProblem problem in problems)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new TextBlock
            {
                Text = (problem.Blocking ? "⚠ " : "△ ") + problem,
                FontSize = 11,
                Foreground = problem.Blocking ? Brushes.OrangeRed : Brushes.Goldenrod,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 700,
            });

            foreach (MotionFix fix in MotionFixes.Suggest(problem))
            {
                string motionName = problem.MotionName;
                MotionFix captured = fix;
                row.Children.Add(SmallButton(MotionFixes.Label(fix), (_, _) => Guard("돕는 손", () =>
                {
                    if (!MotionFixes.Apply(_session.Editor, motionName, captured))
                    {
                        _session.SetStatus($"{MotionFixes.Label(captured)}: 바꿀 것이 없다.");
                    }
                })));
            }

            ProblemsPanel.Children.Add(row);
        }
    }

    /// <summary>테스트가 문제 행의 돕는 손을 누른 것과 같다.</summary>
    internal bool ApplyFix(string motionName, MotionFix fix) => MotionFixes.Apply(_session.Editor, motionName, fix);

    internal int ProblemRowsShown => ProblemsPanel.Children.Count;

    // ── 명령 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 시각·값 칸: 숫자는 절대값, <c>+=</c>·<c>-=</c>·<c>*=</c>·<c>/=</c>는 상대. 선택 키 전부에 적용한다.
    /// 시각의 절대값은 키 하나일 때만(여러 키를 한 시각에 겹칠 수 없다).
    /// </summary>
    /// <summary>값 칸 — 선택 키 전부에 적용(숫자는 절대값, +=·-=·*=·/=는 상대). 시각 칸은 없다(소유자, 2026-10-28): 시각은 끌거나 도프 시트로.</summary>
    private void ApplyValueToSelectedKeys(string text)
    {
        if (_refreshing || _graph.Motion is not { } motion || _graph.Selection.Count == 0 || !RelativeInput.TryParse(text, out Func<float, float> apply))
        {
            return;
        }

        List<KeyRef> keys = _graph.Selection.ToList();
        Guard("키 값", () => _session.Editor.SetKeyValues(motion.Name, keys, k => apply(k.V)));
    }

    private void UpdateKey(KeyRef key, KeyAsset current, float? t = null, float? value = null)
    {
        _session.Editor.UpdateKey(_graph.MotionName!, key.Curve.Node, key.Curve.Kind, key.Curve.Axis, key.Index, new KeyAsset
        {
            T = t ?? current.T,
            V = value ?? current.V,
            InTangent = current.InTangent,
            OutTangent = current.OutTangent,
            Tangent = current.Tangent,
            Broken = current.Broken,
        });
        _graph.Select(new[] { key }, additive: false);
    }

    private void WithMotion(Action<TimelineAsset> action)
    {
        if (_refreshing || _graph.Motion is not { } motion)
        {
            return;
        }

        Guard("모션 편집", () => action(motion));
    }

    private void WithSingleKey(Action<KeyRef, KeyAsset> action)
    {
        if (_refreshing || _graph.Motion is not { } motion || _graph.Selection.Count != 1)
        {
            return;
        }

        KeyRef key = _graph.Selection.First();

        if (key.Resolve(motion) is { } asset)
        {
            Guard("키 편집", () => action(key, asset));
        }
    }

    /// <summary>잘못된 입력·없는 파일은 상태 줄로 — 창이 죽지 않는다.</summary>
    private void Guard(string context, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            _session.SetStatus($"{context} 실패: {exception.Message}");
        }
    }

    // ── 파일 ──────────────────────────────────────────────────────────

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? path = await PickOpenAsync("저작 원본 열기", new FilePickerFileType("모션 저작 원본") { Patterns = new[] { "*" + AuthoringFormat.AuthoringExtension, "*.json" } });

            if (path is not null)
            {
                OpenDocument(path);
            }
        }
        catch (Exception exception)
        {
            _session.SetStatus($"열기 실패: {exception.Message}");
        }
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_session.DocumentPath is null)
            {
                await SaveAsAsync();
                return;
            }

            _session.Save();
        }
        catch (Exception exception)
        {
            _session.SetStatus($"저장 실패: {exception.Message}");
        }
    }

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await SaveAsAsync();
        }
        catch (Exception exception)
        {
            _session.SetStatus($"저장 실패: {exception.Message}");
        }
    }

    private async Task SaveAsAsync()
    {
        string? path = await PickSaveAsync("저작 원본 저장", "motions" + AuthoringFormat.AuthoringExtension,
            new FilePickerFileType("모션 저작 원본") { Patterns = new[] { "*" + AuthoringFormat.AuthoringExtension } });

        if (path is not null)
        {
            _session.Save(path);
            _settings.Touch(path);
            RefreshRecentMenu();
            SaveSettings();
        }
    }

    private async Task<string?> PickOpenAsync(string title, FilePickerFileType type)
    {
        IStorageProvider? storage = GetTopLevel(this)?.StorageProvider;

        if (storage is null || !storage.CanOpen)
        {
            _session.SetStatus("이 환경에서는 파일 선택 창을 열 수 없다.");
            return null;
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { type },
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private async Task<string?> PickSaveAsync(string title, string suggested, FilePickerFileType type)
    {
        IStorageProvider? storage = GetTopLevel(this)?.StorageProvider;

        if (storage is null || !storage.CanSave)
        {
            _session.SetStatus("이 환경에서는 저장 창을 열 수 없다.");
            return null;
        }

        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggested,
            FileTypeChoices = new[] { type },
        });

        return file?.Path.LocalPath;
    }

    // ── 작은 도우미 ───────────────────────────────────────────────────

    private static bool TryFloat(string text, out float value)
        => float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// 글 칸 확정 — Enter와 포커스 잃기. 한 번 확정한 글은 포커스가 떠날 때 다시 확정하지 않고, 들어와서 안 바꾸고 나가면 아무 일도 없다
    /// (같은 편집이 두 번 기록되거나 다시하기가 지워지지 않게). Enter로 확정하면 포커스를 뷰포트로 옮긴다 — 그 뒤 Ctrl+Z는 문서 되돌리기다(마야 채널 박스).
    /// </summary>
    private void OnCommit(TextBox box, Action<string> apply)
    {
        string? committed = null;

        void Commit(bool enter)
        {
            string text = box.Text ?? "";

            if (!enter && text == committed)
            {
                return;
            }

            committed = text;
            apply(text);
        }

        box.GotFocus += (_, _) => committed = box.Text ?? "";
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !box.AcceptsReturn)
            {
                Commit(enter: true);
                e.Handled = true;

                if (ReferenceEquals(TopLevel.GetTopLevel(box), this))
                {
                    _viewport.Focus();
                }
            }
        };
        box.LostFocus += (_, _) => Commit(enter: false);
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeight.SemiBold,
        Margin = new Avalonia.Thickness(0, 6, 0, 0),
    };

    private static Control Labeled(string label, Control control)
    {
        var row = new DockPanel();
        var text = new TextBlock { Text = label, FontSize = 11, Width = 76, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 };
        DockPanel.SetDock(text, Dock.Left);
        row.Children.Add(text);
        row.Children.Add(control);
        return row;
    }

    private static Button ToolButton(string text, EventHandler<RoutedEventArgs> onClick)
    {
        var button = new Button { Content = text, FontSize = 11, Padding = new Avalonia.Thickness(8, 3) };
        button.Click += onClick;
        return button;
    }

    private static Button SmallButton(string text, EventHandler<RoutedEventArgs> onClick)
    {
        var button = new Button { Content = text, FontSize = 10, Padding = new Avalonia.Thickness(6, 2) };
        button.Click += onClick;
        return button;
    }

    private static Control Separator() => new Border
    {
        Width = 1,
        Margin = new Avalonia.Thickness(4, 2),
        Background = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)),
    };
}
