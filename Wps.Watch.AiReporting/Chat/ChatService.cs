using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Wps.Watch.AiReporting.Discovery;

namespace Wps.Watch.AiReporting.Chat;

/// <summary>
/// "Ask AI Reports" backend. Translates a plain-English question into SQL by
/// driving the Claude Messages API through a small agentic loop:
///
///   1. Claude is given the queryable schema + two tools: <c>run_sql</c> and <c>submit_answer</c>.
///   2. It calls <c>run_sql</c>; we execute the SELECT through the existing
///      <see cref="DiscoveryQueryRunner"/> (SqlGuard, row cap, sensitive-column strip,
///      always-rolled-back transaction) and feed the rows back as a tool_result.
///   3. It finishes by calling <c>submit_answer</c> with a concise summary, the time
///      window, and the filters it applied.
///
/// The transparency block returned to the UI (row count, columns, rows, SQL) is
/// built from the *actual* execution result — not from anything the model asserts —
/// so the brief's "every answer shows its rows and the SQL" guarantee holds even if
/// the model's narrative is wrong.
///
/// Calls the API over <see cref="HttpClient"/> directly (no SDK dependency): a single
/// tool loop with full control over the transparency assembly, and zero new packages.
/// </summary>
public sealed class ChatService
{
    private const string AnthropicVersion = "2023-06-01";
    private const int RowsToModel = 40;   // cap rows fed back into the model's context
    private const int RowsToClient = 8;    // rows surfaced in the UI transparency table

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DiscoveryQueryRunner _runner;
    private readonly AnthropicOptions _options;
    private readonly ILogger<ChatService> _logger;

    // The queryable schema rarely changes; describe it once per process.
    private string? _cachedSchema;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);

    public ChatService(
        IHttpClientFactory httpClientFactory,
        DiscoveryQueryRunner runner,
        IOptions<AnthropicOptions> options,
        ILogger<ChatService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _runner = runner;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AskResult> AskAsync(AskRequest request, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return new AskResult.NotConfigured("The Ask AI Reports chat surface is disabled.");
        }
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return new AskResult.NotConfigured(
                "The chat endpoint isn't configured. Set an Anthropic API key via " +
                "`dotnet user-secrets set \"Anthropic:ApiKey\" \"sk-ant-...\"` or the " +
                "Anthropic__ApiKey environment variable.");
        }
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return new AskResult.NoAnswer("Ask a question first.");
        }

        var schema = await GetSchemaTextAsync(cancellationToken);
        if (schema is null)
        {
            return new AskResult.UpstreamError("The reporting schema is unavailable (discovery disabled or DB unreachable).");
        }

        var systemPrompt = BuildSystemPrompt(schema);

        var messages = new JsonArray();
        if (request.History is { Count: > 0 })
        {
            foreach (var turn in request.History)
            {
                var role = string.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user";
                messages.Add(TextMessage(role, turn.Text));
            }
        }
        messages.Add(TextMessage("user", request.Question!));

        DiscoveryQueryOutcome.Success? lastResult = null;

        for (var iteration = 0; iteration < _options.MaxToolIterations; iteration++)
        {
            var (root, error) = await CallClaudeAsync(systemPrompt, messages, cancellationToken);
            if (error is not null)
            {
                return new AskResult.UpstreamError(error);
            }

            var content = root!["content"] as JsonArray ?? new JsonArray();
            var stopReason = root["stop_reason"]?.GetValue<string>();

            // Echo the assistant turn back verbatim (preserves thinking-block signatures
            // for the next request — required when continuing on the same model).
            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = content.DeepClone() });

            if (stopReason == "tool_use")
            {
                var toolResults = new JsonArray();
                AnswerDraft? answer = null;

                foreach (var block in content)
                {
                    if (block?["type"]?.GetValue<string>() != "tool_use")
                    {
                        continue;
                    }

                    var toolName = block["name"]?.GetValue<string>();
                    var toolUseId = block["id"]?.GetValue<string>() ?? string.Empty;
                    var input = block["input"];

                    if (toolName == "run_sql")
                    {
                        var sql = input?["sql"]?.GetValue<string>();
                        var outcome = await _runner.RunQueryAsync(sql, cancellationToken);
                        var (resultText, isError) = RenderRunResult(outcome);
                        if (outcome is DiscoveryQueryOutcome.Success success)
                        {
                            lastResult = success;
                        }
                        toolResults.Add(ToolResult(toolUseId, resultText, isError));
                    }
                    else if (toolName == "submit_answer")
                    {
                        answer = ParseAnswerDraft(input);
                        toolResults.Add(ToolResult(toolUseId, "Answer recorded.", isError: false));
                    }
                    else
                    {
                        toolResults.Add(ToolResult(toolUseId, $"Unknown tool '{toolName}'.", isError: true));
                    }
                }

                if (answer is not null)
                {
                    // submit_answer is terminal — we stop here; the tool_result we built is
                    // unused because there is no follow-up request.
                    return new AskResult.Success(BuildResponse(answer, lastResult));
                }

                messages.Add(new JsonObject { ["role"] = "user", ["content"] = toolResults });
                continue;
            }

            // end_turn (or any non-tool stop) without submit_answer: fall back to the
            // model's text as the summary, attaching whatever was last queried.
            var text = ExtractText(content);
            if (string.IsNullOrWhiteSpace(text))
            {
                return new AskResult.NoAnswer("The assistant returned an empty answer.");
            }
            return new AskResult.Success(BuildResponse(new AnswerDraft(text, string.Empty, Array.Empty<string>()), lastResult));
        }

        return new AskResult.NoAnswer("The assistant didn't reach an answer within the step budget. Try rephrasing.");
    }

    // ───────────────────────── Claude API call ─────────────────────────

    private async Task<(JsonNode? Root, string? Error)> CallClaudeAsync(
        string systemPrompt, JsonArray messages, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["max_tokens"] = _options.MaxTokens,
            // Adaptive thinking improves NL→SQL accuracy; thinking blocks round-trip
            // verbatim because we echo the full assistant content back each turn.
            ["thinking"] = new JsonObject { ["type"] = "adaptive" },
            ["system"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = systemPrompt,
                    ["cache_control"] = new JsonObject { ["type"] = "ephemeral" },
                },
            },
            ["tools"] = BuildToolsSchema(),
            ["messages"] = messages.DeepClone(),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/v1/messages")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("x-api-key", _options.ApiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var client = _httpClientFactory.CreateClient("anthropic");

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Anthropic request failed to send.");
            return (null, $"Could not reach the Claude API: {ex.Message}");
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Anthropic API returned {Status}: {Body}", (int)response.StatusCode, Truncate(payload, 600));
            var reason = ExtractApiError(payload) ?? $"HTTP {(int)response.StatusCode}";
            return (null, $"The Claude API rejected the request ({reason}).");
        }

        try
        {
            return (JsonNode.Parse(payload), null);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not parse Anthropic response.");
            return (null, "The Claude API returned an unparseable response.");
        }
    }

    private static JsonArray BuildToolsSchema() => new()
    {
        new JsonObject
        {
            ["name"] = "run_sql",
            ["description"] =
                "Run a single read-only SQL SELECT against the wpsWatch reporting database and get the rows back. " +
                "Only SELECT (or WITH … SELECT) is allowed; the query is executed inside an always-rolled-back " +
                "transaction and capped to a few thousand rows. Use this to gather the data you need before answering.",
            ["input_schema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["sql"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "A single T-SQL SELECT statement (no trailing semicolon, no second statement).",
                    },
                },
                ["required"] = new JsonArray { "sql" },
            },
        },
        new JsonObject
        {
            ["name"] = "submit_answer",
            ["description"] =
                "Provide the final answer to the user. Call this exactly once, after you have the data you need. " +
                "Give a concise plain-English summary, the time window the answer covers, and the filters you applied. " +
                "The row count, the matching rows, and the exact SQL are attached automatically from the query you ran — " +
                "do not restate them.",
            ["input_schema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["summary"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "1–3 sentence answer in plain English.",
                    },
                    ["time_window"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "The time window the answer covers, e.g. \"Current snapshot\" or \"Last 30 days\".",
                    },
                    ["filters"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject { ["type"] = "string" },
                        ["description"] = "Human-readable filters applied, e.g. [\"Region: Africa\", \"Online% < 70\"].",
                    },
                },
                ["required"] = new JsonArray { "summary" },
            },
        },
    };

    // ───────────────────────── tool execution rendering ─────────────────────────

    private static (string Text, bool IsError) RenderRunResult(DiscoveryQueryOutcome outcome) => outcome switch
    {
        DiscoveryQueryOutcome.Success s => (RenderSuccessRows(s), false),
        DiscoveryQueryOutcome.Rejected r => ($"Query rejected by the SQL guard: {r.Reason}", true),
        DiscoveryQueryOutcome.Failed f => ($"Query failed to execute: {f.Reason}", true),
        DiscoveryQueryOutcome.Forbidden => ("You are not permitted to run discovery queries.", true),
        DiscoveryQueryOutcome.Unauthenticated => ("Not authenticated.", true),
        DiscoveryQueryOutcome.Disabled => ("Discovery queries are disabled on this server.", true),
        _ => ("Unknown query outcome.", true),
    };

    private static string RenderSuccessRows(DiscoveryQueryOutcome.Success s)
    {
        var capped = s.Rows.Take(RowsToModel).ToList();
        var payload = new JsonObject
        {
            ["rowCount"] = s.RowCount,
            ["truncated"] = s.Truncated,
            ["rowsShown"] = capped.Count,
            ["columns"] = new JsonArray(s.Columns.Select(c => (JsonNode)c!).ToArray()),
            ["rows"] = RowsToJson(capped),
        };
        return payload.ToJsonString();
    }

    // ───────────────────────── response assembly ─────────────────────────

    private static AskResponseBody BuildResponse(AnswerDraft draft, DiscoveryQueryOutcome.Success? result)
    {
        if (result is null)
        {
            return new AskResponseBody(draft.Summary, Answer: null);
        }

        var shown = result.Rows.Take(RowsToClient).ToList();
        var more = Math.Max(0, result.RowCount - shown.Count);

        var answer = new AskAnswer(
            Count: result.RowCount,
            Window: string.IsNullOrWhiteSpace(draft.Window) ? "Current snapshot" : draft.Window,
            Filters: draft.Filters,
            Columns: result.Columns,
            Rows: shown,
            Sql: result.ExecutedSql,
            Truncated: result.Truncated,
            MoreCount: more);

        return new AskResponseBody(draft.Summary, answer);
    }

    private static AnswerDraft ParseAnswerDraft(JsonNode? input)
    {
        var summary = input?["summary"]?.GetValue<string>() ?? "Here's what I found.";
        var window = input?["time_window"]?.GetValue<string>() ?? string.Empty;
        var filters = new List<string>();
        if (input?["filters"] is JsonArray arr)
        {
            foreach (var f in arr)
            {
                var s = f?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    filters.Add(s!);
                }
            }
        }
        return new AnswerDraft(summary, window, filters);
    }

    // ───────────────────────── system prompt + schema ─────────────────────────

    private async Task<string?> GetSchemaTextAsync(CancellationToken cancellationToken)
    {
        if (_cachedSchema is not null)
        {
            return _cachedSchema;
        }

        await _schemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedSchema is not null)
            {
                return _cachedSchema;
            }

            var outcome = await _runner.DescribeSchemaAsync(cancellationToken);
            if (outcome is not DiscoverySchemaOutcome.Success success)
            {
                return null;
            }

            var sb = new StringBuilder();
            foreach (var table in success.Tables)
            {
                sb.Append(table.Name).Append('(');
                sb.Append(string.Join(", ", table.Columns.Select(c => $"{c.Name} {c.DataType}")));
                sb.Append(')').Append('\n');
            }

            _cachedSchema = sb.ToString();
            return _cachedSchema;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    private static string BuildSystemPrompt(string schema) =>
$@"You are ""Ask AI Reports"", a read-only analyst for the wpsWatch wildlife-monitoring platform.
Users ask about their camera fleet — inventory, deployments, online/offline status, active issues,
SIM/battery, photos — in plain English. You answer by writing T-SQL against the reporting database
and reading the results. You never make operational decisions; you surface data so an operator can decide.

Rules:
- Use the run_sql tool to fetch data. Only a single read-only SELECT (or WITH … SELECT) is permitted per call.
  No INSERT/UPDATE/DELETE, no semicolons, no second statement, no stored procedures.
- Results are already scoped to the signed-in user's organizations by the server — do not add org-id filters
  unless the user explicitly asks to narrow to a specific org.
- Prefer aggregates and TOP/ORDER BY so results are small and legible. The server caps rows automatically.
- ""Online"" vs ""offline"" in this domain means a deployed camera seen within the last 1440 minutes (24h).
- When you have what you need, finish by calling submit_answer with a short summary, the time window, and the
  filters you applied. Do not restate the row count, the rows, or the SQL — the server attaches those.
- If a query fails or is rejected, read the error and try a corrected query. If you genuinely cannot answer
  from the data, say so via submit_answer.

Queryable schema (table(column type, ...)):
{schema}";

    // ───────────────────────── JSON helpers ─────────────────────────

    private static JsonObject TextMessage(string role, string text) => new()
    {
        ["role"] = role,
        ["content"] = text,
    };

    // Returns a single tool_result content block. The caller collects these into one
    // JsonArray and wraps them in a single { role: "user", content: [...] } message —
    // so this must NOT return a message object, or the blocks end up double-wrapped.
    private static JsonObject ToolResult(string toolUseId, string content, bool isError)
    {
        var block = new JsonObject
        {
            ["type"] = "tool_result",
            ["tool_use_id"] = toolUseId,
            ["content"] = content,
        };
        if (isError)
        {
            block["is_error"] = true;
        }
        return block;
    }

    private static JsonArray RowsToJson(IEnumerable<IReadOnlyDictionary<string, object?>> rows)
    {
        var arr = new JsonArray();
        foreach (var row in rows)
        {
            var obj = new JsonObject();
            foreach (var (key, value) in row)
            {
                obj[key] = ToJsonValue(value);
            }
            arr.Add(obj);
        }
        return arr;
    }

    private static JsonNode? ToJsonValue(object? value) => value switch
    {
        null => null,
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        short sh => JsonValue.Create(sh),
        byte bt => JsonValue.Create(bt),
        double d => JsonValue.Create(d),
        float f => JsonValue.Create(f),
        decimal m => JsonValue.Create(m),
        DateTime dt => JsonValue.Create(dt.ToString("o")),
        DateTimeOffset dto => JsonValue.Create(dto.ToString("o")),
        Guid g => JsonValue.Create(g.ToString()),
        byte[] bytes => JsonValue.Create(Convert.ToBase64String(bytes)),
        _ => JsonValue.Create(value.ToString()),
    };

    private static string ExtractText(JsonArray content)
    {
        var sb = new StringBuilder();
        foreach (var block in content)
        {
            if (block?["type"]?.GetValue<string>() == "text")
            {
                sb.Append(block["text"]?.GetValue<string>());
            }
        }
        return sb.ToString().Trim();
    }

    private static string? ExtractApiError(string payload)
    {
        try
        {
            var node = JsonNode.Parse(payload);
            return node?["error"]?["message"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
