using ScreenTransition.Core;
using ScreenTransition.Authoring.Editing;
using ScreenTransition.Authoring.Model;

namespace ScreenTransition.Authoring.Tests;

internal static class Fixtures
{
    /// <summary>
    /// 인형 전환 둘(<see cref="TransitionTemplates.Figure"/>) — 각각의 타임라인.
    /// nod: MoverY 위치, y 0 → -18 → 0 (0.6s). bow: Pivot 회전, z 0 → 8 (0.4s).
    /// bow의 x·y는 상수 0이다 — 세 성분이 묶여 있어도 그대로 남는지 본다.
    /// </summary>
    public static MotionDocument NodBowDocument()
    {
        var editor = new MotionEditor(new MotionDocument());

        editor.AddTimeline("nod", 0.6f, TransitionTemplates.Figure());
        editor.AddChannel("nod", "MoverY", MotionChannelKind.Position);
        editor.SetKeys("nod", "MoverY", MotionChannelKind.Position, Axis.Y, new[]
        {
            Key(0f, 0f), Key(0.3f, -18f), Key(0.6f, 0f),
        });

        editor.AddTimeline("bow", 0.4f, TransitionTemplates.Figure());
        editor.AddChannel("bow", "Pivot", MotionChannelKind.Euler);
        editor.SetKeys("bow", "Pivot", MotionChannelKind.Euler, Axis.X, new[] { Key(0f, 0f), Key(0.4f, 0f) });
        editor.SetKeys("bow", "Pivot", MotionChannelKind.Euler, Axis.Y, new[] { Key(0f, 0f), Key(0.4f, 0f) });
        editor.SetKeys("bow", "Pivot", MotionChannelKind.Euler, Axis.Z, new[] { Key(0f, 0f), Key(0.4f, 8f) });

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
