using Ked.Presentation.Core;
using Mt.Authoring.Editing;
using Mt.Authoring.Model;

namespace Mt.Authoring.Tests;

internal static class Fixtures
{
    /// <summary>
    /// 발판 세트 <c>character</c>(<see cref="RigTemplates.Character"/>) 위의 두 모션.
    /// nod: Track_Move_Y 위치, y 0 → -18 → 0 (restore 0.6s). bow: SwayPivot 회전, z 0 → 8 (commit 0.4s, 미리보기는 absolute).
    /// bow의 x·y는 상수 0이다 — 세 성분이 묶여 있어도 그대로 남는지 본다.
    /// </summary>
    public static MotionDocument NodBowDocument()
    {
        var editor = new MotionEditor(new MotionDocument());
        editor.AddRig(RigTemplates.Character());

        editor.AddMotion("nod", "character", 0.6f);
        editor.AddChannel("nod", "CharacterPortrait_Track_Move_Y", MotionChannelKind.Position);
        editor.SetKeys("nod", "CharacterPortrait_Track_Move_Y", MotionChannelKind.Position, Axis.Y, new[]
        {
            Key(0f, 0f), Key(0.3f, -18f), Key(0.6f, 0f),
        });

        editor.AddMotion("bow", "character", 0.4f);
        editor.AddChannel("bow", "CharacterPortrait_SwayPivot", MotionChannelKind.Euler);
        editor.SetKeys("bow", "CharacterPortrait_SwayPivot", MotionChannelKind.Euler, Axis.X, new[] { Key(0f, 0f), Key(0.4f, 0f) });
        editor.SetKeys("bow", "CharacterPortrait_SwayPivot", MotionChannelKind.Euler, Axis.Y, new[] { Key(0f, 0f), Key(0.4f, 0f) });
        editor.SetKeys("bow", "CharacterPortrait_SwayPivot", MotionChannelKind.Euler, Axis.Z, new[] { Key(0f, 0f), Key(0.4f, 8f) });

        return editor.Document;
    }

    public static KeyAsset Key(float t, float v, float inTangent = 0f, float outTangent = 0f) => new()
    {
        T = t,
        V = v,
        InTangent = inTangent,
        OutTangent = outTangent,
        Tangent = TangentMode.Flat,
    };
}
