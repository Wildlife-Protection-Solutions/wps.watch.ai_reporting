using Microsoft.AspNetCore.Mvc;
using Wps.Watch.AiReporting.Chat;

namespace Wps.Watch.AiReporting.Controllers;

/// <summary>
/// "Ask AI Reports" chat surface. Takes a plain-English question, routes it through
/// <see cref="ChatService"/> (Claude → SQL → guarded execution), and returns the
/// answer plus the brief-mandated transparency block (row count, rows, time window,
/// filters, and the SQL that ran).
/// </summary>
[ApiController]
[Route("api/ask")]
public sealed class AskController : ControllerBase
{
    private readonly ChatService _chat;

    public AskController(ChatService chat)
    {
        _chat = chat;
    }

    [HttpPost]
    public async Task<IActionResult> Ask([FromBody] AskRequest request, CancellationToken cancellationToken)
    {
        var outcome = await _chat.AskAsync(request, cancellationToken);

        return outcome switch
        {
            AskResult.Success s => Ok(s.Body),
            AskResult.NotConfigured nc => StatusCode(503, new { error = "chat_not_configured", message = nc.Message }),
            AskResult.UpstreamError ue => StatusCode(502, new { error = "upstream_error", message = ue.Message }),
            AskResult.NoAnswer na => StatusCode(500, new { error = "no_answer", message = na.Message }),
            _ => StatusCode(500),
        };
    }
}
