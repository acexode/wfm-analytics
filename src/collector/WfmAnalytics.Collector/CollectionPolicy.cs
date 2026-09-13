using System.Text.Json.Serialization;

namespace WfmAnalytics.Collector;

public sealed record SensitiveCaptureSettings(
    [property: JsonPropertyName("window_titles")]
    bool WindowTitles,
    [property: JsonPropertyName("browser_urls")]
    bool BrowserUrls,
    [property: JsonPropertyName("typed_text")]
    bool TypedText,
    [property: JsonPropertyName("screenshots")]
    bool Screenshots,
    [property: JsonPropertyName("clipboard")]
    bool Clipboard,
    [property: JsonPropertyName("full_paths")]
    bool FullPaths)
{
    public static SensitiveCaptureSettings Disabled { get; } = new(false, false, false, false, false, false);

    [JsonIgnore]
    public bool AnyEnabled => WindowTitles || BrowserUrls || TypedText || Screenshots || Clipboard || FullPaths;
}

public sealed record CollectionPolicy(
    [property: JsonPropertyName("policy_version")]
    string PolicyVersion,
    [property: JsonPropertyName("sensitive_capture")]
    SensitiveCaptureSettings SensitiveCapture)
{
    public static CollectionPolicy Minimum(string policyVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyVersion);
        return new CollectionPolicy(policyVersion, SensitiveCaptureSettings.Disabled);
    }
}

public sealed record SensitiveObservation(
    [property: JsonPropertyName("window_title")]
    string? WindowTitle,
    [property: JsonPropertyName("browser_url")]
    string? BrowserUrl,
    [property: JsonPropertyName("typed_text")]
    string? TypedText,
    [property: JsonPropertyName("screenshot_ref")]
    string? ScreenshotRef,
    [property: JsonPropertyName("clipboard_text")]
    string? ClipboardText,
    [property: JsonPropertyName("full_path")]
    string? FullPath)
{
    public static SensitiveObservation Empty { get; } = new(null, null, null, null, null, null);

    public SensitiveObservation ApplyPolicy(SensitiveCaptureSettings settings)
    {
        return new SensitiveObservation(
            settings.WindowTitles ? WindowTitle : null,
            settings.BrowserUrls ? BrowserUrl : null,
            settings.TypedText ? TypedText : null,
            settings.Screenshots ? ScreenshotRef : null,
            settings.Clipboard ? ClipboardText : null,
            settings.FullPaths ? FullPath : null);
    }
}
