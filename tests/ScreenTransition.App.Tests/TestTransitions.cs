namespace ScreenTransition.App.Tests;

/// <summary>테스트의 발판 — 그 전환을 열어 뷰포트에 세운다. 발판 전환(<see cref="TransitionTemplates"/>)이 아직 없으면 먼저 정의한다.</summary>
internal static class TestTransitions
{
    public static void PlaceTransition(this MainWindow window, string asset)
    {
        if (window.Session.Editor.Document.Transitions.All(r => r.Name != asset))
        {
            window.Session.Editor.AddTransition(asset == TransitionTemplates.FigureName ? TransitionTemplates.Figure() : TransitionTemplates.Emoji(asset));
        }

        window.SelectTransition(asset);
    }

    /// <summary>인형 전환 하나(= 타임라인 하나)를 더하고 고른다 — 그 타임라인이 묶인다(배치를 끈 애니메이션 모드).</summary>
    public static void AddFigureTimeline(this MainWindow window, string name, float duration)
    {
        window.Session.Editor.AddTimeline(name, duration, TransitionTemplates.Figure());
        window.SelectTransition(name);
    }
}
