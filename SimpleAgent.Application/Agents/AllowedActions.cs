using SimpleAgent.Application.Queries;
using SimpleAgent.Domain.Models;

namespace SimpleAgent.Application.Agents;

public sealed record AllowedActionDefinition(string Name, string Description, Type RequestType);

public static class AllowedActions
{
    public static readonly AllowedActionDefinition GetComplaints = new(
        "get_complaints",
        "Fetch complaint counts for a month.",
        typeof(GetComplaintsQuery));

    private static readonly IReadOnlyDictionary<string, AllowedActionDefinition> Definitions =
        new Dictionary<string, AllowedActionDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [GetComplaints.Name] = GetComplaints
        };

    public static IEnumerable<AllowedActionDefinition> All => Definitions.Values;

    public static bool TryGet(string actionName, out AllowedActionDefinition? definition)
    {
        return Definitions.TryGetValue(actionName, out definition);
    }

    public static string GetActionName(DecisionAction action)
    {
        return action switch
        {
            DecisionAction.GetComplaints => GetComplaints.Name,
            DecisionAction.Finish => "finish",
            _ => "unknown"
        };
    }
}
