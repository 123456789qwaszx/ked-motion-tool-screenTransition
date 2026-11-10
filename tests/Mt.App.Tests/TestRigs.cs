namespace Mt.App.Tests;

/// <summary>테스트의 발판 — 그 리그의 오브젝트 하나를 장면에 놓는다(하이어아키의 "세트 놓기"와 같은 길). 발판 세트(<see cref="RigTemplates"/>)가 아직 없으면 먼저 정의한다.</summary>
internal static class TestRigs
{
    public static string PlaceRig(this MainWindow window, string rig)
    {
        if (rig == RigTemplates.EmojiKind && window.Session.Rig?.RigKinds.Contains(rig) != true)
        {
            window.Session.Editor.AddRig(RigTemplates.Emoji());
        }

        return window.PlaceSetFromMenu(rig);
    }
}
