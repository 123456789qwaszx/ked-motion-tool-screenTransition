using ScreenTransition.Authoring;
using ScreenTransition.Authoring.Editing;
using ScreenTransition.Authoring.Model;
using ScreenTransition.Authoring.Preview;
using ScreenTransition.Authoring.Import;
using ScreenTransition.Authoring.Serialization;
using ScreenTransition.Authoring.Validation;

namespace ScreenTransition.App.Services;

/// <summary>
/// 열린 문서 하나의 살림 — 경로·편집기·저장 여부. 화면은 이것을 통해서만 파일에 닿는다.
/// 파일 선택 창은 화면의 일이고, 여기는 경로를 받아 읽고 쓴다(헤드리스 테스트가 그대로 쓴다).
/// </summary>
public sealed class DocumentSession
{
    private long _savedRevision;

    public DocumentSession()
    {
        Editor = new MotionEditor(new MotionDocument());
        Editor.Changed += OnEditorChanged;
        _assetFingerprint = TransitionsKeyOf(Editor.Document);
    }

    // ── 문서 ↔ 실은 그림 ──────────────────────────────────────────────
    //
    // 전환의 모양과 층의 그림은 되돌리기·다시하기로도 바뀐다. 뷰포트의 전환·비트맵이 문서와 어긋나지 않도록 지문을 두고
    // 달라진 때만 AssetsChanged를 올린다. 키 편집(Keys·Metadata)은 보지도 않는다.

    private string _assetFingerprint = "";

    /// <summary>테스트 손잡이 — 문서를 따라 전환·그림을 다시 실은 횟수.</summary>
    internal int AssetSyncCount { get; private set; }

    private void OnEditorChanged(object? sender, MotionChangedEventArgs e)
    {
        if (e.Kind is MotionChangeKind.Keys or MotionChangeKind.Metadata)
        {
            return;
        }

        string fingerprint = TransitionsKeyOf(Editor.Document);

        if (fingerprint == _assetFingerprint)
        {
            return;
        }

        _assetFingerprint = fingerprint;
        AssetSyncCount++;
        AssetsChanged?.Invoke(this, EventArgs.Empty);
    }

    public MotionEditor Editor { get; private set; }

    public string? DocumentPath { get; private set; }

    private static string TransitionsKeyOf(MotionDocument document)
        => string.Join("|", document.Transitions.Select(r => r.Name + ":" + string.Join(",", r.Layers.Select(n => $"{n.Id}<{n.Parent}@{n.X},{n.Y}#{n.Width}x{n.Height}r{n.Rotation}s{n.ScaleX},{n.ScaleY}c{n.Color}i{n.Image}{(n.Alpha ? "a" : "")}"))));

    public string Status { get; private set; } = "";

    /// <summary>문서의 층들이 든 그림 전부 — 문서 기준 경로 → 파일 경로(파일이 없으면 null: 자리표시로 그린다).</summary>
    public IReadOnlyList<(string Image, string? Path)> LayerImages()
        => Editor.Document.Transitions.SelectMany(r => r.Layers).Select(n => n.Image).OfType<string>().Distinct(StringComparer.Ordinal).Select(image =>
        {
            string resolved = ResolveFromDocument(image);
            return (image, File.Exists(resolved) ? resolved : (string?)null);
        }).ToList();

    /// <summary>
    /// 층 표(<c>layers.tsv</c>)를 전환으로 들인다 — 표 옆의 그림들이 그림 층 여럿으로, 한 칸에. 그림 경로는 문서 기준으로 적힌다.
    /// </summary>
    public IReadOnlyList<string> ImportLayers(string transition, string sheetPath)
    {
        ArgumentNullException.ThrowIfNull(sheetPath);
        LayerSheet sheet = LayerSheet.Load(sheetPath);
        string folder = Path.GetDirectoryName(Path.GetFullPath(sheetPath)) ?? "";
        // 표의 파일 하나를 문서 기준 경로로 바꿔 그 폴더를 얻는다(저장 전의 문서면 절대 경로로 남는다).
        string probe = RelativeToDocument(Path.Combine(folder, "x")).Replace('\\', '/');
        string relative = probe.Length > 1 ? probe[..^2] : "";
        return Editor.ImportLayers(transition, sheet, relative, StageTree.Stage);
    }


    public bool IsDirty => Editor.Revision != _savedRevision;

    /// <summary>문서·편집기가 통째로 갈렸다(새로/열기). 화면은 편집기를 다시 묶는다.</summary>
    public event EventHandler? DocumentReplaced;

    public event EventHandler? StatusChanged;

    /// <summary>전환의 모양이나 층의 그림이 바뀌었다. 뷰포트가 다시 싣는다.</summary>
    public event EventHandler? AssetsChanged;

    /// <summary>문서의 문제 — 전환마다 그 층들로 검증한다.</summary>
    public IReadOnlyList<MotionProblem> Problems => MotionValidator.Validate(Editor.Document);

    public void SetStatus(string status)
    {
        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void New()
    {
        Replace(new MotionDocument(), null);
        SetStatus("새 문서.");
    }

    public void Open(string path)
    {
        MotionDocument document = MotionDocumentJson.Read(File.ReadAllText(path));
        Replace(document, path);
        AssetsChanged?.Invoke(this, EventArgs.Empty);
        SetStatus($"열었다: {path}");
    }

    /// <summary>자동 저장 — 문서 옆 <c>*.autosave.transition-authoring.json</c>. 저장 여부(IsDirty)는 건드리지 않는다. 경로가 없으면 안 한다.</summary>
    public string? AutoSave()
    {
        if (DocumentPath is null || !IsDirty)
        {
            return null;
        }

        string path = AutoSavePathFor(DocumentPath);
        File.WriteAllText(path, MotionDocumentJson.Write(Editor.Document));
        SetStatus($"자동 저장: {Path.GetFileName(path)}");
        return path;
    }

    public static string AutoSavePathFor(string documentPath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(documentPath)) ?? ".";
        string name = Path.GetFileName(documentPath);
        string stem = name.EndsWith(AuthoringFormat.AuthoringExtension, StringComparison.OrdinalIgnoreCase)
            ? name[..^AuthoringFormat.AuthoringExtension.Length]
            : Path.GetFileNameWithoutExtension(name);
        return Path.Combine(directory, stem + ".autosave" + AuthoringFormat.AuthoringExtension);
    }

    public void Save(string? path = null)
    {
        path ??= DocumentPath ?? throw new InvalidOperationException("저장할 경로가 없다 — 다른 이름으로 저장을 쓴다.");
        File.WriteAllText(path, MotionDocumentJson.Write(Editor.Document));
        DocumentPath = path;
        _savedRevision = Editor.Revision;
        SetStatus($"저장했다: {path}");
    }

    private void Replace(MotionDocument document, string? path)
    {
        Editor.Changed -= OnEditorChanged;
        Editor = new MotionEditor(document);
        Editor.Changed += OnEditorChanged;
        DocumentPath = path;
        _savedRevision = Editor.Revision;
        _assetFingerprint = TransitionsKeyOf(Editor.Document);
        DocumentReplaced?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>파일 경로 → 문서 기준 상대 경로(저장 전의 문서면 절대 경로 그대로).</summary>
    public string RelativeToDocument(string path)
    {
        string full = Path.GetFullPath(path);

        if (DocumentPath is null)
        {
            return full.Replace('\\', '/');
        }

        string documentDirectory = Path.GetDirectoryName(Path.GetFullPath(DocumentPath)) ?? ".";
        return Path.GetRelativePath(documentDirectory, full).Replace('\\', '/');
    }

    private string ResolveFromDocument(string path)
    {
        if (Path.IsPathRooted(path) || DocumentPath is null)
        {
            return Path.GetFullPath(path);
        }

        string documentDirectory = Path.GetDirectoryName(Path.GetFullPath(DocumentPath)) ?? ".";
        return Path.GetFullPath(Path.Combine(documentDirectory, path));
    }
}
