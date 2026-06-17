namespace Wps.Watch.AiReporting.Chat;

/// <summary>Inbound request for <c>POST /api/ask</c>.</summary>
public sealed record AskRequest(string? Question, IReadOnlyList<AskHistoryTurn>? History);

/// <summary>A prior turn in the conversation, replayed to give the model context.</summary>
public sealed record AskHistoryTurn(string Role, string Text);

/// <summary>
/// The transparency block the chat UI renders for every AI answer: the live
/// row count, the time window and applied filters the model reported, the actual
/// matching rows, and the exact SQL that produced them. Per the design brief this
/// block is mandatory — "operators verify and decide".
/// </summary>
public sealed record AskAnswer(
    int Count,
    string Window,
    IReadOnlyList<string> Filters,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    string Sql,
    bool Truncated,
    int MoreCount);

/// <summary>The body returned to the client on a successful ask.</summary>
public sealed record AskResponseBody(string Text, AskAnswer? Answer);

/// <summary>Outcome of an ask, mirroring the discriminated-union style used elsewhere in the service.</summary>
public abstract record AskResult
{
    public sealed record Success(AskResponseBody Body) : AskResult;

    /// <summary>The Anthropic key isn't configured (or the surface is disabled). Maps to 503.</summary>
    public sealed record NotConfigured(string Message) : AskResult;

    /// <summary>The upstream Claude API call failed. Maps to 502.</summary>
    public sealed record UpstreamError(string Message) : AskResult;

    /// <summary>The model didn't converge on an answer within the iteration budget. Maps to 500.</summary>
    public sealed record NoAnswer(string Message) : AskResult;
}

/// <summary>The narrative half of an answer that the model supplies via the submit_answer tool.</summary>
internal sealed record AnswerDraft(string Summary, string Window, IReadOnlyList<string> Filters);
