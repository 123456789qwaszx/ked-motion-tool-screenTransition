namespace Mt.App.Tests;

/// <summary>테스트의 발판 — 그 세트를 열어 뷰포트에 세운다. 발판 세트(<see cref="RigTemplates"/>)가 아직 없으면 먼저 정의한다.</summary>
internal static class TestRigs
{
    public static void PlaceRig(this MainWindow window, string rig)
    {
        if (window.Session.Editor.Document.Rigs.All(r => r.Kind != rig))
        {
            window.Session.Editor.AddRig(rig == RigTemplates.FigureKind ? RigTemplates.Figure() : RigTemplates.Emoji(rig));
        }

        window.SelectSet(rig);
    }

    /// <summary>인형 세트 하나(= 타임라인 하나)를 더하고 고른다 — 그 타임라인이 묶인다(애니메이션 모드).</summary>
    public static void AddFigureTimeline(this MainWindow window, string name, float duration)
    {
        window.Session.Editor.AddTimeline(name, duration, RigTemplates.Figure());
        window.SelectSet(name);
    }
}
