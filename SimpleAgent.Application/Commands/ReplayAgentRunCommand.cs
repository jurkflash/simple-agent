using Pokok.BuildingBlocks.Cqrs.Abstractions;

namespace SimpleAgent.Application.Commands;

public sealed record ReplayAgentRunCommand(Guid AgentRunId) : ICommand<ReplayAgentRunResult>;
