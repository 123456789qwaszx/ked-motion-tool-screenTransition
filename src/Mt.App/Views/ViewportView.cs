using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Graph;
using Mt.Authoring.Model;
using Mt.Authoring.Preview;
using Mt.Authoring.Rig;

namespace Mt.App.Views;

/// <summary>
/// 뷰포트 — 리그 덤프로 세운 트리 위에 초상 한 장을 올리고 모션을 재생한다.
///
/// 포즈는 <see cref="MotionPreview"/>(코어 수학), 자리는 <see cref="PortraitLayout"/>(런타임 사이징)이다 —
/// 여기에는 그리기·시계·입력만 있다. 무대는 기준 해상도 크기이고 컨트롤 안에 레터박스로 맞춘다.
/// 깊이는 mid(DepthScale 1) 고정이고 카메라는 없다 — v1 범위.
///
/// 타임 슬라이더는 따로(<see cref="TimeSliderView"/>)다. 재생 토글은 전역 Alt+`(셸), Home/End 처음/끝.
/// <b>포즈 들고 스크럽</b>(마야 MMB 드래그): <see cref="HoldScrubTo"/>로 시각을 옮겨도 보이는 포즈는 그대로라 S로 그 포즈를 다른 프레임에 키한다.
/// </summary>
public sealed class ViewportView : Control
{
    private const double Pad = 8;

    private static readonly IBrush Background = new SolidColorBrush(Color.FromArgb(255, 16, 16, 22));
    private static readonly IBrush StageBrush = new SolidColorBrush(Color.FromArgb(255, 44, 44, 58));
    private static readonly Pen StagePen = new(new SolidColorBrush(Color.FromArgb(120, 148, 163, 184)), 1);
    private static readonly Pen PlaceholderPen = new(new SolidColorBrush(Color.FromArgb(200, 226, 232, 240)), 1.5, DashStyle.Dash);
    private static readonly IBrush PlaceholderFill = new SolidColorBrush(Color.FromArgb(40, 226, 232, 240));
    private static readonly Pen XAxisPen = new(new SolidColorBrush(Color.FromArgb(230, 248, 113, 113)), 2);
    private static readonly Pen YAxisPen = new(new SolidColorBrush(Color.FromArgb(230, 74, 222, 128)), 2);
    private static readonly IBrush PivotBrush = new SolidColorBrush(Color.FromArgb(240, 250, 204, 21));
    private static readonly Pen GridMinorPen = new(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1);
    private static readonly Pen GridMajorPen = new(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1);
    private static readonly Pen WorldXPen = new(new SolidColorBrush(Color.FromArgb(200, 0xE2, 0x4C, 0x3F)), 1.5);
    private static readonly Pen WorldYPen = new(new SolidColorBrush(Color.FromArgb(200, 0x5E, 0xC6, 0x5A)), 1.5);
    private static readonly Pen ObjectOutlinePen = new(new SolidColorBrush(Color.FromArgb(180, 0x9E, 0xB4, 0xFF)), 1, DashStyle.Dash);
    private static readonly Pen SelectedObjectPen = new(new SolidColorBrush(Color.FromArgb(230, 0xF9, 0xD7, 0x1C)), 1.5);
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromArgb(200, 203, 213, 225));
    private static readonly IBrush GizmoCenterBrush = new SolidColorBrush(Color.FromArgb(230, 250, 204, 21));
    private static readonly IBrush GizmoXBrush = new SolidColorBrush(Color.FromArgb(240, 248, 113, 113));
    private static readonly IBrush GizmoYBrush = new SolidColorBrush(Color.FromArgb(240, 74, 222, 128));
    private static readonly Pen GizmoRingPen = new(new SolidColorBrush(Color.FromArgb(230, 96, 165, 250)), 2);
    private static readonly Pen PendingPen = new(new SolidColorBrush(Color.FromArgb(220, 251, 146, 60)), 1.5, DashStyle.Dash);
    private static readonly IBrush OutsideBrush = new SolidColorBrush(Color.FromArgb(185, 58, 58, 66));
    private static readonly IBrush StageOnlyBrush = new SolidColorBrush(Color.FromRgb(24, 24, 28));

    private readonly PlaybackClock _clock = new();
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _stopwatch = new();

    private MotionEditor? _editor;
    private string? _motionName;
    private RigSchemaFile? _rig;
    private DepthTuningFile? _depth;
    private FocusTuningFile? _focus;
    // 무대 뼈대(PresentataionUIRoot). 리그가 있으면 내장 뼈대를 쓴다 — 카메라·패럴랙스가 이 트리에 들어간다.
    private RigSchemaRigDto? _stage;
    // 씬 전체의 시작 상태(오브젝트마다 슬롯 접두사). 모션은 선택 오브젝트에만 얹는다.
    private RectNodeTree? _template;
    private readonly Dictionary<string, Bitmap?> _portraits = new();

    /// <summary>조각 그림 — (오브젝트 id, 조각 노드 id) → 비트맵. 스킨이 그림을 준 조각만 든다(파일을 못 읽었으면 null — 자리표시).</summary>
    // 스킨의 그림 — 문서 기준 경로 → 비트맵. 어느 스킨의 어느 장을 그릴지는 그릴 때 고른다(입은 스킨 · 이모지 정의의 스킨 · 반전 판 · 그림 넘기기).
    private readonly Dictionary<string, Bitmap?> _images = new(StringComparer.Ordinal);
    private string? _selectedObjectId;
    public bool ShowGrid { get; set; } = true;
    private Vec2 _stageSize = BaseResolutionFile.Default;
    private KeySelection _selection = new();
    private CurveFlags _flags = new();

    /// <summary>뮤트된 성분은 재생에서 뺀다(시작값 그대로). 그래프·도프시트와 같은 객체.</summary>
    public CurveFlags Flags
    {
        get => _flags;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (ReferenceEquals(_flags, value))
            {
                return;
            }

            _flags.Changed -= OnFlagsChanged;
            _flags = value;
            _flags.Changed += OnFlagsChanged;
            InvalidateVisual();
        }
    }

    private void OnFlagsChanged(object? sender, EventArgs e) => InvalidateVisual();

    private enum Drag { None, MoveFree, MoveX, MoveY, Rotate, ScaleFree, ScaleX, ScaleY }

    private Drag _drag = Drag.None;

    private const double GizmoArm = 60;
    private const double GizmoHit = 9;

    // 키로 아직 안 들어간 포즈(마야: 키 없이 바꾼 값). 시각이 바뀌면 버린다.
    private readonly Dictionary<string, RectNodeState> _overrides = new();
    private readonly Dictionary<(string Node, MotionChannelKind Kind), HashSet<Axis>> _pending = new();

    // 들고 있는 포즈(마야 MMB 스크럽). 시각이 바뀌어도 이것을 보여 주고, S가 이것을 키한다.
    private RectNodeTree? _held;
    private bool _holdScrub;

    // 드래그 시작 때의 키 없는 포즈 — 놓을 때 이력에 올릴 차이의 기준.
    private PendingSnapshot? _dragPendingBefore;
    private Vec2 _dragStartRoot;
    private Vec2 _dragPivotRoot;
    private Point _dragStartControl;
    private Vec2 _dragScaleDirection;
    private RectNodeState _dragStartState;

    public ViewportView()
    {
        MinHeight = 120;
        ClipToBounds = true;
        Focusable = true;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => OnTick();
        _selection.Changed += OnSelectionChanged;
        _flags.Changed += OnFlagsChanged;
    }

    public event EventHandler? TimeChanged;

    public event EventHandler? PlayStateChanged;

    /// <summary>재생 범위·모드·속도가 바뀌었다.</summary>
    public event EventHandler? RangeChanged;

    /// <summary>키 없이 바꾼 포즈가 생기거나 사라졌다(S로 키를 놓거나 시각이 바뀌면 사라진다).</summary>
    public event EventHandler? PendingChanged;

    /// <summary>매니퓰레이터·S·Auto Key로 키가 들어갔다.</summary>
    public event EventHandler? PoseKeyed;

    public ManipulatorMode Manipulator { get; set; } = ManipulatorMode.Move;

    // 보기 변환(네비게이션): 무대를 컨트롤에 맞춘 배율 × ViewZoom, 가운데에서 ViewPan만큼. 씬 데이터가 아니라 화면뿐이다.
    private double _viewZoom = 1;
    private Vector _viewPan;
    private bool _panning;
    private Point _panPointerStart;
    private Vector _panStart;

    public const double MinViewZoom = 0.2;
    public const double MaxViewZoom = 10;

    /// <summary>보기 배율 — 1 = 무대가 컨트롤에 맞는다(레터박스). 휠이 커서를 축으로 바꾼다.</summary>
    public double ViewZoom => _viewZoom;

    /// <summary>보기 이동(컨트롤 픽셀) — MMB 드래그.</summary>
    public Vector ViewPan => _viewPan;

    /// <summary>보기 맞춤(F) — 배율 1, 이동 0.</summary>
    public void ResetView()
    {
        _viewZoom = 1;
        _viewPan = default;
        InvalidateVisual();
    }

    /// <summary>커서 아래의 루트 점이 제자리에 있도록 배율을 바꾼다(그래프 편집기 휠과 같은 관습).</summary>
    public void ZoomViewAt(Point anchor, double factor)
    {
        double next = Math.Clamp(_viewZoom * factor, MinViewZoom, MaxViewZoom);

        if (Math.Abs(next - _viewZoom) < 1e-9)
        {
            return;
        }

        Vec2 under = ToRoot(anchor);
        _viewZoom = next;
        Point moved = ToControl(under);
        _viewPan = new Vector(_viewPan.X + anchor.X - moved.X, _viewPan.Y + anchor.Y - moved.Y);
        InvalidateVisual();
    }

    private bool _stageOnly;

    /// <summary>
    /// 무대만 보기 — 무대 밖을 다 가리고 격자를 끈다: 게임에서 보이는 그대로. 화면 밖에서 들어오는 층이 바깥에서 비치지 않는다.
    /// 꺼져 있으면(기본) 무대 밖의 것이 흐리게 보인다 — 어디서 들어오는지 보며 고친다.
    /// </summary>
    public bool StageOnly
    {
        get => _stageOnly;
        set
        {
            _stageOnly = value;
            InvalidateVisual();
        }
    }

    /// <summary>켜져 있으면 매니퓰레이터를 놓는 순간 키가 들어간다. 꺼져 있으면 S를 누를 때까지 "키 없는 포즈"다.</summary>
    public bool AutoKey { get; set; }

    /// <summary>새 키의 탄젠트 모드(설정).</summary>
    public TangentMode DefaultTangent { get; set; } = TangentMode.Auto;

    public bool SnapEnabled { get; set; } = true;

    public double SnapStep { get; set; } = TimeSnap.DefaultStep;

    public bool HasPending => _pending.Count > 0 || _held is not null;

    /// <summary>포즈를 들고 있는가(MMB 스크럽 뒤). S가 모든 채널에 그 포즈를 키한다.</summary>
    public bool IsHoldingPose => _held is not null;

    /// <summary>매니퓰레이터를 둘 수 있는 노드들 — 덤프의 노드 중 __root·키잉 금지 노드를 뺀 것.</summary>
    public IReadOnlyList<string> NodeChoices
    {
        get
        {
            if (_rig is null || SelectedObject is not { } o || !_rig.TryGetRig(o.Rig, out RigSchemaRigDto dto))
            {
                return Array.Empty<string>();
            }

            return dto.nodes.Select(n => n.id).Where(k => k != RigSchemaLoader.RootKey && !MotionRules.ForbiddenNodes.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        }
    }

    // ── 씬·오브젝트 ───────────────────────────────────────────────────

    /// <summary>뷰포트가 보여 주는 씬 — 문서의 활성 씬.</summary>
    public SceneAsset? Scene => _editor is null
        ? null
        : SetScene() is { } set ? set
        : CueScene() is { } cue ? cue
        : _editor.Document.ActiveSceneAsset;

    // ── 화면에 서는 연출을 본다 (PLAN-set.md S5) ──────────────────────

    /// <summary>화면 연출을 볼 때, 씬에 그 세트가 안 놓여 있으면 대신 서는 오브젝트의 id — 문서의 씬에는 없다(보기 전용).</summary>
    public const string CueObjectId = "cue";

    private SceneAsset? _cueScene;
    private (SceneAsset Source, string Rig)? _cueSceneOf;

    /// <summary>
    /// 화면에 서는 연출을 보는 중이고 문서의 씬에 그 세트가 (어디에도 안 붙은 채) 놓여 있지 않으면 — 씬의 사본에 그 세트 하나를 맨 앞 층에 더한 보기 전용 씬.
    /// 화면 연출을 만들자마자 오브젝트를 놓지 않고도 본다.
    /// </summary>
    private SceneAsset? CueScene()
    {
        if (_editor is null || _emojiName is null || _editor.Document.FindEmoji(_emojiName) is not { Screen: true } emoji || _editor.Document.ActiveSceneAsset is not { } scene
            || scene.Objects.Any(o => o.Rig == emoji.Rig && scene.ParentOf(o) is null))
        {
            return null;
        }

        if (_cueScene is null || _cueSceneOf is not { } of || !ReferenceEquals(of.Source, scene) || of.Rig != emoji.Rig || _cueScene.Objects.Count != scene.Objects.Count + 1)
        {
            _cueScene = scene.Clone();
            _cueScene.Objects.Add(new SceneObject { Id = CueObjectId, Rig = emoji.Rig, Layer = SceneLayers.Close });
            _cueSceneOf = (scene, emoji.Rig);
        }

        return _cueScene;
    }

    // ── 세트 보기 (PLAN-set.md S1) ────────────────────────────────────

    /// <summary>세트를 열었을 때 그 세트가 서는 오브젝트의 id — 문서의 씬에는 없다(보기 전용).</summary>
    public const string SetObjectId = "set";

    private string? _setView;
    private string? _setSkin;
    private SceneAsset? _setScene;

    /// <summary>세트 보기에서 입혀 볼 스킨 — 그 세트의 것이 아니거나 null이면 첫 스킨.</summary>
    public string? SetSkin
    {
        get => SetScene()?.Objects[0].Skin;
        set
        {
            _setSkin = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// 배치 모드인가(스파인의 Setup) — 세트를 열었고 고른 애니메이션이 없다. 뷰포트의 끌기가 오브젝트의 **가만히 선 자세**를 바꾼다.
    /// 그 세트의 모션을 고르면 애니메이션 모드다(끌기 = 키).
    /// </summary>
    public bool IsLayoutMode => _setView is not null && Motion is null && ShownEmoji is null;

    /// <summary>
    /// 연 세트(rigKind) — 뷰포트와 하이어아키가 **그 세트만** 보인다: 문서의 씬 대신 그 세트 하나가 무대 가운데에 선 보기 전용 씬.
    /// 그 리그의 모션을 고르면 이 오브젝트가 연기한다. null이면 문서의 씬.
    /// </summary>
    public string? SetView
    {
        get => _setView;
        set
        {
            if (_setView == value)
            {
                return;
            }

            DiscardPending();
            _setView = value;
            _setScene = null;
            _hiddenSetNodes.Clear();
            _soloSetNode = null;
            RebuildTemplate();

            if (value is not null)
            {
                SelectedObjectId = SetObjectId;
            }
            else if (Scene is { } scene && scene.Find(SelectedObjectId ?? "") is null)
            {
                SelectedObjectId = scene.Objects.FirstOrDefault()?.Id;
            }

            InvalidateVisual();
        }
    }

    // 세트 안에서 오브젝트를 하나씩 따로 본다(PLAN-set.md S3) — 보기 상태다: 문서·되돌리기·산출물에 없고 세트를 닫으면 풀린다.
    private readonly HashSet<string> _hiddenSetNodes = new(StringComparer.Ordinal);
    private string? _soloSetNode;

    /// <summary>이것만 본다 — 그 오브젝트와 그 밑의 것만 그려진다. null이면 솔로가 없다.</summary>
    public string? SoloSetNode
    {
        get => _soloSetNode;
        set
        {
            _soloSetNode = value;
            InvalidateVisual();
        }
    }

    /// <summary>그 오브젝트를 숨겼는가(제 것만 — 부모가 숨겨져 안 보이는 것은 <see cref="IsSetNodeShown"/>).</summary>
    public bool IsSetNodeHidden(string node) => _hiddenSetNodes.Contains(node);

    public void SetSetNodeHidden(string node, bool hidden)
    {
        if (hidden ? _hiddenSetNodes.Add(node) : _hiddenSetNodes.Remove(node))
        {
            InvalidateVisual();
        }
    }

    /// <summary>숨김·솔로를 다 푼다.</summary>
    public void ShowAllSetNodes()
    {
        _hiddenSetNodes.Clear();
        _soloSetNode = null;
        InvalidateVisual();
    }

    /// <summary>세트 보기에서 그 오브젝트가 그려지는가 — 저나 조상이 숨겨지지 않았고, 솔로가 있으면 저나 조상이 그 솔로다. 세트 보기가 아니면 늘 참.</summary>
    public bool IsSetNodeShown(string node)
    {
        if (_setView is null || _editor?.Document.FindRig(_setView) is not { } rig || (_hiddenSetNodes.Count == 0 && _soloSetNode is null))
        {
            return true;
        }

        bool underSolo = _soloSetNode is null;

        for (RigNodeAsset? at = rig.Find(node); at is not null; at = at.Parent.Length > 0 ? rig.Find(at.Parent) : null)
        {
            if (_hiddenSetNodes.Contains(at.Id))
            {
                return false;
            }

            underSolo |= at.Id == _soloSetNode;
        }

        return underSolo;
    }

    /// <summary>세트 보기의 씬 — 그 세트 하나. 그 리그의 첫 스킨을 입는다(그림이 보이게 — 스킨을 고르는 것은 배치 모드에서).</summary>
    private SceneAsset? SetScene()
    {
        if (_setView is not { } kind || _editor is null)
        {
            return null;
        }

        string? skin = _editor.Document.FindSkin(_setSkin) is { } chosen && chosen.Rig == kind
            ? chosen.Name
            : _editor.Document.Skins.FirstOrDefault(s => s.Rig == kind)?.Name;

        if (_setScene is null || _setScene.Objects[0].Rig != kind || _setScene.Objects[0].Skin != skin)
        {
            _setScene = new SceneAsset { Name = kind, Objects = { new SceneObject { Id = SetObjectId, Rig = kind, Skin = skin } } };
        }

        return _setScene;
    }

    public IReadOnlyList<SceneObject> SceneObjects => Scene?.Objects ?? (IReadOnlyList<SceneObject>)Array.Empty<SceneObject>();

    /// <summary>선택 오브젝트 — 매니퓰레이터·모션 미리보기·초상 바꾸기의 대상.</summary>
    public string? SelectedObjectId
    {
        get => _selectedObjectId;
        set
        {
            if (_selectedObjectId == value)
            {
                return;
            }

            _selectedObjectId = value;
            DiscardPending();
            ObjectSelected?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    public SceneObject? SelectedObject => _selectedObjectId is null ? null : Scene?.Find(_selectedObjectId);

    /// <summary>포즈를 잡는 오브젝트(선택 오브젝트)의 노드 키 접두사("c1/"). 오브젝트가 없으면 빈 문자열.</summary>
    private string Prefix => SelectedObject?.Prefix ?? "";

    private EmojiPhase? _emojiPhase;

    /// <summary>
    /// 이모지 정의를 보는 중에 고칠 구간(등장·머묾·퇴장) — 끌기·채널 박스·S가 **그 구간의 모션**에 키를 놓는다(<c>PLAN-parts.md</c> §15).
    /// 시각은 수명의 시각 − 구간의 시작(머묾은 지금 바퀴의 처음), 시작값은 그 구간이 얹히기 전의 자세다. null이면 포즈를 잡지 않고 조각을 끌어 놓는다(§14).
    /// </summary>
    public EmojiPhase? EmojiPhaseContext
    {
        get => _emojiPhase;
        set
        {
            if (_emojiPhase == value)
            {
                return;
            }

            DiscardPending();
            _emojiPhase = value;

            if (value is not null && EmojiActor is { } actor)
            {
                SelectedObjectId = actor.Id;
            }

            PendingChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    /// <summary>고치는 구간의 모션과 그 0초가 수명의 몇 초인가 — 지금 시각이 그 구간 안일 때만(밖에서는 그 모션이 안 보인다).</summary>
    private (MotionAsset Motion, double Offset)? EmojiEdit
    {
        get
        {
            if (_editor is null || _emojiPhase is not { } phase || ShownEmoji is not { } emoji || EmojiActor is null
                || _editor.Document.Find(EmojiPreview.MotionNameOf(emoji, phase) ?? "") is not { } motion)
            {
                return null;
            }

            (bool inRange, double offset) = EmojiPreview.PhaseWindow(_editor.Document, emoji, phase, _clock.Time);
            return inRange ? (motion, offset) : null;
        }
    }

    /// <summary>고치는 구간의 모션의 0초가 수명의 몇 초인가 — 그래프·도프 시트의 시각이 이만큼 어긋난다. 구간 밖이어도 낸다.</summary>
    public double EmojiPhaseTimeOffset
        => _editor is not null && _emojiPhase is { } phase && ShownEmoji is { } emoji ? EmojiPreview.PhaseWindow(_editor.Document, emoji, phase, _clock.Time).Offset : 0;

    /// <summary>
    /// 이모지 안에서 고치는 모션의 키가 **시계의** 어디에 놓였는가 — 타임 슬라이더가 틱으로 보인다. 되풀이되는 것(머묾)은 바퀴마다 다시 나온다.
    /// 고치는 것이 없으면 빈 목록.
    /// </summary>
    public IReadOnlyList<double> ContextKeyTimes()
    {
        var times = new List<double>();

        if (_editor is null)
        {
            return times;
        }

        if (_emojiPhase is { } phase && ShownEmoji is { } emoji)
        {
            times.AddRange(PhaseKeyTimes(emoji, phase));
        }

        return times;
    }

    /// <summary>그 구간의 모션의 키가 수명의 어디에 놓였는가 — 머묾은 바퀴마다.</summary>
    private List<double> PhaseKeyTimes(EmojiAsset emoji, EmojiPhase phase)
    {
        var times = new List<double>();

        if (_editor?.Document.Find(EmojiPreview.MotionNameOf(emoji, phase) ?? "") is not { } motion)
        {
            return times;
        }

        EmojiPhases phases = EmojiPreview.PhasesOf(_editor.Document, emoji);
        List<float> keys = KeyTimes.All(motion);

        switch (phase)
        {
            case EmojiPhase.Enter:
                times.AddRange(keys.Select(k => (double)k));
                break;

            case EmojiPhase.Exit:
                times.AddRange(keys.Select(k => phases.ExitStart + (double)k));
                break;

            default:
                for (double from = phases.HoldStart; motion.Duration > 0f && from < phases.ExitStart - 1e-6; from += motion.Duration)
                {
                    times.AddRange(keys.Select(k => from + k).Where(t => t <= phases.ExitStart + 1e-6));
                }

                break;
        }

        return times;
    }

    /// <summary>
    /// 이모지 정의를 보되 고치는 구간이 없을 때 — 세 구간의 모션의 키 전부(구간과 함께). 타임 슬라이더가 구간마다 다른 색으로 보인다.
    /// 구간을 고르면 비고 <see cref="ContextKeyTimes"/>가 그 구간만 낸다.
    /// </summary>
    public IReadOnlyList<(EmojiPhase Phase, double Time)> EmojiKeyTimes()
        => ShownEmoji is { } emoji && _emojiPhase is null
            ? new[] { EmojiPhase.Enter, EmojiPhase.Hold, EmojiPhase.Exit }.SelectMany(p => PhaseKeyTimes(emoji, p).Select(t => (p, t))).ToList()
            : Array.Empty<(EmojiPhase, double)>();

    /// <summary>포즈를 키로 바꿀 모션 — 묶인 모션, 또는 이모지 안에서 고치는 구간의 모션.</summary>
    public MotionAsset? EditMotion => Motion ?? EmojiEdit?.Motion;

    /// <summary>키를 놓는 시각(모션 기준) — 이모지 안에서는 시계에서 구간의 시작을 뺀다.</summary>
    private double EditTime => EmojiEdit is { } emojiEdit ? _clock.Time - emojiEdit.Offset : _clock.Time;

    /// <summary>키 값의 시작값 — 보통은 씬으로 세운 트리.</summary>
    private RectNodeState StartStateOf(string key)
    {
        // 이모지 안에서는 그 구간이 얹히기 전의 자세(자리 · 앞 구간이 남긴 것).
        if (_editor is not null && _template is not null && _emojiPhase is { } phase && ShownEmoji is { } emoji && EmojiActor is { } actor)
        {
            return EmojiPreview.PoseBefore(_template, _editor.Document, emoji, actor, phase, null, EmojiOrigin(emoji, actor, _clock.Time)).GetState(key);
        }

        return _template!.GetState(key);
    }

    /// <summary>선택 노드의 트리 키(접두사 포함).</summary>
    private string? SelectedKey => SelectedNode is null ? null : Prefix + SelectedNode;

    /// <summary>오브젝트를 골랐다.</summary>
    public event EventHandler? ObjectSelected;

    /// <summary>이 오브젝트에 지금 모션을 입혀 볼 수 있나(rigKind가 같거나 채널 노드가 전부 있으면).</summary>
    public bool CanWear(SceneObject o) => _rig is not null && Motion is { } motion && ScenePreview.CanWear(_rig, o, motion);

    /// <summary>오브젝트의 그림(초상) 사각형 — 루트 공간. 그림 노드가 없는 리그면 루트 rect. 트리가 없으면 null.</summary>
    public Quad? ObjectQuad(string objectId)
    {
        RectNodeTree? pose = PoseAt(_clock.Time);
        SceneObject? o = Scene?.Find(objectId);

        if (pose is null || o is null)
        {
            return null;
        }

        return QuadOf(pose, o);
    }

    /// <summary>포즈 위의 오브젝트 사각형(루트 공간) — 초상이 있으면 보이는 그림, 없으면 루트 rect.</summary>
    private Quad? QuadOf(RectNodeTree pose, SceneObject o)
    {
        // 조각으로 그려지는 오브젝트는 첫 조각의 사각형이 고르는 자리다.
        if (DrawnParts(o) is { Count: > 0 } parts && PartQuadOf(pose, o, parts[0]) is { } first)
        {
            return first;
        }

        // 색 오브젝트뿐인 세트 — 첫 색 사각형.
        if (RigParts.ColorsOf(_rig, o.Rig).Keys.FirstOrDefault(n => pose.Contains(o.KeyOf(n))) is { } colored)
        {
            return PortraitLayout.RectQuad(pose, o.KeyOf(colored));
        }

        string? image = ScenePreview.ImageKeyOf(pose, o);
        return image is not null
            ? PortraitLayout.VisualQuad(pose, image, AspectOf(o))
            : pose.Contains(ScenePreview.RootKeyOf(o)) ? PortraitLayout.RectQuad(pose, ScenePreview.RootKeyOf(o)) : null;
    }

    private float AspectOf(SceneObject o)
        => _portraits.TryGetValue(o.Id, out Bitmap? bitmap) && bitmap is { PixelSize: { Width: > 0, Height: > 0 } }
            ? bitmap.PixelSize.Width / (float)bitmap.PixelSize.Height
            : PortraitLayout.PlaceholderAspect;

    private double SnapTime(double seconds) => SnapEnabled ? TimeSnap.Snap(seconds, SnapStep) : seconds;

    /// <summary>지금 보이는 포즈 — 시각의 포즈에 키 없는 포즈를 얹은 것.</summary>
    public RectNodeTree? CurrentPose() => CurrentPose(null);

    /// <param name="alphas">노드 키 → 불투명도(알파 채널이 얹힌 것만 든다, 없는 키는 1). 받은 표에 채워 준다.</param>
    private RectNodeTree? CurrentPose(IDictionary<string, float>? alphas)
    {
        if (_held is not null)
        {
            return _held;
        }

        RectNodeTree? pose = PoseAt(_clock.Time, alphas);

        if (pose is null)
        {
            return null;
        }

        foreach ((string node, RectNodeState state) in _overrides)
        {
            if (pose.Contains(node))
            {
                pose.SetState(node, state);
            }
        }

        // 배치 모드에서 끄는 중 — 문서는 놓을 때 바뀐다. 그 전에는 그림만 따라온다.
        if (_layout is { } layout && pose.Contains(Prefix + layout.Node))
        {
            RectNodeState dragged = pose.GetState(Prefix + layout.Node);
            pose.SetState(Prefix + layout.Node, dragged
                .WithAnchoredPosition(new Vec2(layout.Now.X, layout.Now.Y))
                .WithLocalScale(new Vec3(layout.Now.ScaleX, layout.Now.ScaleY, dragged.LocalScale.Z))
                .WithLocalEuler(new Vec3(0f, 0f, layout.Now.Rotation)));
        }

        return pose;
    }

    /// <summary>매니퓰레이터 중심(컨트롤 좌표). 테스트가 쓴다.</summary>
    public Point? GizmoCenter()
    {
        RectNodeTree? pose = CurrentPose();

        if (pose is null || SelectedKey is not { } key || !pose.Contains(key))
        {
            return null;
        }

        return ToControl(GizmoAnchor(pose, key));
    }

    /// <summary>
    /// 매니퓰레이터가 서는 점(루트 공간) — 노드의 원점. 원점이 오브젝트 그림 밖이면(화면 크기 스트레치 노드의 pivot —
    /// 발 기준 회전의 SwayPivot은 rect 아래 끝이라 무대 밖으로 나간다) 그림 가운데에 둔다. 손잡이는 늘 보이는 곳에 있어야 한다.
    /// </summary>
    private Vec2 GizmoAnchor(RectNodeTree pose, string key)
    {
        Vec2 origin = pose.TransformPoint(key, Vec3.Zero).XY;

        if (SelectedObject is not { } o || QuadOf(pose, o) is not { } quad)
        {
            return origin;
        }

        float minX = Math.Min(Math.Min(quad.BottomLeft.X, quad.BottomRight.X), Math.Min(quad.TopLeft.X, quad.TopRight.X));
        float maxX = Math.Max(Math.Max(quad.BottomLeft.X, quad.BottomRight.X), Math.Max(quad.TopLeft.X, quad.TopRight.X));
        float minY = Math.Min(Math.Min(quad.BottomLeft.Y, quad.BottomRight.Y), Math.Min(quad.TopLeft.Y, quad.TopRight.Y));
        float maxY = Math.Max(Math.Max(quad.BottomLeft.Y, quad.BottomRight.Y), Math.Max(quad.TopLeft.Y, quad.TopRight.Y));
        bool inside = origin.X >= minX && origin.X <= maxX && origin.Y >= minY && origin.Y <= maxY;
        return inside ? origin : quad.Center;
    }

    /// <summary>키 없는 포즈를 버린다(시각이 바뀌거나 Esc·선택 변경·재생). 버린 것도 이력에 올라 Ctrl+Z로 돌아온다.</summary>
    public void DiscardPending() => WithPendingHistory(ClearPending);

    private void ClearPending()
    {
        if (_overrides.Count == 0 && _pending.Count == 0 && _held is null)
        {
            return;
        }

        _overrides.Clear();
        _pending.Clear();
        _held = null;
        _holdScrub = false;
        PendingChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    // ── 키 없는 포즈의 이력 ────────────────────────────────────────────
    //
    // 키 없는 포즈(_overrides·_pending·_held)는 문서 밖이라 스냅샷에 없다. 그래서 조작의 경계(드래그 끝·값 확정·S·Esc·스크럽·선택 변경·재생)마다
    // 앞뒤 상태를 재어 편집기 이력에 손(RecordExternal)으로 올린다 — 문서 조작과 한 줄에서 실제 순서대로 되돌아가고, S로 키한 것을 되돌리면
    // 키 확정 직전의 포즈로 돌아온다(키 칸에 손이 붙는다). 저장 파일·산출물에는 아무것도 안 들어간다. 복원 중의 반응은 편집기가 거른다.

    private sealed record PendingSnapshot(
        IReadOnlyDictionary<string, RectNodeState> Overrides,
        IReadOnlyDictionary<(string Node, MotionChannelKind Kind), IReadOnlyList<Axis>> Pending,
        RectNodeTree? Held,
        bool HoldScrub);

    private PendingSnapshot CapturePending()
        => new(
            new Dictionary<string, RectNodeState>(_overrides),
            _pending.ToDictionary(p => p.Key, p => (IReadOnlyList<Axis>)p.Value.ToList()),
            _held,
            _holdScrub);

    private void ApplyPending(PendingSnapshot snapshot)
    {
        _overrides.Clear();

        foreach ((string key, RectNodeState state) in snapshot.Overrides)
        {
            _overrides[key] = state;
        }

        _pending.Clear();

        foreach (((string Node, MotionChannelKind Kind) key, IReadOnlyList<Axis> axes) in snapshot.Pending)
        {
            _pending[key] = new HashSet<Axis>(axes);
        }

        _held = snapshot.Held;
        _holdScrub = snapshot.HoldScrub;
        PendingChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private static bool SamePending(PendingSnapshot a, PendingSnapshot b)
    {
        if (!ReferenceEquals(a.Held, b.Held) || a.HoldScrub != b.HoldScrub || a.Overrides.Count != b.Overrides.Count || a.Pending.Count != b.Pending.Count)
        {
            return false;
        }

        foreach ((string key, RectNodeState state) in a.Overrides)
        {
            if (!b.Overrides.TryGetValue(key, out RectNodeState other) || !state.Equals(other))
            {
                return false;
            }
        }

        foreach (((string Node, MotionChannelKind Kind) key, IReadOnlyList<Axis> axes) in a.Pending)
        {
            if (!b.Pending.TryGetValue(key, out IReadOnlyList<Axis>? others) || !new HashSet<Axis>(axes).SetEquals(others))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>경계 시작 때의 상태와 지금을 견줘 달라졌으면 이력에 올린다. 묶음 안이면 그 칸에 붙는다.</summary>
    private void RecordPendingChange(PendingSnapshot before)
    {
        if (_editor is null)
        {
            return;
        }

        PendingSnapshot after = CapturePending();

        if (SamePending(before, after))
        {
            return;
        }

        _editor.RecordExternal(() => ApplyPending(before), () => ApplyPending(after));
    }

    private void WithPendingHistory(Action operation)
    {
        PendingSnapshot before = CapturePending();
        operation();
        RecordPendingChange(before);
    }

    /// <summary>조작 하나 = 되돌리기 한 칸: 문서 변화와 키 없는 포즈 변화를 같은 칸에.</summary>
    private void AsOneEdit(Action operation)
    {
        if (_editor is null)
        {
            operation();
            return;
        }

        _editor.Batch(MotionChangeKind.Keys, () => WithPendingHistory(operation));
    }

    /// <summary>
    /// Set Key(S). 키 없는 포즈가 있으면 그것을 키로. 없으면 선택 노드의 있는 채널·성분을 지금 포즈 값으로 키(홀드 키).
    /// </summary>
    public void SetKey()
    {
        MotionAsset? motion = EditMotion;

        // 카메라 모션에는 캐릭터의 포즈를 키하지 않는다 — 카메라 값은 SetCameraValue로.
        if (_editor is null || motion is null || CameraRig.IsCamera(motion))
        {
            return;
        }

        // 조작 하나 — 되돌리기 한 칸(성분·노드가 여럿이어도). 되돌리면 키 확정 직전의 키 없는 포즈로.
        AsOneEdit(() => SetKeyCore(motion));
    }

    private void SetKeyCore(MotionAsset motion)
    {
        if (_held is not null)
        {
            KeyHeldPose(motion);
            return;
        }

        if (_pending.Count > 0)
        {
            foreach (((string node, MotionChannelKind kind), HashSet<Axis> axes) in _pending.ToList())
            {
                KeyNow(node, kind, axes.ToList());
            }

            ClearPending();
            return;
        }

        if (SelectedNode is null)
        {
            return;
        }

        foreach (ChannelAsset channel in motion.Channels.Where(c => c.Node == SelectedNode).ToList())
        {
            var axes = new List<Axis>();

            foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
            {
                if (channel.Get(axis) is not null)
                {
                    axes.Add(axis);
                }
            }

            if (axes.Count > 0)
            {
                KeyNow(channel.Node, channel.Kind, axes);
            }
        }
    }

    /// <summary>들고 있는 포즈를 모든 채널의 있는 성분에 한 조작으로 키(마야: MMB로 옮긴 뒤 S).</summary>
    private void KeyHeldPose(MotionAsset motion)
    {
        RectNodeTree? pose = _held;

        if (_editor is null || _template is null || pose is null)
        {
            ClearPending();
            return;
        }

        var items = new List<(string, MotionChannelKind, IReadOnlyList<(Axis, float)>)>();

        foreach (ChannelAsset channel in motion.Channels)
        {
            string key = Prefix + channel.Node;

            if (!pose.Contains(key))
            {
                continue;
            }

            RectNodeState start = StartStateOf(key);
            RectNodeState posed = pose.GetState(key);
            var values = new List<(Axis, float)>();

            foreach (Axis axis in new[] { Axis.X, Axis.Y, Axis.Z })
            {
                if (channel.Get(axis) is not null)
                {
                    values.Add((axis, PoseEdit.KeyValueFor(channel.Kind, motion.PreviewMode, PoseEdit.Component(channel.Kind, start, axis), PoseEdit.Component(channel.Kind, posed, axis))));
                }
            }

            items.Add((channel.Node, channel.Kind, values));
        }

        float time = (float)Math.Max(0, SnapTime(EditTime));
        ClearPending();
        _editor.KeyPose(motion.Name, time, items, DefaultTangent);
        PoseKeyed?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>
    /// 마야 MMB 스크럽 — 지금 보이는 포즈를 든 채 시각만 옮긴다. 처음 부르면 포즈를 들고, 놓을 때 <see cref="EndHoldScrub"/>.
    /// 든 포즈는 S로 키하거나 Esc·보통 스크럽으로 버린다.
    /// </summary>
    public void HoldScrubTo(double seconds)
    {
        if (_held is null)
        {
            _held = CurrentPose();
            _holdScrub = _held is not null;
            PendingChanged?.Invoke(this, EventArgs.Empty);
        }

        if (_held is null)
        {
            ScrubTo(seconds);
            return;
        }

        Pause();
        double before = _clock.Time;
        _clock.ScrubTo(seconds);

        if (Math.Abs(before - _clock.Time) > 1e-12)
        {
            TimeChanged?.Invoke(this, EventArgs.Empty);
        }

        InvalidateVisual();
    }

    public void EndHoldScrub() => _holdScrub = false;

    /// <summary>지금 포즈의 값으로 키를 놓는다 — 시작값은 덤프(템플릿), 키 값은 모션의 재생 기준(relative/absolute)를 따른다.</summary>
    private void KeyNow(string node, MotionChannelKind kind, IReadOnlyList<Axis> axes)
    {
        MotionAsset? motion = EditMotion;
        RectNodeTree? pose = CurrentPose();

        string key = Prefix + node;

        if (_editor is null || motion is null || CameraRig.IsCamera(motion) || _template is null || pose is null || !pose.Contains(key) || axes.Count == 0)
        {
            return;
        }

        MotionChannelMode mode = motion.PreviewMode;
        RectNodeState start = StartStateOf(key);
        RectNodeState posed = pose.GetState(key);
        var values = new List<(Axis, float)>();

        foreach (Axis axis in axes)
        {
            values.Add((axis, PoseEdit.KeyValueFor(kind, mode, PoseEdit.Component(kind, start, axis), PoseEdit.Component(kind, posed, axis))));
        }

        _overrides.Remove(key);
        _pending.Remove((node, kind));
        _editor.KeyComponents(motion.Name, node, kind, (float)Math.Max(0, SnapTime(EditTime)), values, DefaultTangent);
        PendingChanged?.Invoke(this, EventArgs.Empty);
        PoseKeyed?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>선택 키를 띠에 노랗게 보이려고 든다. 그래프·도프시트와 같은 객체를 꽂는다.</summary>
    public KeySelection Selection
    {
        get => _selection;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (ReferenceEquals(_selection, value))
            {
                return;
            }

            _selection.Changed -= OnSelectionChanged;
            _selection = value;
            _selection.Changed += OnSelectionChanged;
            InvalidateVisual();
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs e) => InvalidateVisual();

    public PlaybackMode Mode
    {
        get => _clock.Mode;
        set
        {
            _clock.Mode = value;
            RangeChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    /// <summary>1프레임 단위 재생 — 보이는 시각이 정수 프레임이다. 꺼지면 연속.</summary>
    public bool PlayWholeFrames
    {
        get => _clock.WholeFrames;
        set
        {
            _clock.WholeFrames = value;
            RangeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double Speed
    {
        get => _clock.Speed;
        set
        {
            _clock.Speed = Math.Clamp(value, 0.05, 8);
            RangeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetRange(double start, double end)
    {
        _clock.SetRange(start, end);
        RangeChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void ResetRange()
    {
        _clock.ResetRange();
        RangeChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>다음 키 시각으로(`.`). 없으면 그대로.</summary>
    public bool JumpToNextKey()
    {
        if (EditMotion is not { } motion)
        {
            return false;
        }

        float? next = KeyTimes.Next(KeyTimes.All(motion), EditTime);

        if (next is null)
        {
            return false;
        }

        Pause();
        ScrubTo(next.Value + (_clock.Time - EditTime));
        return true;
    }

    /// <summary>이전 키 시각으로(`,`). 없으면 그대로.</summary>
    public bool JumpToPreviousKey()
    {
        if (EditMotion is not { } motion)
        {
            return false;
        }

        float? previous = KeyTimes.Previous(KeyTimes.All(motion), EditTime);

        if (previous is null)
        {
            return false;
        }

        Pause();
        ScrubTo(previous.Value + (_clock.Time - EditTime));
        return true;
    }

    public PlaybackClock Clock => _clock;

    public double Time => _clock.Time;

    public bool IsPlaying => _clock.IsPlaying;

    public bool Loop
    {
        get => _clock.Loop;
        set => _clock.Loop = value;
    }

    /// <summary>축을 그릴 노드. null이면 안 그린다.</summary>
    public string? SelectedNode { get; set; }

    public Vec2 StageSize
    {
        get => _stageSize;
        set
        {
            _stageSize = value.X > 0 && value.Y > 0 ? value : BaseResolutionFile.Default;
            InvalidateVisual();
        }
    }

    public string? MotionName => _motionName;

    public MotionAsset? Motion => _editor is not null && _motionName is not null ? _editor.Document.Find(_motionName) : null;

    // ── 이모지 정의의 미리보기(PLAN-parts.md §8) ─────────────────────────

    private string? _emojiName;

    /// <summary>보고 있는 이모지 정의의 이름(<see cref="BindEmoji"/>). 모션을 묶으면 비워진다 — 시간의 주인은 둘 중 하나다.</summary>
    public string? EmojiName => _emojiName;

    public EmojiAsset? ShownEmoji => _editor is not null && _emojiName is not null ? _editor.Document.FindEmoji(_emojiName) : null;

    /// <summary>
    /// 이모지를 연기할 오브젝트 — 고른 오브젝트가 그 리그면 그것, 아니면 씬에서 그 리그의 첫 오브젝트. 없으면 null(미리볼 데가 없다).
    /// </summary>
    public SceneObject? EmojiActor
    {
        get
        {
            if (ShownEmoji is not { } emoji || Scene is not { } scene)
            {
                return null;
            }

            // 화면에 서는 연출은 어디에도 안 붙은 그 세트가, 캐릭터에 붙는 연출은 (고른 것이 그 리그면 그것, 아니면) 그 리그의 첫 오브젝트가 연기한다.
            if (emoji.Screen)
            {
                return scene.Objects.FirstOrDefault(o => o.Rig == emoji.Rig && scene.ParentOf(o) is null);
            }

            return SelectedObject is { } selected && selected.Rig == emoji.Rig ? selected : scene.Objects.FirstOrDefault(o => o.Rig == emoji.Rig);
        }
    }

    /// <summary>
    /// 이모지 정의를 본다 — 시계의 길이는 수명(등장 + 머묾 + 퇴장), 포즈는 <see cref="EmojiPreview.Pose"/>, 그림은 정의의 스킨.
    /// 수명 밖에서는 그 오브젝트를 안 그린다. 포즈는 잡지 않는다(키는 모션에서).
    /// </summary>
    public void BindEmoji(MotionEditor? editor, string? emojiName)
    {
        Bind(editor, null);
        _emojiName = emojiName;
        SyncDuration();
        RebuildTemplate();
        TimeChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>카메라 모션을 보는 중일 때 지금 시각의 카메라 값(zoom·pan). 카메라 모션이 아니면 null.</summary>
    public CameraSettings? CameraValue()
    {
        if (Motion is { } motion && CameraRig.IsCamera(motion) && Scene is { } scene)
        {
            return CameraRig.Pose(scene.Camera, motion, _clock.Time, motion.PreviewMode, _flags.IsMuted);
        }

        return null;
    }

    /// <summary>
    /// 카메라 값을 지금 시각에 키한다(마야 채널 박스에 값을 넣은 것) — 그 성분의 키 하나, 되돌리기 한 칸.
    /// <paramref name="node"/>는 <see cref="CameraRig.PanNode"/>(x·y) 또는 <see cref="CameraRig.ZoomNode"/>(x). 재생 기준이 상대값이면 씬의 카메라와의 차가 키 값이다.
    /// </summary>
    public void SetCameraValue(string node, Axis axis, float value)
    {
        if (_editor is null || Motion is not { } motion || !CameraRig.IsCamera(motion) || Scene is not { } scene)
        {
            return;
        }

        float start = node == CameraRig.ZoomNode ? scene.Camera.Zoom : axis == Axis.X ? scene.Camera.PanX : scene.Camera.PanY;
        float time = (float)Math.Max(0, SnapTime(_clock.Time));
        _editor.KeyComponents(motion.Name, node, MotionChannelKind.Position, time, new[] { (axis, CameraRig.KeyValueFor(motion.PreviewMode, start, value)) }, DefaultTangent);
    }

    public bool HasRig => _template is not null;

    /// <summary>선택 오브젝트의 초상 종횡비(없으면 자리표시).</summary>
    public float Aspect => SelectedObject is { } o ? AspectOf(o) : PortraitLayout.PlaceholderAspect;

    // ── 바인딩 ────────────────────────────────────────────────────────

    public void Bind(MotionEditor? editor, string? motionName)
    {
        if (ReferenceEquals(_editor, editor))
        {
            DiscardPending(); // 같은 문서에서 모션을 바꾸는 것 — 버린 포즈도 이력에
        }
        else
        {
            ClearPending(); // 문서가 갈렸다 — 이전 문서의 포즈·이력은 남지 않는다
        }

        if (_editor is not null)
        {
            _editor.Changed -= OnEditorChanged;
        }

        _editor = editor;
        _motionName = motionName;
        _emojiName = null;

        if (_editor is not null)
        {
            _editor.Changed += OnEditorChanged;
        }

        _clock.Stop();
        _timer.Stop();
        SyncDuration();
        PickSelectedObject();
        RebuildTemplate();
        PlayStateChanged?.Invoke(this, EventArgs.Empty);
        TimeChanged?.Invoke(this, EventArgs.Empty);
        PendingChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void SetRig(RigSchemaFile? rig)
    {
        _rig = rig;
        _stage = rig is null ? null : BuiltinTuning.StageRig;
        PickSelectedObject();
        RebuildTemplate();
        InvalidateVisual();
    }

    /// <summary>카메라가 트리(뼈대의 Zoom/Pan 노드)에 들어가 있나. 뼈대가 없으면 보기 변환으로 입힌다(같은 식).</summary>
    public bool CameraInTree => _stage is not null;

    /// <summary>presets/depth.json — 레이어를 깊이(DepthY·DepthScale)로. null이면 레이어는 그리는 순서만.</summary>
    public void SetDepth(DepthTuningFile? depth)
    {
        _depth = depth;
        RebuildTemplate();
        InvalidateVisual();
    }

    /// <summary>presets/focus.json — focus 오프셋(base + bust)이 깊이·place 폴드에 들어간다.</summary>
    public void SetFocus(FocusTuningFile? focus)
    {
        _focus = focus;
        RebuildTemplate();
        InvalidateVisual();
    }

    /// <summary>씬 카메라(보기 전용) 배율 — 코어 규약 zoom 1당 +0.05.</summary>
    public float CameraScale => ShotIntentMath.EvaluateCameraScale(Scene?.Camera.Zoom ?? 0f);

    private Vec2 CameraPan => Scene is { } scene ? new Vec2(scene.Camera.PanX, scene.Camera.PanY) : Vec2.Zero;

    /// <summary>루트 공간 → 컨트롤 좌표(카메라 적용). 테스트가 쓴다.</summary>
    public Point ToScreen(Vec2 root) => ToControl(root);

    /// <summary>선택 오브젝트의 초상 비트맵(호환용). null이면 자리표시 사각형.</summary>
    public void SetPortrait(Bitmap? portrait)
    {
        if (SelectedObject is { } o)
        {
            SetPortrait(o.Id, portrait);
        }
    }

    /// <summary>오브젝트의 초상 비트맵. 종횡비는 비트맵에서 온다.</summary>
    public void SetPortrait(string objectId, Bitmap? portrait)
    {
        ArgumentNullException.ThrowIfNull(objectId);
        _portraits[objectId] = portrait;
        RebuildTemplate();
        InvalidateVisual();
    }

    /// <summary>스킨의 그림 비트맵 — 문서 기준 경로로. 못 읽었으면 null을 넣는다(자리표시로 그려진다).</summary>
    public void SetImage(string image, Bitmap? bitmap)
    {
        ArgumentNullException.ThrowIfNull(image);
        _images[image] = bitmap;
        InvalidateVisual();
    }

    // ── 이벤트 키로 뜨는 이모지 (PLAN-parts.md §10) ───────────────────

    /// <summary>처음부터 그 시각까지 울린 이벤트 키(연기하는 오브젝트와 함께) — 고른 오브젝트가 연기하는 모션의 것.</summary>
    private List<(double Time, string? ObjectId, string Name, string? Value)> FiredUntil(double seconds)
    {
        if (_editor is null || ShownEmoji is not null)
        {
            return new();
        }

        return Motion is { Events.Count: > 0 } motion && SelectedObject is { } o && CanWear(o)
            ? MotionEvents.Between(motion, double.NegativeInfinity, seconds).Select(e => (Time: (double)e.T, ObjectId: (string?)o.Id, e.Name, e.Value)).ToList()
            : new();
    }

    /// <summary>그 시각에 이벤트 키(<c>emoji</c>)로 떠서 돌고 있는 이모지들 — 캐릭터에 붙은 이모지 리그가 정의의 수명을 돈다.</summary>
    public IReadOnlyList<EmojiPlay> EmojiPlaysAt(double seconds)
        => _editor is not null && Scene is { } scene && FiredUntil(seconds) is { Count: > 0 } fired
            ? EmojiEvents.Resolve(_editor.Document, scene, fired, seconds)
            : Array.Empty<EmojiPlay>();

    /// <summary>붙는 자리(로케이터)가 푼 점 — 가만히 선 무대에서 잰다. 못 풀면 null(오프셋만).</summary>
    private Vec2? EmojiOrigin(EmojiAsset emoji, SceneObject actor, double seconds)
        => _template is { } rest && Scene is { } scene
            ? EmojiPreview.LocatorPoint(rest, scene, emoji, actor, _focus)
            : null;

    private EmojiPlay? EmojiPlayOn(string objectId)
    {
        foreach (EmojiPlay play in EmojiPlaysAt(_clock.Time))
        {
            if (play.Target.Id == objectId)
            {
                return play;
            }
        }

        return null;
    }

    /// <summary>이벤트가 부리는 이모지 오브젝트인데 지금은 안 떠 있다 — 안 그린다.</summary>
    public bool IsDormantEmoji(string objectId)
        => _editor is not null && Scene is { } scene && FiredUntil(double.PositiveInfinity) is { Count: > 0 } all
            && EmojiEvents.Targets(_editor.Document, scene, all).Contains(objectId) && EmojiPlayOn(objectId) is null;

    /// <summary>그 오브젝트가 지금 입은 것 — 스킨, 그림 넘기기의 시각(뜬 뒤 몇 초), 반전 판인가(그림을 뒤집지 않고 그린다).</summary>
    private readonly record struct Worn(SkinAsset Skin, double Seconds, bool Upright);

    /// <summary>
    /// 지금 입은 스킨 — 이모지 정의를 보는 중이거나 이벤트로 떠 있으면 정의의 스킨(오브젝트가 입은 것 대신), 그 캐릭터가 좌우 반전이고 반전 판이 있으면 그 판.
    /// 그림 넘기기의 시각은 이모지면 뜬 뒤의 시간, 아니면 시계의 시각.
    /// </summary>
    private Worn? WornBy(RectNodeTree? pose, SceneObject o)
    {
        if (_editor is null)
        {
            return null;
        }

        EmojiAsset? emoji = ShownEmoji is { } shown && EmojiActor is { } actor && actor.Id == o.Id ? shown : null;
        double seconds = _clock.Time;

        if (emoji is null && EmojiPlayOn(o.Id) is { } play)
        {
            emoji = play.Emoji;
            seconds = play.Local;
        }

        bool upright = false;
        string? name = emoji is null ? o.Skin : emoji.Skin;

        if (emoji?.SkinFlipped is { } flipped && pose is not null && IsMirrored(pose, o))
        {
            name = flipped;
            upright = true;
        }

        return _editor?.Document.FindSkin(name) is { } skin && skin.Rig == o.Rig ? new Worn(skin, seconds, upright) : null;
    }

    /// <summary>그 오브젝트가 좌우로 뒤집혀 서 있는가(부모 캐릭터의 배율 x가 음수 등) — 조각 사각형의 손잡이 방향으로 본다.</summary>
    private bool IsMirrored(RectNodeTree pose, SceneObject o)
    {
        foreach (string node in RigParts.Of(_rig, o.Rig))
        {
            if (pose.Contains(o.KeyOf(node)))
            {
                Quad q = PortraitLayout.RectQuad(pose, o.KeyOf(node));
                Vec2 a = q.BottomRight - q.BottomLeft;
                Vec2 b = q.TopLeft - q.BottomLeft;
                float cross = a.X * b.Y - a.Y * b.X;

                if (MathF.Abs(cross) > 1e-6f)
                {
                    return cross < 0f;
                }
            }
        }

        return false;
    }

    private Bitmap? ImageOf(Worn worn, string node)
        => worn.Skin.ImageAt(node, worn.Seconds) is { } image && _images.TryGetValue(image, out Bitmap? bitmap) ? bitmap : null;

    /// <summary>테스트 손잡이 — 그 조각에 지금 그려지는 그림(문서 기준 경로). 안 그려지면 null.</summary>
    public string? PartImage(string objectId, string node)
        => Scene?.Find(objectId) is { } o && WornBy(CurrentPose(), o) is { } worn ? worn.Skin.ImageAt(node, worn.Seconds) : null;

    /// <summary>
    /// 그 오브젝트에서 그려지는 조각 노드들(그리는 순서) — 리그의 조각 가운데 입은 스킨이 그림을 준 것. 조각이 없는 리그나 스킨을 안 입었으면 빈 목록.
    /// </summary>
    public IReadOnlyList<string> DrawnParts(SceneObject o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return DrawnParts(CurrentPose(), o);
    }

    private IReadOnlyList<string> DrawnParts(RectNodeTree? pose, SceneObject o)
        => WornBy(pose, o) is { } worn ? RigParts.Of(_rig, o.Rig).Where(worn.Skin.Parts.ContainsKey).Where(IsSetNodeShown).ToList() : Array.Empty<string>();

    /// <summary>조각이 지금 그려지는 사각형(루트 공간) — 노드의 rect 안에 그림의 종횡비로 맞추고 스킨의 놓임(자리·크기·회전)을 입힌 것. 없으면 null.</summary>
    public Quad? PartQuad(string objectId, string node)
        => CurrentPose() is { } pose && Scene?.Find(objectId) is { } o ? PartQuadOf(pose, o, node) : null;

    private Quad? PartQuadOf(RectNodeTree pose, SceneObject o, string node)
    {
        string key = o.KeyOf(node);

        if (!pose.Contains(key))
        {
            return null;
        }

        Quad rect = PortraitLayout.RectQuad(pose, key);
        Worn? worn = WornBy(pose, o);
        Bitmap? image = worn is { } w0 ? ImageOf(w0, node) : null;

        float w = Norm(rect.BottomRight - rect.BottomLeft);
        float h = Norm(rect.TopLeft - rect.BottomLeft);

        static float Norm(Vec2 v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);

        if (w <= 1e-6f || h <= 1e-6f)
        {
            return rect;
        }

        Vec2 ux = (rect.BottomRight - rect.BottomLeft) * (1f / w);
        Vec2 uy = (rect.TopLeft - rect.BottomLeft) * (1f / h);
        float fitW = w;
        float fitH = h;

        // 노드 rect 안에 그림을 찌그러뜨리지 않고 넣는다(가운데, contain) — rect의 두 변을 따라 줄인다.
        if (image is { PixelSize: { Width: > 0, Height: > 0 } })
        {
            float aspect = image.PixelSize.Width / (float)image.PixelSize.Height;
            fitW = Math.Min(w, h * aspect);
            fitH = fitW / aspect;
        }

        Vec2 center = rect.BottomLeft + ux * (w / 2f) + uy * (h / 2f);
        float turn = 0f;

        // 스킨의 놓임 — 조각 노드 안에서의 자리(노드의 px), 크기, 회전. 노드의 값(모션) 위에 따로 얹힌다.
        if (worn is { } w1 && PlacementOf(w1.Skin, node) is { IsPlaced: true } setup && pose.GetState(key) is var state)
        {
            Vec2 size = state.SizeDelta;
            float sx = size.X > 1e-6f ? w / size.X : 1f; // 노드의 px → 루트 공간(부모들의 배율까지)
            float sy = size.Y > 1e-6f ? h / size.Y : 1f;
            center = center + ux * (setup.X * sx) + uy * (setup.Y * sy);
            fitW *= setup.Scale;
            fitH *= setup.Scale;
            turn = setup.Rotation * MathF.PI / 180f;
        }

        // 반전 판은 뒤집지 않고 그린다 — 뒤집힌 축(ux)을 되돌린다.
        if (worn is { Upright: true })
        {
            ux = ux * -1f;
        }

        if (turn != 0f)
        {
            // 화면에서 반시계가 +가 되게 — 축의 손잡이 방향을 따른다.
            float handed = ux.X * uy.Y - ux.Y * uy.X < 0f ? -1f : 1f;
            float c = MathF.Cos(turn);
            float s = MathF.Sin(turn) * handed;
            (ux, uy) = (ux * c + uy * s, uy * c - ux * s);
        }

        Vec2 origin = center - ux * (fitW / 2f) - uy * (fitH / 2f);
        return new Quad(origin, origin + ux * fitW, origin + ux * fitW + uy * fitH, origin + uy * fitH);
    }

    public void ClearPortraits()
    {
        _images.Clear();
        _portraits.Clear();
        RebuildTemplate();
        InvalidateVisual();
    }

    private void OnEditorChanged(object? sender, MotionChangedEventArgs e)
    {
        SyncDuration();

        if (e.Kind != MotionChangeKind.Keys)
        {
            PickSelectedObject();
            RebuildTemplate();
        }

        InvalidateVisual();
    }

    /// <summary>선택 오브젝트가 없거나 씬에서 사라졌으면 — 모션을 입을 수 있는 첫 오브젝트, 없으면 첫 오브젝트.</summary>
    private void PickSelectedObject()
    {
        SceneAsset? scene = Scene;

        if (scene is null || scene.Objects.Count == 0)
        {
            SelectedObjectId = null;
            return;
        }

        if (_selectedObjectId is not null && scene.Find(_selectedObjectId) is not null)
        {
            return;
        }

        SceneObject pick = scene.Objects.FirstOrDefault(CanWear) ?? scene.Objects[0];
        SelectedObjectId = pick.Id;
    }

    private void SyncDuration()
    {
        if (Motion is { } motion)
        {
            _clock.SetDuration(motion.Duration);
            RangeChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (_editor is not null && ShownEmoji is { } emoji)
        {
            _clock.SetDuration(EmojiPreview.PhasesOf(_editor.Document, emoji).Total);
            RangeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RebuildTemplate()
    {
        _template = null;
        SceneAsset? scene = Scene;

        if (_rig is null || scene is null)
        {
            return;
        }

        _template = ScenePreview.Build(_rig, scene, AspectOf, null, _depth, _focus, _stage);
    }

    // ── 시계 ──────────────────────────────────────────────────────────

    public void Play()
    {
        _clock.Play();
        _stopwatch.Restart();
        _timer.Start();
        PlayStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        _clock.Pause();
        _timer.Stop();
        PlayStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>거꾸로 재생(마야 ◀).</summary>
    public void PlayBackward()
    {
        _clock.PlayBackward();
        _stopwatch.Restart();
        _timer.Start();
        PlayStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>처음부터 재생(Alt+`) — 범위 시작으로 간 뒤 재생. 이미 재생 중이면 처음으로 되감는다.</summary>
    public void PlayFromStart()
    {
        Stop();
        Play();
    }

    public void TogglePlay()
    {
        if (_clock.IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    public void Stop()
    {
        _clock.Stop();
        _timer.Stop();
        PlayStateChanged?.Invoke(this, EventArgs.Empty);
        TimeChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void ScrubTo(double seconds)
    {
        double before = _clock.Time;
        _clock.ScrubTo(seconds);

        if (Math.Abs(before - _clock.Time) > 1e-12)
        {
            // 포즈 들고 스크럽 중(MMB)이면 든 포즈를 지키고, 보통 스크럽은 키 없는 포즈를 버린다(마야).
            if (!_holdScrub)
            {
                DiscardPending();
            }

            TimeChanged?.Invoke(this, EventArgs.Empty);
        }

        InvalidateVisual();
    }

    /// <summary>시계를 손으로 민다 — 타이머 대신 테스트가 부른다.</summary>
    public void Advance(double seconds)
    {
        if (_clock.Advance(seconds))
        {
            DiscardPending();
            TimeChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        if (!_clock.IsPlaying)
        {
            _timer.Stop();
            PlayStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnTick()
    {
        double elapsed = _stopwatch.Elapsed.TotalSeconds;
        _stopwatch.Restart();
        Advance(elapsed);
    }

    // ── 포즈 조회 (테스트·인스펙터) ───────────────────────────────────

    public RectNodeTree? PoseAt(double seconds) => PoseAt(seconds, null);

    private RectNodeTree? PoseAt(double seconds, IDictionary<string, float>? alphas)
    {
        // 캐릭터들의 포즈 위에, 이벤트 키로 뜬 이모지를 얹는다.
        RectNodeTree? tree = BasePoseAt(seconds, alphas);
        return tree is not null && _editor is not null && EmojiPlaysAt(seconds) is { Count: > 0 } plays
            ? EmojiEvents.Pose(tree, _editor.Document, plays, alphas, play => EmojiOrigin(play.Emoji, play.Target, seconds))
            : tree;
    }

    private RectNodeTree? BasePoseAt(double seconds, IDictionary<string, float>? alphas)
    {
        MotionAsset? motion = Motion;
        if (_template is null)
        {
            return null;
        }

        // 이모지 정의 — 수명 한 번(등장 → 머묾 → 퇴장)을 그 리그의 오브젝트에 얹는다.
        if (_editor is not null && ShownEmoji is { } emoji)
        {
            return EmojiActor is { } actor ? EmojiPreview.Pose(_template, _editor.Document, emoji, actor, seconds, alphas, EmojiOrigin(emoji, actor, seconds)) : _template.Clone();
        }

        // 카메라 모션 — 씬의 카메라 값에 모션을 얹어 무대의 카메라 노드를 다시 세운다. 캐릭터는 시작 상태로 선다.
        if (CameraRig.IsCamera(motion) && Scene is { } cameraScene)
        {
            RectNodeTree tree = _template.Clone();
            StageShotResponse.Apply(tree, CameraRig.Pose(cameraScene.Camera, motion!, seconds, motion!.PreviewMode, _flags.IsMuted));
            return tree;
        }

        return motion is not null && SelectedObject is { } o && CanWear(o)
            ? ScenePreview.Pose(_template, o, motion, seconds, _flags.IsMuted, alphas)
            : _template.Clone();
    }

    // ── 이벤트 키 ─────────────────────────────────────────────────────

    /// <summary>이벤트가 울린 뒤 화면에 남아 있는 시간(초) — 미리보기에서 "여기서 울린다"를 본다.</summary>
    public const double EventFlash = 0.4;

    /// <summary>방금(<see cref="EventFlash"/> 안에) 울린 이벤트 키들("이름 값"). 스크럽해도 그 자리의 것이 보인다.</summary>
    public IReadOnlyList<string> RecentEvents()
    {
        double now = _clock.Time;
        static string Text(string name, string? value) => string.IsNullOrEmpty(value) ? name : $"{name} {value}";

        return Motion is { Events.Count: > 0 } bound
            ? MotionEvents.Between(bound, now - EventFlash, now).Select(e => Text(e.Name, e.Value)).ToList()
            : Array.Empty<string>();
    }

    private void DrawRecentEvents(DrawingContext context, Rect stage)
    {
        IReadOnlyList<string> fired = RecentEvents();

        for (int i = 0; i < fired.Count; i++)
        {
            DrawLabel(context, "⚑ " + fired[i], new Point(stage.X + 8, stage.Y + 24 + i * 16));
        }
    }

    // ── 불투명도(알파 채널, PLAN-parts.md P4) ───────────────────────────

    /// <summary>그리는 중인 프레임의 불투명도 표 — Render가 포즈와 함께 채운다.</summary>
    private readonly Dictionary<string, float> _drawAlphas = new(StringComparer.Ordinal);

    /// <summary>노드에서 그 오브젝트의 루트까지 불투명도를 곱한 값 — CanvasGroup이 겹치는 방식 그대로.</summary>
    private static float EffectiveAlpha(RectNodeTree pose, string key, string prefix, IDictionary<string, float> alphas)
    {
        float alpha = 1f;

        for (string? at = key; at is not null && at.StartsWith(prefix, StringComparison.Ordinal) && pose.Contains(at); at = pose.GetParentKey(at))
        {
            if (alphas.TryGetValue(at, out float own))
            {
                alpha *= own;
            }
        }

        return Math.Clamp(alpha, 0f, 1f);
    }

    /// <summary>지금 시각에 그 노드가 그려지는 불투명도(부모들의 것까지 곱한 값). 오브젝트·노드가 없으면 null.</summary>
    public float? NodeOpacity(string objectId, string node)
    {
        var alphas = new Dictionary<string, float>(StringComparer.Ordinal);

        if (CurrentPose(alphas) is not { } pose || Scene?.Find(objectId) is not { } o || !pose.Contains(o.KeyOf(node)))
        {
            return null;
        }

        return EffectiveAlpha(pose, o.KeyOf(node), o.Prefix, alphas);
    }

    /// <summary>그 노드가 불투명도를 받을 수 있나 — 덤프에 CanvasGroup이 있는 노드만.</summary>
    public bool CanTakeAlpha(string? node)
        => node is not null && _rig is not null && SelectedObject is { } o
           && _rig.AlphaNodeIds.TryGetValue(o.Rig, out ICollection<string>? nodes) && nodes.Contains(node);

    /// <summary>
    /// 고른 노드의 **제** 불투명도(부모 것은 안 곱한다) — 지금 시각. 불투명도를 받을 수 없는 노드거나 키를 놓을 모션이 없으면 null.
    /// </summary>
    public float? AlphaValue(string? node)
    {
        if (!CanTakeAlpha(node) || EditMotion is not { } motion || CameraRig.IsCamera(motion))
        {
            return null;
        }

        var alphas = new Dictionary<string, float>(StringComparer.Ordinal);
        return CurrentPose(alphas) is null ? null : alphas.TryGetValue(Prefix + node, out float alpha) ? alpha : 1f;
    }

    /// <summary>불투명도를 지금 시각에 키한다(값 칸) — 알파 채널의 키 하나, 되돌리기 한 칸. 없던 채널이면 생긴다.</summary>
    public void SetAlphaValue(string node, float value)
    {
        if (_editor is null || !CanTakeAlpha(node) || EditMotion is not { } motion || CameraRig.IsCamera(motion) || _template is null)
        {
            return;
        }

        float start = 1f;

        if (_emojiPhase is { } phase && ShownEmoji is { } emoji && EmojiActor is { } actor)
        {
            var before = new Dictionary<string, float>(StringComparer.Ordinal);
            EmojiPreview.PoseBefore(_template, _editor.Document, emoji, actor, phase, before);
            start = before.TryGetValue(Prefix + node, out float alpha) ? alpha : 1f;
        }

        float key = PoseEdit.KeyValueFor(MotionChannelKind.Alpha, motion.PreviewMode, start, Math.Clamp(value, 0f, 1f));
        _editor.KeyComponents(motion.Name, node, MotionChannelKind.Alpha, (float)Math.Max(0, SnapTime(EditTime)), new[] { (Axis.X, Math.Clamp(key, 0f, 1f)) }, DefaultTangent);
    }

    /// <summary>지금 시각의 초상 사각형(루트 공간). 리그가 없으면 null.</summary>
    public Quad? CurrentPortraitQuad()
    {
        return SelectedObject is { } o ? ObjectQuad(o.Id) : null;
    }

    // ── 좌표 ──────────────────────────────────────────────────────────

    /// <summary>무대가 놓이는 컨트롤 영역(레터박스).</summary>
    public Rect StageRect
    {
        get
        {
            double availableWidth = Math.Max(1, Bounds.Width - Pad * 2);
            double availableHeight = Math.Max(1, Bounds.Height - Pad * 2);
            double scale = Math.Min(availableWidth / _stageSize.X, availableHeight / _stageSize.Y) * _viewZoom;
            double width = _stageSize.X * scale;
            double height = _stageSize.Y * scale;
            double centerX = Bounds.Width / 2 + _viewPan.X;
            double centerY = Bounds.Height / 2 + _viewPan.Y;
            return new Rect(centerX - width / 2, centerY - height / 2, width, height);
        }
    }

    // 카메라(샷): 보이는 좌표 = 루트 × 배율 + pan (코어 ShotIntentMath와 같은 규약). 보기만 바뀐다.
    private Point ToControl(Vec2 root)
    {
        Rect stage = StageRect;
        Vec2 shot = CameraInTree ? root : root * CameraScale + CameraPan;
        Vec2 canvas = PortraitLayout.ToCanvas(shot, _stageSize);
        double scale = stage.Width / _stageSize.X;
        return new Point(stage.X + canvas.X * scale, stage.Y + canvas.Y * scale);
    }

    /// <summary>컨트롤 좌표 → 루트 공간(가운데 원점·y 위). 카메라를 벗긴다.</summary>
    public Vec2 ToRoot(Point control)
    {
        Rect stage = StageRect;
        double scale = stage.Width / _stageSize.X;
        double canvasX = (control.X - stage.X) / scale;
        double canvasY = (control.Y - stage.Y) / scale;
        var shot = new Vec2((float)(canvasX - _stageSize.X / 2), (float)(_stageSize.Y / 2 - canvasY));
        return CameraInTree ? shot : (shot - CameraPan) * (1f / CameraScale);
    }

    // ── 그리기 ────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Background, new Rect(Bounds.Size));
        Rect stage = StageRect;
        context.FillRectangle(StageBrush, stage);
        context.DrawRectangle(StagePen, stage);

        MotionAsset? motion = Motion;

        if (ShowGrid && !StageOnly)
        {
            using (context.PushClip(stage))
            {
                DrawWorld(context, stage);
            }
        }

        if (_template is null)
        {
            DrawLabel(context, _rig is null ? "리그 덤프(rig-schemas.json)를 실으면 오브젝트가 선다." : "씬에 오브젝트가 없다 — 왼쪽 '씬'에서 추가한다.", new Point(stage.X + 10, stage.Y + 10));
            return;
        }

        // 오브젝트는 무대 밖도 그대로 그리고, 카메라에 안 잡히는 바깥을 회색으로 덮어 가른다. 축·손잡이는 그 위에.
        _drawAlphas.Clear();
        RectNodeTree pose = CurrentPose(_drawAlphas) ?? _template;
        DrawObjects(context, pose);
        DrawOutside(context, stage);
        context.DrawRectangle(StagePen, stage);
        DrawRecentEvents(context, stage);

        if (_editor is not null && ShownEmoji is { } previewed)
        {
            EmojiPhases phases = EmojiPreview.PhasesOf(_editor.Document, previewed);
            string where = EmojiActor is { } on ? on.Id : "이 리그의 오브젝트가 씬에 없다 — 캐릭터를 우클릭해 붙는 리그를 만든다";
            string phase = _clock.Time <= phases.HoldStart ? "등장" : _clock.Time <= phases.ExitStart ? "머묾" : "퇴장";
            string editing = "";

            if (_emojiPhase is { } editPhase)
            {
                string name = editPhase switch { EmojiPhase.Enter => "등장", EmojiPhase.Hold => "머묾", _ => "퇴장" };

                // 그 구간 안이면 손잡이가 선다 — 이모지를 보면서 그 구간의 모션에 키를 놓는다.
                if (EmojiEdit is { } edit)
                {
                    DrawAxes(context, pose);
                    DrawGizmo(context, pose);
                    editing = $" — {name}의 모션 {edit.Motion.Name}에 키가 놓인다";
                }
                else
                {
                    editing = $" — {name} 구간 밖이다(그 구간으로 가면 고칠 수 있다)";
                }
            }

            DrawLabel(context, $"연출 {previewed.Name}{(previewed.Screen ? " · 화면" : "")} · {where} · 등장 {phases.Enter:0.##}s › 머묾 {phases.Hold:0.##}s{(phases.Staying ? "(끌 때까지 — 미리보기 길이)" : "")} › 퇴장 {phases.Exit:0.##}s · 지금 {phase}{editing}", new Point(stage.X + 8, stage.Y + 6));
            return;
        }

        if (CameraRig.IsCamera(motion) && CameraValue() is { } shot)
        {
            DrawLabel(context, $"카메라 모션 {motion!.Name} · zoom {shot.Zoom:0.##} · pan {shot.PanX:0.#}, {shot.PanY:0.#}", new Point(stage.X + 8, stage.Y + 6));
            return;
        }

        // 배치 모드 — 키를 놓는 손잡이가 아니라 고른 오브젝트의 사각형과 원점.
        if (IsLayoutMode)
        {
            DrawLayout(context, pose);
            DrawLabel(context, $"세트 {_setView} · 배치 — 끌기: 자리 · Shift+끌기: 배율 · Ctrl+끌기: 회전 (가만히 선 자세다. 애니메이션을 고르면 끌기가 키가 된다)", new Point(stage.X + 8, stage.Y + 6));
            return;
        }

        DrawAxes(context, pose);
        DrawGizmo(context, pose);

        string objectLabel = SelectedObject is { } so ? $"{so.Id} ({so.Rig} · {so.Layer})" : "오브젝트 없음";
        string motionLabel = motion is null
            ? "모션 없음"
            : $"{motion.Name} · {(motion.End == MotionEnd.Restore ? "restore — 끝에서 시작 상태로" : "commit — 마지막 키가 남는다")}"
              + (SelectedObject is { } wearer && !CanWear(wearer) ? " · 이 오브젝트의 리그에는 안 맞는다" : "");
        DrawLabel(context, $"{objectLabel} · {motionLabel}", new Point(stage.X + 8, stage.Y + 6));
    }

    /// <summary>무대 프레임 바깥(카메라가 안 잡는 곳)을 회색으로 덮는다 — 네 띠.</summary>
    private void DrawOutside(DrawingContext context, Rect stage)
    {
        // 무대만 보기 — 바깥을 다 가린다(게임에서 보이는 그대로). 보통은 반투명으로 덮어 무대 밖의 것도 비친다.
        IBrush OutsideBrush = StageOnly ? StageOnlyBrush : ViewportView.OutsideBrush;
        var bounds = new Rect(Bounds.Size);
        double left = Math.Max(bounds.X, stage.X);
        double right = Math.Min(bounds.Right, stage.Right);
        double top = Math.Max(bounds.Y, stage.Y);
        double bottom = Math.Min(bounds.Bottom, stage.Bottom);

        if (right <= left || bottom <= top)
        {
            context.FillRectangle(OutsideBrush, bounds);
            return;
        }

        context.FillRectangle(OutsideBrush, new Rect(bounds.X, bounds.Y, bounds.Width, top - bounds.Y));
        context.FillRectangle(OutsideBrush, new Rect(bounds.X, bottom, bounds.Width, bounds.Bottom - bottom));
        context.FillRectangle(OutsideBrush, new Rect(bounds.X, top, left - bounds.X, bottom - top));
        context.FillRectangle(OutsideBrush, new Rect(right, top, bounds.Right - right, bottom - top));
    }

    /// <summary>월드 축·격자·무대 프레임 — 루트 공간(가운데 원점·y 위, px). 가는 선 1u(기준 폭/48), 굵은 선 5u.</summary>
    private void DrawWorld(DrawingContext context, Rect stage)
    {
        double unit = _stageSize.X / 48.0;
        double scale = stage.Width / _stageSize.X;
        double unitPx = unit * scale;

        if (unitPx >= 4)
        {
            Point origin = ToControl(Vec2.Zero);
            int countX = (int)Math.Ceiling(_stageSize.X / 2 / unit) + 1;
            int countY = (int)Math.Ceiling(_stageSize.Y / 2 / unit) + 1;

            for (int i = -countX; i <= countX; i++)
            {
                double x = origin.X + i * unitPx;
                context.DrawLine(i % 5 == 0 ? GridMajorPen : GridMinorPen, new Point(x, stage.Y), new Point(x, stage.Bottom));
            }

            for (int j = -countY; j <= countY; j++)
            {
                double y = origin.Y - j * unitPx;
                context.DrawLine(j % 5 == 0 ? GridMajorPen : GridMinorPen, new Point(stage.X, y), new Point(stage.Right, y));
            }

            context.DrawLine(WorldXPen, new Point(stage.X, origin.Y), new Point(stage.Right, origin.Y));
            context.DrawLine(WorldYPen, new Point(origin.X, stage.Y), new Point(origin.X, stage.Bottom));
            context.DrawEllipse(null, new Pen(PivotBrush, 1), origin, 5, 5);
            DrawLabel(context, $"0,0 · 1u = {unit:0}px", new Point(origin.X + 6, origin.Y + 4));
        }

        context.DrawRectangle(StagePen, stage);
    }

    private void DrawObjects(DrawingContext context, RectNodeTree pose)
    {
        SceneAsset? scene = Scene;

        if (scene is null)
        {
            return;
        }

        // 이모지 정의를 보는 중이면 그 오브젝트는 수명 안에서만 보인다(뜨기 전·사라진 뒤에는 없다).
        string? emojiActor = _editor is not null && ShownEmoji is { } shownEmoji && !EmojiPreview.IsAlive(_editor.Document, shownEmoji, _clock.Time) ? EmojiActor?.Id : null;

        foreach (SceneObject o in scene.DrawOrder())
        {
            if (!o.Visible || o.Id == emojiActor || IsDormantEmoji(o.Id))
            {
                continue;
            }

            DrawObject(context, pose, o, selected: o.Id == _selectedObjectId);
        }
    }

    /// <summary>사각형 하나에 그림을 그린다 — 비트맵이 없으면 자리표시(X 표 사각형).</summary>
    private void DrawImageQuad(DrawingContext context, Quad quad, Bitmap? image)
    {
        Point topLeft = ToControl(quad.TopLeft);
        Point topRight = ToControl(quad.TopRight);
        Point bottomLeft = ToControl(quad.BottomLeft);
        Point bottomRight = ToControl(quad.BottomRight);

        if (image is null)
        {
            context.DrawGeometry(PlaceholderFill, PlaceholderPen, new PolylineGeometry(new[] { topLeft, topRight, bottomRight, bottomLeft, topLeft }, true));
            context.DrawLine(PlaceholderPen, topLeft, bottomRight);
            context.DrawLine(PlaceholderPen, topRight, bottomLeft);
            return;
        }

        double w = image.PixelSize.Width;
        double h = image.PixelSize.Height;
        var matrix = new Matrix(
            (topRight.X - topLeft.X) / w, (topRight.Y - topLeft.Y) / w,
            (bottomLeft.X - topLeft.X) / h, (bottomLeft.Y - topLeft.Y) / h,
            topLeft.X, topLeft.Y);

        using (context.PushTransform(matrix))
        {
            context.DrawImage(image, new Rect(0, 0, w, h));
        }
    }

    private void DrawObject(DrawingContext context, RectNodeTree pose, SceneObject o, bool selected)
    {
        // 조각·색이 있는 리그(세트) — 리그의 노드 순서로(뒤의 것이 위): 색 오브젝트는 사각형을 채우고, 조각은 스킨이 준 그림을 그린다.
        // 그릴 것이 하나도 없으면(스킨을 안 입었고 색도 없다) 아래의 윤곽으로 떨어진다.
        IReadOnlyDictionary<string, string> colors = RigParts.ColorsOf(_rig, o.Rig);
        IReadOnlyList<string> parts = DrawnParts(pose, o);
        Worn? wornNow = WornBy(pose, o);

        if (_rig is not null && _rig.TryGetRig(o.Rig, out RigSchemaRigDto drawn) && (colors.Count > 0 || (parts.Count > 0 && wornNow is not null)))
        {
            foreach (RigSchemaNodeDto rigNode in drawn.nodes)
            {
                string node = rigNode.id;
                string key = o.KeyOf(node);

                if (!pose.Contains(key))
                {
                    continue;
                }

                float alpha = EffectiveAlpha(pose, key, o.Prefix, _drawAlphas);

                if (alpha <= 0.001f)
                {
                    continue;
                }

                if (colors.TryGetValue(node, out string? fill) && IsSetNodeShown(node) && Color.TryParse(fill, out Color color))
                {
                    Quad rect = PortraitLayout.RectQuad(pose, key);

                    using (context.PushOpacity(alpha))
                    {
                        context.DrawGeometry(new SolidColorBrush(color), null, new PolylineGeometry(
                            new[] { ToControl(rect.TopLeft), ToControl(rect.TopRight), ToControl(rect.BottomRight), ToControl(rect.BottomLeft) }, true));
                    }
                }

                if (wornNow is { } wearing && parts.Contains(node) && PartQuadOf(pose, o, node) is { } part)
                {
                    using (context.PushOpacity(alpha))
                    {
                        DrawImageQuad(context, part, ImageOf(wearing, node));
                    }
                }
            }

            if (wornNow is not { } worn || parts.Count == 0)
            {
                return;
            }

            if (PlacedPart is { } picked && picked.Skin == worn.Skin.Name && PartQuadOf(pose, o, picked.Node) is { } held)
            {
                context.DrawGeometry(null, SelectedObjectPen, new PolylineGeometry(
                    new[] { ToControl(held.TopLeft), ToControl(held.TopRight), ToControl(held.BottomRight), ToControl(held.BottomLeft), ToControl(held.TopLeft) }, true));
            }
            else if (selected && _setView is null && PartQuadOf(pose, o, parts[0]) is { } outline) // 세트 보기에서는 고른 오브젝트만 윤곽이 선다(DrawLayout)
            {
                context.DrawGeometry(null, SelectedObjectPen, new PolylineGeometry(
                    new[] { ToControl(outline.TopLeft), ToControl(outline.TopRight), ToControl(outline.BottomRight), ToControl(outline.BottomLeft), ToControl(outline.TopLeft) }, true));
            }

            return;
        }

        string? imageKey = ScenePreview.ImageKeyOf(pose, o);

        if (imageKey is null)
        {
            // 그림 노드가 없는 리그(배경 등): 루트 rect 윤곽과 이름.
            string rootKey = ScenePreview.RootKeyOf(o);

            if (!pose.Contains(rootKey))
            {
                return;
            }

            Quad root = PortraitLayout.RectQuad(pose, rootKey);
            Point a = ToControl(root.BottomLeft);
            Point b = ToControl(root.BottomRight);
            Point c = ToControl(root.TopRight);
            Point d = ToControl(root.TopLeft);
            context.DrawGeometry(null, selected ? SelectedObjectPen : ObjectOutlinePen, new PolylineGeometry(new[] { a, b, c, d, a }, true));
            DrawLabel(context, $"{o.Id} ({o.Rig})", new Point(Math.Min(a.X, d.X) + 4, Math.Min(c.Y, d.Y) + 4));
            return;
        }

        Quad quad = PortraitLayout.VisualQuad(pose, imageKey, AspectOf(o));
        using IDisposable faded = context.PushOpacity(EffectiveAlpha(pose, imageKey, o.Prefix, _drawAlphas)); // 초상에 알파 모션이 얹혔으면
        Point topLeft = ToControl(quad.TopLeft);
        Point topRight = ToControl(quad.TopRight);
        Point bottomLeft = ToControl(quad.BottomLeft);
        Point bottomRight = ToControl(quad.BottomRight);
        _portraits.TryGetValue(o.Id, out Bitmap? portrait);

        if (portrait is null)
        {
            var outline = new PolylineGeometry(new[] { topLeft, topRight, bottomRight, bottomLeft, topLeft }, true);
            context.DrawGeometry(PlaceholderFill, PlaceholderPen, outline);
            context.DrawLine(PlaceholderPen, topLeft, bottomRight);
            context.DrawLine(PlaceholderPen, topRight, bottomLeft);
        }
        else
        {
            // 이미지 로컬 (0,0)-(w,h) → 화면 사각형 네 점. 직교 투영 아래서 평면 사각형의 변환은 아핀이라 세 점이면 정확하다.
            double w = portrait.PixelSize.Width;
            double h = portrait.PixelSize.Height;
            var matrix = new Matrix(
                (topRight.X - topLeft.X) / w, (topRight.Y - topLeft.Y) / w,
                (bottomLeft.X - topLeft.X) / h, (bottomLeft.Y - topLeft.Y) / h,
                topLeft.X, topLeft.Y);

            using (context.PushTransform(matrix))
            {
                context.DrawImage(portrait, new Rect(0, 0, w, h));
            }
        }

        if (selected)
        {
            context.DrawGeometry(null, SelectedObjectPen, new PolylineGeometry(new[] { topLeft, topRight, bottomRight, bottomLeft, topLeft }, true));
        }

        DrawLabel(context, o.Id, new Point(Math.Min(topLeft.X, bottomLeft.X) + 4, Math.Min(topLeft.Y, topRight.Y) + 4));
    }

    private void DrawAxes(DrawingContext context, RectNodeTree pose)
    {
        if (SelectedKey is not { } key || !pose.Contains(key))
        {
            return;
        }

        (Vec2 origin, Vec2 xEnd, Vec2 yEnd) = PortraitLayout.Axes(pose, key, 80f);
        Point o = ToControl(origin);
        context.DrawLine(XAxisPen, o, ToControl(xEnd));
        context.DrawLine(YAxisPen, o, ToControl(yEnd));
        context.DrawEllipse(PivotBrush, null, o, 4, 4);
        DrawLabel(context, key, new Point(o.X + 6, o.Y + 6));
    }

    private void DrawGizmo(DrawingContext context, RectNodeTree pose)
    {
        if (SelectedKey is not { } gizmoKey || !pose.Contains(gizmoKey) || Manipulator == ManipulatorMode.Select)
        {
            return;
        }

        (Point center, Point xEnd, Point yEnd) = GizmoPoints(pose);

        if (_pending.Count > 0)
        {
            context.DrawEllipse(null, PendingPen, center, GizmoArm + 8, GizmoArm + 8);
        }

        switch (Manipulator)
        {
            case ManipulatorMode.Move:
                // 이동 = 화살표(마야). 가운데 네모는 자유 이동.
                context.DrawLine(XAxisPen, center, xEnd);
                context.DrawLine(YAxisPen, center, yEnd);
                context.DrawGeometry(GizmoXBrush, null, ArrowHead(center, xEnd));
                context.DrawGeometry(GizmoYBrush, null, ArrowHead(center, yEnd));
                context.FillRectangle(GizmoCenterBrush, new Rect(center.X - 6, center.Y - 6, 12, 12));
                break;
            case ManipulatorMode.Scale:
                // 배율 = 끝에 상자(마야). 가운데 상자는 양쪽 같이.
                context.DrawLine(XAxisPen, center, xEnd);
                context.DrawLine(YAxisPen, center, yEnd);
                context.FillRectangle(GizmoXBrush, new Rect(xEnd.X - 5, xEnd.Y - 5, 10, 10));
                context.FillRectangle(GizmoYBrush, new Rect(yEnd.X - 5, yEnd.Y - 5, 10, 10));
                context.FillRectangle(GizmoCenterBrush, new Rect(center.X - 6, center.Y - 6, 12, 12));
                break;
            case ManipulatorMode.Rotate:
                context.DrawEllipse(null, GizmoRingPen, center, GizmoArm, GizmoArm);
                context.DrawEllipse(GizmoCenterBrush, null, center, 4, 4);
                break;
        }
    }

    /// <summary>팔 끝의 화살촉(삼각형) — 길이 14, 폭 10, 끝이 팔 끝에 온다.</summary>
    private static PolylineGeometry ArrowHead(Point from, Point tip)
    {
        double dx = tip.X - from.X;
        double dy = tip.Y - from.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 1e-6)
        {
            dx = 1;
            dy = 0;
            length = 1;
        }

        double ux = dx / length;
        double uy = dy / length;
        var back = new Point(tip.X - ux * 14, tip.Y - uy * 14);
        var left = new Point(back.X - uy * 5, back.Y + ux * 5);
        var right = new Point(back.X + uy * 5, back.Y - ux * 5);
        return new PolylineGeometry(new[] { tip, left, right, tip }, true);
    }

    /// <summary>매니퓰레이터의 중심과 x·y 팔 끝(컨트롤 좌표, 팔 길이는 화면 고정). 팔 방향은 노드의 축, 중심은 <see cref="GizmoAnchor"/>.</summary>
    private (Point Center, Point XEnd, Point YEnd) GizmoPoints(RectNodeTree pose)
    {
        (Vec2 origin, Vec2 xAxis, Vec2 yAxis) = PortraitLayout.Axes(pose, SelectedKey!, 100f);
        Vec2 anchor = GizmoAnchor(pose, SelectedKey!);
        Point center = ToControl(anchor);
        return (center, Along(center, ToControl(anchor + (xAxis - origin))), Along(center, ToControl(anchor + (yAxis - origin))));
    }

    private static Point Along(Point from, Point towards)
    {
        double dx = towards.X - from.X;
        double dy = towards.Y - from.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 1e-6)
        {
            return new Point(from.X + GizmoArm, from.Y);
        }

        return new Point(from.X + dx / length * GizmoArm, from.Y + dy / length * GizmoArm);
    }

    private static void DrawLabel(DrawingContext context, string text, Point at)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 11, LabelBrush);
        context.DrawText(formatted, at);
    }

    // ── 입력 ──────────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        Point position = e.GetPosition(this);

        // MMB·RMB 드래그 = 보기 이동(팬). 씬·키는 안 건드린다. (소유자: 그래프와 같이 둘 다)
        PointerPointProperties pressed = e.GetCurrentPoint(this).Properties;

        if (pressed.IsMiddleButtonPressed || pressed.IsRightButtonPressed)
        {
            _panning = true;
            _panPointerStart = position;
            _panStart = _viewPan;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (!pressed.IsLeftButtonPressed)
        {
            return;
        }

        // 배치 모드 — 오브젝트를 집어 가만히 선 자세를 잡는다(키가 아니다).
        if (IsLayoutMode)
        {
            if (TryStartLayoutDrag(position, e.KeyModifiers))
            {
                e.Pointer.Capture(this);
                e.Handled = true;
            }

            return;
        }

        // 이모지 정의를 보는 중: 구간을 고치는 중이 아니면 조각을 끌어 스킨의 놓임을 잡는다(모션의 키가 아니다).
        if (ShownEmoji is not null && _emojiPhase is null)
        {
            if (TryStartPartPlacement(position, e.KeyModifiers))
            {
                Pause();
                e.Pointer.Capture(this);
                e.Handled = true;
            }

            return;
        }

        // 카메라 모션에는 캐릭터의 포즈를 키하지 않는다.
        bool canPose = ShownEmoji is not null ? EmojiEdit is not null : !CameraRig.IsCamera(Motion);

        if (canPose && TryStartManipulation(position))
        {
            Pause();
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (TryPickObject(position, out string picked))
        {
            SelectedObjectId = picked;
            e.Handled = true;
        }
    }

    /// <summary>클릭 자리의 오브젝트(앞 레이어 우선). 초상 사각형 안이면 맞은 것.</summary>
    private bool TryPickObject(Point position, out string objectId)
    {
        objectId = "";
        RectNodeTree? pose = CurrentPose();
        SceneAsset? scene = Scene;

        if (pose is null || scene is null)
        {
            return false;
        }

        var at = new Pixel(position.X, position.Y);

        foreach (SceneObject o in scene.DrawOrder().Reverse())
        {
            if (!o.Visible)
            {
                continue;
            }

            if (ObjectQuad(o.Id) is not { } quad)
            {
                continue;
            }

            var polygon = new List<Pixel>();

            foreach (Vec2 corner in new[] { quad.BottomLeft, quad.BottomRight, quad.TopRight, quad.TopLeft })
            {
                Point p = ToControl(corner);
                polygon.Add(new Pixel(p.X, p.Y));
            }

            if (GraphHitTest.Contains(polygon, at))
            {
                objectId = o.Id;
                return true;
            }
        }

        return false;
    }

    private bool TryStartManipulation(Point position)
    {
        RectNodeTree? pose = CurrentPose();

        if (Manipulator == ManipulatorMode.Select || SelectedKey is not { } key || pose is null || !pose.Contains(key))
        {
            return false;
        }

        (Point center, Point xEnd, Point yEnd) = GizmoPoints(pose);
        double toCenter = Distance(position, center);
        Drag drag = Drag.None;

        switch (Manipulator)
        {
            case ManipulatorMode.Move:
                drag = Distance(position, xEnd) <= GizmoHit ? Drag.MoveX
                    : Distance(position, yEnd) <= GizmoHit ? Drag.MoveY
                    : toCenter <= GizmoHit + 2 ? Drag.MoveFree : Drag.None;
                break;
            case ManipulatorMode.Scale:
                drag = Distance(position, xEnd) <= GizmoHit ? Drag.ScaleX
                    : Distance(position, yEnd) <= GizmoHit ? Drag.ScaleY
                    : toCenter <= GizmoHit + 2 ? Drag.ScaleFree : Drag.None;
                break;
            case ManipulatorMode.Rotate:
                drag = Math.Abs(toCenter - GizmoArm) <= GizmoHit ? Drag.Rotate : Drag.None;
                break;
        }

        if (drag == Drag.None)
        {
            return false;
        }

        _drag = drag;
        _dragStartRoot = ToRoot(position);
        _dragStartControl = position;
        _dragPivotRoot = GizmoAnchor(pose, key);
        // 배율은 팔 방향(화면)으로 잰다 — 자유(가운데)는 x 팔 방향.
        Point arm = drag == Drag.ScaleY ? yEnd : xEnd;
        _dragScaleDirection = new Vec2((float)(arm.X - center.X), (float)(arm.Y - center.Y));
        _dragStartState = pose.GetState(key);
        _dragPendingBefore = CapturePending();
        return true;
    }

    private void DragManipulation(Point position)
    {
        RectNodeTree? pose = CurrentPose();

        if (SelectedKey is not { } key || pose is null || !pose.Contains(key))
        {
            return;
        }

        Vec2 now = ToRoot(position);
        RectNodeState start = _dragStartState;
        RectNodeState next;

        switch (_drag)
        {
            case Drag.MoveFree:
            case Drag.MoveX:
            case Drag.MoveY:
            {
                Vec2 delta = PoseEdit.RootDeltaToLocal(pose, key, _dragStartRoot, now);
                float dx = _drag == Drag.MoveY ? 0f : delta.X;
                float dy = _drag == Drag.MoveX ? 0f : delta.Y;
                next = start.WithAnchoredPosition(new Vec2(start.AnchoredPosition.X + dx, start.AnchoredPosition.Y + dy));
                break;
            }

            case Drag.Rotate:
            {
                float angle = PoseEdit.AngleDelta(_dragPivotRoot, _dragStartRoot, now);
                Vec3 euler = start.LocalEulerAngles;
                next = start.WithLocalEuler(new Vec3(euler.X, euler.Y, euler.Z + angle));
                break;
            }

            case Drag.ScaleFree:
            case Drag.ScaleX:
            case Drag.ScaleY:
            {
                var fromControl = new Vec2((float)_dragStartControl.X, (float)_dragStartControl.Y);
                var toControl = new Vec2((float)position.X, (float)position.Y);
                float factor = PoseEdit.ScaleAlong(_dragScaleDirection, fromControl, toControl, (float)GizmoArm);
                float sx = _drag == Drag.ScaleY ? start.LocalScale.X : start.LocalScale.X * factor;
                float sy = _drag == Drag.ScaleX ? start.LocalScale.Y : start.LocalScale.Y * factor;
                next = start.WithLocalScale(new Vec3(sx, sy, start.LocalScale.Z));
                break;
            }

            default:
                return;
        }

        _overrides[key] = next;
        InvalidateVisual();
    }

    /// <summary>놓는 순간: Auto Key거나 그 시각에 이미 키가 있으면 키로, 아니면 키 없는 포즈로 남긴다.</summary>
    private void EndManipulation()
    {
        MotionAsset? motion = EditMotion;
        string? node = SelectedNode;

        if (motion is null || node is null || !_overrides.TryGetValue(Prefix + node, out RectNodeState posed))
        {
            return;
        }

        (MotionChannelKind kind, List<Axis> axes) = ChangedAxes(_dragStartState, posed);

        if (axes.Count == 0)
        {
            _overrides.Remove(Prefix + node);
            InvalidateVisual();
            return;
        }

        CommitPose(motion, node, kind, axes);
    }

    /// <summary>바뀐 포즈의 처리: Auto Key거나 그 시각에 이미 키가 있으면 키로, 아니면 키 없는 포즈로 남긴다.</summary>
    private void CommitPose(MotionAsset motion, string node, MotionChannelKind kind, IReadOnlyList<Axis> axes)
    {
        ChannelAsset? channel = motion.FindChannel(node, kind);
        float time = (float)SnapTime(EditTime);
        bool keyExists = channel is not null && axes.Any(a => channel.Get(a)?.Keys.Any(k => Math.Abs(k.T - time) <= MotionRules.Tolerance) == true);

        if (AutoKey || keyExists)
        {
            KeyNow(node, kind, axes);
            return;
        }

        if (!_pending.TryGetValue((node, kind), out HashSet<Axis>? pendingAxes))
        {
            pendingAxes = new HashSet<Axis>();
            _pending[(node, kind)] = pendingAxes;
        }

        pendingAxes.UnionWith(axes);
        PendingChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    // ── 채널 박스(마야) ───────────────────────────────────────────────

    /// <summary>선택 오브젝트의 노드 성분의 지금 포즈 값(키 없는 포즈 포함). 노드·리그가 없으면 null.</summary>
    public float? ComponentValue(string node, MotionChannelKind kind, Axis axis)
    {
        RectNodeTree? pose = CurrentPose();
        string key = Prefix + node;

        if (pose is null || !pose.Contains(key))
        {
            return null;
        }

        return PoseEdit.Component(kind, pose.GetState(key), axis);
    }

    /// <summary>이 성분이 키 없는 포즈로 남아 있는가(채널 박스가 주황으로 보인다).</summary>
    public bool IsPending(string node, MotionChannelKind kind, Axis axis)
        => _pending.TryGetValue((node, kind), out HashSet<Axis>? axes) && axes.Contains(axis);

    /// <summary>
    /// 채널 박스 입력 — 노드 성분 하나를 이 값으로. 매니퓰레이터를 놓은 것과 같다:
    /// Auto Key거나 그 시각에 키가 있으면 키로, 아니면 키 없는 포즈(S로 키·Esc로 버림). 값이 같으면 아무것도 안 한다.
    /// </summary>
    public void SetComponent(string node, MotionChannelKind kind, Axis axis, float value)
        => SetComponents(node, kind, new[] { (axis, value) });

    /// <summary>여러 성분을 한 번에(깊이 컨트롤러의 X·Y 배율처럼). 바뀐 성분만 키·키 없는 포즈가 된다.</summary>
    public void SetComponents(string node, MotionChannelKind kind, IReadOnlyList<(Axis Axis, float Value)> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        MotionAsset? motion = EditMotion;
        RectNodeTree? pose = CurrentPose();
        string key = Prefix + node;

        if (motion is null || CameraRig.IsCamera(motion) || pose is null || !pose.Contains(key) || MotionRules.ForbiddenNodes.Contains(node))
        {
            return;
        }

        RectNodeState state = pose.GetState(key);
        var changed = new List<Axis>();

        foreach ((Axis axis, float value) in values)
        {
            if (Math.Abs(PoseEdit.Component(kind, state, axis) - value) < 1e-6f)
            {
                continue;
            }

            state = PoseEdit.WithComponent(kind, state, axis, value);
            changed.Add(axis);
        }

        if (changed.Count == 0)
        {
            return;
        }

        AsOneEdit(() =>
        {
            _overrides[key] = state;
            InvalidateVisual();
            CommitPose(motion, node, kind, changed);
        });
    }

    /// <summary>채널 박스의 ● — 성분 하나를 지금 포즈 값으로 지금 키(마야 Key Selected). 없는 채널·성분은 생긴다.</summary>
    public void KeyComponent(string node, MotionChannelKind kind, Axis axis) => KeyComponents(node, kind, new[] { axis });

    public void KeyComponents(string node, MotionChannelKind kind, IReadOnlyList<Axis> axes)
    {
        if (MotionRules.ForbiddenNodes.Contains(node) || axes.Count == 0)
        {
            return;
        }

        AsOneEdit(() => KeyNow(node, kind, axes));
    }

    /// <summary>드래그 종류의 채널 종류와, 실제로 값이 바뀐 성분만(마야 Auto Key의 "바뀐 속성만").</summary>
    private (MotionChannelKind Kind, List<Axis> Axes) ChangedAxes(in RectNodeState start, in RectNodeState posed)
    {
        const float epsilon = 1e-4f;
        var axes = new List<Axis>();

        switch (_drag)
        {
            case Drag.MoveFree:
            case Drag.MoveX:
            case Drag.MoveY:
                if (Math.Abs(posed.AnchoredPosition.X - start.AnchoredPosition.X) > epsilon) axes.Add(Axis.X);
                if (Math.Abs(posed.AnchoredPosition.Y - start.AnchoredPosition.Y) > epsilon) axes.Add(Axis.Y);
                return (MotionChannelKind.Position, axes);
            case Drag.Rotate:
                if (Math.Abs(posed.LocalEulerAngles.Z - start.LocalEulerAngles.Z) > epsilon) axes.Add(Axis.Z);
                return (MotionChannelKind.Euler, axes);
            default:
                if (Math.Abs(posed.LocalScale.X - start.LocalScale.X) > epsilon) axes.Add(Axis.X);
                if (Math.Abs(posed.LocalScale.Y - start.LocalScale.Y) > epsilon) axes.Add(Axis.Y);
                return (MotionChannelKind.Scale, axes);
        }
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_panning)
        {
            Point now = e.GetPosition(this);
            _viewPan = new Vector(_panStart.X + now.X - _panPointerStart.X, _panStart.Y + now.Y - _panPointerStart.Y);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_layout is not null)
        {
            DragLayout(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        if (_place is not null)
        {
            DragPartPlacement(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        switch (_drag)
        {
            case Drag.MoveFree:
            case Drag.MoveX:
            case Drag.MoveY:
            case Drag.Rotate:
            case Drag.ScaleFree:
            case Drag.ScaleX:
            case Drag.ScaleY:
                DragManipulation(e.GetPosition(this));
                e.Handled = true;
                break;
        }
    }

    // ── 배치 모드 (PLAN-set.md S2) — 세트의 오브젝트를 끌어 가만히 선 자세(자리 · 배율 · 회전)를 잡는다 ──

    private sealed record LayoutDrag(string Node, PlaceMode Mode, Vec2 StartRoot, Vec2 CenterRoot, RigNodeAsset Start)
    {
        public RigNodeAsset Now { get; set; } = Start;
    }

    private LayoutDrag? _layout;

    /// <summary>배치 모드에서 오브젝트를 집었다 — 하이어아키·칸이 그것을 따라 고른다.</summary>
    public event EventHandler<string>? LayoutNodePicked;

    private static RigNodeAsset CopyOf(RigNodeAsset n) => n.Clone();

    /// <summary>그 자리의 오브젝트(위에 그려진 것 먼저) — 사각형이 있는 것만 집힌다. 없으면, 고른 오브젝트의 원점 가까이를 눌렀을 때 그것(묶기만 하는 오브젝트를 옮기는 길).</summary>
    private string? LayoutNodeAt(RectNodeTree pose, SceneObject o, RigAsset rig, Point position)
    {
        var at = new Pixel(position.X, position.Y);

        // 세운 리그의 순서로(위에 그려진 것 먼저).
        if (_rig is not null && _rig.TryGetRig(o.Rig, out RigSchemaRigDto dto))
        {
            foreach (RigSchemaNodeDto node in Enumerable.Reverse(dto.nodes))
            {
                string key = o.KeyOf(node.id);

                if (node.id == RigSchemaLoader.RootKey || !pose.Contains(key) || node.sizeDelta is null || node.sizeDelta.x <= 0f || node.sizeDelta.y <= 0f
                    || rig.Find(node.id) is null || !IsSetNodeShown(node.id))
                {
                    continue;
                }

                Quad quad = (node.hasImage ? PartQuadOf(pose, o, node.id) : null) ?? PortraitLayout.RectQuad(pose, key);
                var polygon = new[] { quad.BottomLeft, quad.BottomRight, quad.TopRight, quad.TopLeft }.Select(c => ToControl(c)).Select(p => new Pixel(p.X, p.Y)).ToList();

                if (GraphHitTest.Contains(polygon, at))
                {
                    return node.id;
                }
            }
        }

        if (SelectedNode is { } selected && rig.Find(selected) is not null && pose.Contains(o.KeyOf(selected))
            && Distance(position, ToControl(pose.TransformPoint(o.KeyOf(selected), Vec3.Zero).XY)) <= GizmoHit + 6)
        {
            return selected;
        }

        return null;
    }

    /// <summary>끌기 = 자리, Shift+끌기 = 배율(원점에서의 거리 비), Ctrl+끌기 = 회전(원점 둘레).</summary>
    private bool TryStartLayoutDrag(Point position, KeyModifiers modifiers)
    {
        if (_editor?.Document.FindRig(_setView) is not { } rig || SelectedObject is not { } o || CurrentPose() is not { } pose
            || LayoutNodeAt(pose, o, rig, position) is not { } id || rig.Find(id) is not { } node)
        {
            return false;
        }

        SelectedNode = id;
        LayoutNodePicked?.Invoke(this, id);
        PlaceMode mode = modifiers.HasFlag(KeyModifiers.Shift) ? PlaceMode.Scale : modifiers.HasFlag(KeyModifiers.Control) ? PlaceMode.Rotate : PlaceMode.Move;
        _layout = new LayoutDrag(id, mode, ToRoot(position), pose.TransformPoint(o.KeyOf(id), Vec3.Zero).XY, CopyOf(node));
        InvalidateVisual();
        return true;
    }

    private void DragLayout(Point position)
    {
        if (_layout is not { } drag || _template is null || SelectedObject is not { } o)
        {
            return;
        }

        Vec2 now = ToRoot(position);
        RigNodeAsset next = CopyOf(drag.Start);

        static float Length(Vec2 v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);

        switch (drag.Mode)
        {
            case PlaceMode.Move:
            {
                // 루트 공간의 이동을 부모의 좌표로 — 자리는 부모 안에서의 값이다(부모가 돌았거나 커졌어도 끈 만큼 간다).
                string parentKey = string.IsNullOrEmpty(drag.Start.Parent) ? ScenePreview.RootKeyOf(o) : o.KeyOf(drag.Start.Parent);

                if (_template.Contains(parentKey))
                {
                    Vec3 from = _template.InverseTransformPoint(parentKey, new Vec3(drag.StartRoot, 0f));
                    Vec3 to = _template.InverseTransformPoint(parentKey, new Vec3(now, 0f));
                    next.X = MathF.Round(drag.Start.X + to.X - from.X);
                    next.Y = MathF.Round(drag.Start.Y + to.Y - from.Y);
                }

                break;
            }

            case PlaceMode.Scale:
            {
                float from = Length(drag.StartRoot - drag.CenterRoot);
                float to = Length(now - drag.CenterRoot);

                if (from > 1e-3f)
                {
                    float factor = MathF.Max(0.02f, to / from);
                    next.ScaleX = MathF.Round(drag.Start.ScaleX * factor, 2);
                    next.ScaleY = MathF.Round(drag.Start.ScaleY * factor, 2);

                    if (next.ScaleX == 0f || next.ScaleY == 0f)
                    {
                        next.ScaleX = drag.Start.ScaleX;
                        next.ScaleY = drag.Start.ScaleY;
                    }
                }

                break;
            }

            case PlaceMode.Rotate:
            {
                Vec2 a = drag.StartRoot - drag.CenterRoot;
                Vec2 b = now - drag.CenterRoot;

                if (Length(a) > 1e-3f && Length(b) > 1e-3f)
                {
                    float turn = (MathF.Atan2(b.Y, b.X) - MathF.Atan2(a.Y, a.X)) * 180f / MathF.PI;
                    next.Rotation = MathF.Round(drag.Start.Rotation + turn - 360f * MathF.Round(turn / 360f));
                }

                break;
            }
        }

        drag.Now = next;
        InvalidateVisual();
    }

    /// <summary>놓는다 — 끈 하나가 되돌리기 한 칸. 안 움직였으면 기록이 없다(고르기만 한 것).</summary>
    private void EndLayoutDrag()
    {
        if (_layout is not { } drag)
        {
            return;
        }

        _layout = null;

        try
        {
            if (_setView is not null)
            {
                _editor?.UpdateRigNode(_setView, drag.Node, drag.Now);
            }
        }
        finally
        {
            InvalidateVisual();
        }
    }

    /// <summary>루트 공간의 점을 세트의 그 오브젝트(없으면 세트의 맨 위) 안의 좌표로 — 그 밑에 새 오브젝트를 놓을 자리.</summary>
    public Vec2 ToSetLocal(string? parentNode, Vec2 root)
    {
        if (_template is null || SelectedObject is not { } o)
        {
            return root;
        }

        string key = string.IsNullOrEmpty(parentNode) ? ScenePreview.RootKeyOf(o) : o.KeyOf(parentNode);
        return _template.Contains(key) ? _template.InverseTransformPoint(key, new Vec3(root, 0f)).XY : root;
    }

    /// <summary>배치 모드의 표시 — 고른 오브젝트의 사각형과 원점.</summary>
    private void DrawLayout(DrawingContext context, RectNodeTree pose)
    {
        if (SelectedObject is not { } o || SelectedNode is not { } id || !pose.Contains(o.KeyOf(id)))
        {
            return;
        }

        Quad quad = PartQuadOf(pose, o, id) ?? PortraitLayout.RectQuad(pose, o.KeyOf(id));
        context.DrawGeometry(null, SelectedObjectPen, new PolylineGeometry(
            new[] { ToControl(quad.TopLeft), ToControl(quad.TopRight), ToControl(quad.BottomRight), ToControl(quad.BottomLeft), ToControl(quad.TopLeft) }, true));
        Point origin = ToControl(pose.TransformPoint(o.KeyOf(id), Vec3.Zero).XY);
        context.DrawLine(SelectedObjectPen, new Point(origin.X - 7, origin.Y), new Point(origin.X + 7, origin.Y));
        context.DrawLine(SelectedObjectPen, new Point(origin.X, origin.Y - 7), new Point(origin.X, origin.Y + 7));
    }

    /// <summary>세트의 오브젝트들이 화면에 차도록 보기를 맞춘다 — 세트를 열었을 때. 사각형이 하나도 없으면 원점 둘레.</summary>
    public void FitToSet()
    {
        ResetView();

        if (_setView is null || SelectedObject is not { } o || _editor?.Document.FindRig(_setView) is not { } rig || CurrentPose() is not { } pose || Bounds.Width < 1)
        {
            return;
        }

        var points = new List<Vec2>();

        foreach (RigNodeAsset node in rig.Nodes.Where(n => pose.Contains(o.KeyOf(n.Id))))
        {
            Quad q = PortraitLayout.RectQuad(pose, o.KeyOf(node.Id));
            points.AddRange(new[] { q.BottomLeft, q.BottomRight, q.TopLeft, q.TopRight });
        }

        if (points.Count == 0)
        {
            return;
        }

        float minX = points.Min(p => p.X), maxX = points.Max(p => p.X), minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        float width = Math.Max(maxX - minX, 200f);
        float height = Math.Max(maxY - minY, 200f);
        var center = new Vec2((minX + maxX) / 2f, (minY + maxY) / 2f);

        // 세트가 무대의 절반쯤을 차지하게 — 화면 전체짜리 세트는 그대로(배율 1).
        double factor = Math.Clamp(Math.Min(_stageSize.X / (width * 2.0), _stageSize.Y / (height * 2.0)), 1.0, MaxViewZoom);
        ZoomViewAt(ToControl(center), factor);
        Point at = ToControl(center);
        Rect stage = new(Bounds.Size);
        _viewPan = new Vector(_viewPan.X + stage.Width / 2 - at.X, _viewPan.Y + stage.Height / 2 - at.Y);
        InvalidateVisual();
    }

    // ── 조각 놓기 (PLAN-parts.md §14) — 이모지 정의를 보며 조각을 끌어 스킨의 놓임을 잡는다 ──

    private enum PlaceMode { Move, Scale, Rotate }

    private sealed record PartPlacement(string Skin, string Node, PlaceMode Mode, Vec2 StartRoot, Vec2 CenterRoot, SkinPartSetup Start, Vec2 AxisX, Vec2 AxisY)
    {
        public SkinPartSetup Now { get; set; } = Start;
    }

    private PartPlacement? _place;

    /// <summary>마지막으로 집은 조각(스킨, 노드) — 윤곽으로 보인다.</summary>
    public (string Skin, string Node)? PlacedPart { get; private set; }

    /// <summary>끄는 중이면 끌고 있는 값, 아니면 스킨의 값.</summary>
    private SkinPartSetup PlacementOf(SkinAsset skin, string node)
        => _place is { } place && place.Skin == skin.Name && place.Node == node ? place.Now : skin.SetupOf(node);

    /// <summary>
    /// 그 자리의 조각을 집는다 — 끌면 자리, Shift+끌기는 크기(가운데에서의 거리 비), Ctrl+끌기는 회전(가운데 둘레). 위에 그려진 조각이 먼저 집힌다.
    /// </summary>
    private bool TryStartPartPlacement(Point position, KeyModifiers modifiers)
    {
        if (_editor is null || EmojiActor is not { } actor || CurrentPose() is not { } pose || WornBy(pose, actor) is not { } worn)
        {
            return false;
        }

        var at = new Pixel(position.X, position.Y);

        foreach (string node in DrawnParts(pose, actor).Reverse())
        {
            if (PartQuadOf(pose, actor, node) is not { } quad)
            {
                continue;
            }

            var polygon = new[] { quad.BottomLeft, quad.BottomRight, quad.TopRight, quad.TopLeft }.Select(c => ToControl(c)).Select(p => new Pixel(p.X, p.Y)).ToList();

            if (!GraphHitTest.Contains(polygon, at))
            {
                continue;
            }

            // 노드의 px 한 칸이 루트 공간에서 얼마인가 — 놓임은 노드의 px이다(PartQuadOf와 같은 축).
            Quad rect = PortraitLayout.RectQuad(pose, actor.KeyOf(node));
            Vec2 size = pose.GetState(actor.KeyOf(node)).SizeDelta;
            Vec2 axisX = size.X > 1e-6f ? (rect.BottomRight - rect.BottomLeft) * (1f / size.X) : new Vec2(1f, 0f);
            Vec2 axisY = size.Y > 1e-6f ? (rect.TopLeft - rect.BottomLeft) * (1f / size.Y) : new Vec2(0f, 1f);
            PlaceMode mode = modifiers.HasFlag(KeyModifiers.Shift) ? PlaceMode.Scale : modifiers.HasFlag(KeyModifiers.Control) ? PlaceMode.Rotate : PlaceMode.Move;
            _place = new PartPlacement(worn.Skin.Name, node, mode, ToRoot(position), quad.Center, worn.Skin.SetupOf(node).Clone(), axisX, axisY);
            PlacedPart = (worn.Skin.Name, node);
            InvalidateVisual();
            return true;
        }

        PlacedPart = null;
        InvalidateVisual();
        return false;
    }

    private void DragPartPlacement(Point position)
    {
        if (_place is not { } place)
        {
            return;
        }

        Vec2 now = ToRoot(position);
        SkinPartSetup next = place.Start.Clone();

        static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        static float Length(Vec2 v) => MathF.Sqrt(Dot(v, v));

        switch (place.Mode)
        {
            case PlaceMode.Move:
            {
                // 루트 공간의 이동을 노드의 px로 — 축이 직교한다고 본다(리그의 노드는 기울지 않는다).
                Vec2 delta = now - place.StartRoot;
                float lx = Dot(place.AxisX, place.AxisX);
                float ly = Dot(place.AxisY, place.AxisY);
                next.X = MathF.Round(place.Start.X + (lx > 1e-9f ? Dot(delta, place.AxisX) / lx : 0f));
                next.Y = MathF.Round(place.Start.Y + (ly > 1e-9f ? Dot(delta, place.AxisY) / ly : 0f));
                break;
            }

            case PlaceMode.Scale:
            {
                float from = Length(place.StartRoot - place.CenterRoot);
                float to = Length(now - place.CenterRoot);

                if (from > 1e-3f)
                {
                    next.Scale = MathF.Max(0.05f, MathF.Round(place.Start.Scale * to / from, 2));
                }

                break;
            }

            case PlaceMode.Rotate:
            {
                Vec2 a = place.StartRoot - place.CenterRoot;
                Vec2 b = now - place.CenterRoot;

                if (Length(a) > 1e-3f && Length(b) > 1e-3f)
                {
                    // 뒤집힌 계층에서는 화면의 돌림이 거꾸로다 — 축의 손잡이 방향을 따른다.
                    float handed = place.AxisX.X * place.AxisY.Y - place.AxisX.Y * place.AxisY.X < 0f ? -1f : 1f;
                    float turn = (MathF.Atan2(b.Y, b.X) - MathF.Atan2(a.Y, a.X)) * 180f / MathF.PI;
                    turn = (turn - 360f * MathF.Round(turn / 360f)) * handed; // 가까운 쪽으로(−180~180) — 반 바퀴 경계에서 튀지 않게
                    next.Rotation = MathF.Round(place.Start.Rotation + turn);
                }

                break;
            }
        }

        place.Now = next;
        InvalidateVisual();
    }

    /// <summary>놓는다 — 끈 하나가 되돌리기 한 칸. 안 움직였으면 기록이 없다.</summary>
    private void EndPartPlacement()
    {
        if (_place is not { } place)
        {
            return;
        }

        _place = null;

        try
        {
            _editor?.SetSkinPartSetup(place.Skin, place.Node, place.Now);
        }
        finally
        {
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_panning)
        {
            _panning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_layout is not null)
        {
            EndLayoutDrag();
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_place is not null)
        {
            EndPartPlacement();
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_drag != Drag.None)
        {
            Drag drag = _drag;

            if (drag is Drag.MoveFree or Drag.MoveX or Drag.MoveY or Drag.Rotate or Drag.ScaleFree or Drag.ScaleX or Drag.ScaleY)
            {
                // 드래그 하나 = 되돌리기 한 칸: 키가 됐으면 문서 변화, 키 없는 포즈로 남았으면 포즈 변화(드래그 시작 때와 견줘)
                PendingSnapshot before = _dragPendingBefore ?? CapturePending();
                _dragPendingBefore = null;

                if (_editor is null)
                {
                    EndManipulation();
                }
                else
                {
                    _editor.Batch(MotionChangeKind.Keys, () =>
                    {
                        EndManipulation();
                        RecordPendingChange(before);
                    });
                }
            }

            _drag = Drag.None;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    /// <summary>휠 = 커서를 축으로 확대·축소(한 칸 = ×1.15).</summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomViewAt(e.GetPosition(this), Math.Pow(1.15, e.Delta.Y));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.F when e.KeyModifiers == KeyModifiers.None:
                ResetView();
                break;
            case Key.Home:
                Pause();
                ScrubTo(0);
                break;
            case Key.End:
                Pause();
                ScrubTo(_clock.Duration);
                break;
            case Key.Left:
                Pause();
                ScrubTo(_clock.Time - 1.0 / TimeSnap.FramesPerSecond);
                break;
            case Key.Right:
                Pause();
                ScrubTo(_clock.Time + 1.0 / TimeSnap.FramesPerSecond);
                break;
            case Key.OemPeriod:
                JumpToNextKey();
                break;
            case Key.OemComma:
                JumpToPreviousKey();
                break;
            case Key.Q:
                Manipulator = ManipulatorMode.Select;
                InvalidateVisual();
                break;
            case Key.W:
                Manipulator = ManipulatorMode.Move;
                InvalidateVisual();
                break;
            case Key.E:
                Manipulator = ManipulatorMode.Rotate;
                InvalidateVisual();
                break;
            case Key.R:
                Manipulator = ManipulatorMode.Scale;
                InvalidateVisual();
                break;
            case Key.S:
                SetKey();
                break;
            case Key.Escape:
                DiscardPending();
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
