namespace Mt.Authoring;

/// <summary>저작 원본(<c>*.motion-authoring.json</c>)의 스키마 이름 — 이 툴의 것이다. 산출물은 T5에서 새로 선다.</summary>
public static class AuthoringFormat
{
    /// <summary>저작 원본 파일의 스키마. 3: 모션에 끝 규칙(end) · 재생 기준(previewMode)이 없다 — 키는 가만히 선 자세에 대한 값이고 끝에서 마지막 키가 남는다.</summary>
    public const string AuthoringSchema = "motion-authoring/3";

    /// <summary>저작 원본 파일 확장자.</summary>
    public const string AuthoringExtension = ".motion-authoring.json";
}
