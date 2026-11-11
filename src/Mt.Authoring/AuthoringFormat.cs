namespace Mt.Authoring;

/// <summary>저작 원본(<c>*.transition-authoring.json</c>)의 스키마 이름 — 이 툴의 것이다. 산출물은 T5에서 새로 선다.</summary>
public static class AuthoringFormat
{
    /// <summary>
    /// 저작 원본 파일의 스키마. 1: 전환들(<c>transitions[]</c>) — 전환마다 층들(<c>layers[]</c>)과 타임라인 하나(<c>timeline</c>).
    /// 키는 가만히 선 자세에 대한 값이고 끝에서 마지막 키가 남는다.
    /// </summary>
    public const string AuthoringSchema = "transition-authoring/1";

    /// <summary>저작 원본 파일 확장자.</summary>
    public const string AuthoringExtension = ".transition-authoring.json";
}
