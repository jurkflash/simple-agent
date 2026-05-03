using Microsoft.AspNetCore.Mvc;
using SimpleAgent.Api.Models;
using SimpleAgent.Api.Services;

namespace SimpleAgent.Api.Controllers;

[ApiController]
[Route("agent")]
public sealed class AgentController : ControllerBase
{
    private readonly AgentRequestClient _agentRequestClient;

    public AgentController(AgentRequestClient agentRequestClient)
    {
        _agentRequestClient = agentRequestClient;
    }

    [HttpPost("run")]
    public async Task<IActionResult> RunAsync([FromBody] AgentRunRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Goal))
        {
            return BadRequest(new { error = "Goal is required." });
        }

        try
        {
            var result = await _agentRequestClient.RunAsync(request.Goal, cancellationToken);
            if (!result.Success)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        correlationId = result.CorrelationId,
                        error = result.Error ?? "Agent processing failed."
                    });
            }

            return Ok(new
            {
                correlationId = result.CorrelationId,
                result = result.Result
            });
        }
        catch (TimeoutException)
        {
            return StatusCode(
                StatusCodes.Status504GatewayTimeout,
                new { error = "Agent response timed out." });
        }
    }
}
