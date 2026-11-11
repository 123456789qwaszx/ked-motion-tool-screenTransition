namespace Mt.Authoring;

/// <summary>저작 원본(<c>*.motion-authoring.json</c>)의 스키마 이름 — 이 툴의 것이다. 산출물은 T5에서 새로 선다.</summary>
public static class AuthoringFormat
{
    /// <summary>저작 원본 파일의 스키마. 2: 채널 mode가 없고 모션에 previewMode가 있다(motions/2와 함께).</summary>
    public const string AuthoringSchema = "motion-authoring/2";

    /// <summary>옛 저작 원본 — 읽을 수 있다. 채널 <c>mode</c>는 버리고 <c>defaultMode</c>는 previewMode로 옮긴다.</summary>
    public const string AuthoringSchemaV1 = "motion-authoring/1";

    /// <summary>저작 원본 파일 확장자.</summary>
    public const string AuthoringExtension = ".motion-authoring.json";
}
