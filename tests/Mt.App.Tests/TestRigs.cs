namespace Mt.App.Tests;

/// <summary>테스트의 발판 — 그 세트를 열어 뷰포트에 세운다. 발판 세트(<see cref="RigTemplates"/>)가 아직 없으면 먼저 정의한다.</summary>
internal static class TestRigs
{
    /// <summary>뷰포트가 그 세트를 보이는 오브젝트의 id(<see cref="Views.ViewportView.SetObjectId"/>).</summary>
    public static string PlaceRig(this MainWindow window, string rig)
    {
        if (window.Session.Editor.Document.Rigs.All(r => r.Kind != rig))
        {
            window.Session.Editor.AddRig(rig == RigTemplates.CharacterKind ? RigTemplates.Character() : RigTemplates.Emoji(rig));
        }

        window.SelectSet(rig);
        return Views.ViewportView.SetObjectId;
    }
}
