using SimpleAgent.Application.Queries;
using SimpleAgent.Domain.Models;

namespace SimpleAgent.Application.Agents;

public sealed record AgentActionDefinition(
    string Name,
    string Description,
    Type? RequestType,
    bool Replayable,
    bool Idempotent);

public static class AllowedActions
{
    public static readonly AgentActionDefinition GetComplaints = new(
        "get_complaints",
        "Fetch complaint counts for a month.",
        typeof(GetComplaintsQuery),
        Replayable: true,
        Idempotent: true);

    public static readonly AgentActionDefinition Finish = new(
        "finish",
        "Finish the agent run with a final answer.",
        null,
        Replayable: true,
        Idempotent: true);

    public static readonly AgentActionDefinition LlmDecision = new(
        "llm_decision",
        "Internal LLM decision step.",
        null,
        Replayable: true,
        Idempotent: true);

    private static readonly IReadOnlyDictionary<string, AgentActionDefinition> Definitions =
        new Dictionary<string, AgentActionDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [GetComplaints.Name] = GetComplaints,
            [Finish.Name] = Finish,
            [LlmDecision.Name] = LlmDecision
        };

    public static IEnumerable<AgentActionDefinition> All => Definitions.Values;

    public static bool TryGet(string actionName, out AgentActionDefinition? definition)
    {
        return Definitions.TryGetValue(actionName, out definition);
    }

    public static string GetActionName(DecisionAction action)
    {
        return action switch
        {
            DecisionAction.GetComplaints => GetComplaints.Name,
            DecisionAction.Finish => Finish.Name,
            _ => "unknown"
        };
    }
}
