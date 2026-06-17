namespace Wps.Watch.AiReporting.Chat;

/// <summary>
/// Configuration for the "Ask AI Reports" chat endpoint, which calls the Claude
/// Messages API to translate plain-English questions into SQL. Bound from the
/// <c>Anthropic</c> section of appsettings.
///
/// The API key is intentionally NOT committed. Supply it locally via user-secrets
/// (<c>dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..."</c>) or the
/// <c>Anthropic__ApiKey</c> environment variable. When the key is absent the
/// endpoint returns a structured "not configured" response rather than failing.
/// </summary>
public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    /// <summary>Anthropic API key. Empty disables the chat endpoint (503).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Model id. Default is the most capable Opus tier; switch to
    /// <c>claude-sonnet-4-6</c> or <c>claude-haiku-4-5</c> to trade quality for cost.</summary>
    public string Model { get; set; } = "claude-opus-4-8";

    /// <summary>Anthropic API base URL (no trailing slash).</summary>
    public string BaseUrl { get; set; } = "https://api.anthropic.com";

    /// <summary>Per-response output token ceiling (kept under the SDK's non-streaming timeout band).</summary>
    public int MaxTokens { get; set; } = 8192;

    /// <summary>Safety stop on the agentic tool loop (run_sql → submit_answer).</summary>
    public int MaxToolIterations { get; set; } = 6;

    /// <summary>Whether the chat surface is enabled at all. Independent of the API key.</summary>
    public bool Enabled { get; set; } = true;
}
