using Microsoft.AspNetCore.Mvc;
using Pokok.BuildingBlocks.Cqrs.Dispatching;
using SimpleAgent.Application.Commands;
using SimpleAgent.Api.Models;
using SimpleAgent.Api.Services;

namespace SimpleAgent.Api.Controllers;

[ApiController]
[Route("agent")]
public sealed class AgentController : ControllerBase
{
    private readonly AgentRequestClient _agentRequestClient;
    private readonly ICommandDispatcher _commandDispatcher;

    public AgentController(AgentRequestClient agentRequestClient, ICommandDispatcher commandDispatcher)
    {
        _agentRequestClient = agentRequestClient;
        _commandDispatcher = commandDispatcher;
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

    [HttpPost("runs/{agentRunId:guid}/replay")]
    public async Task<IActionResult> ReplayAsync(Guid agentRunId, CancellationToken cancellationToken)
    {
        var result = await _commandDispatcher.DispatchAsync<ReplayAgentRunCommand, ReplayAgentRunResult>(
            new ReplayAgentRunCommand(agentRunId),
            cancellationToken);

        if (!result.Accepted)
        {
            return result.Error?.Code switch
            {
                "not_found" => NotFound(new
                {
                    originalRunId = result.OriginalRunId,
                    error = result.Error
                }),
                "invalid_status" => Conflict(new
                {
                    originalRunId = result.OriginalRunId,
                    error = result.Error
                }),
                "unsafe_action" => Conflict(new
                {
                    originalRunId = result.OriginalRunId,
                    error = result.Error
                }),
                _ => BadRequest(new
                {
                    originalRunId = result.OriginalRunId,
                    error = result.Error
                })
            };
        }

        return Accepted(new
        {
            originalRunId = result.OriginalRunId,
            replayRunId = result.ReplayRunId,
            correlationId = result.CorrelationId
        });
    }
}
