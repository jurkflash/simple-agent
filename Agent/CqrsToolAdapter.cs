using System.Text.Json;
using Pokok.BuildingBlocks.Cqrs.Dispatching;
using SimpleAgent.Application.Queries;
using SimpleAgent.Models;

namespace SimpleAgent;

public sealed class CqrsToolAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IQueryDispatcher _queryDispatcher;

    public CqrsToolAdapter(IQueryDispatcher queryDispatcher)
    {
        _queryDispatcher = queryDispatcher;
    }

    public bool TryValidateAction(DecisionAction action, JsonElement input, out string error)
    {
        return action switch
        {
            DecisionAction.GetComplaints => TryParseGetComplaintsQuery(input, out _, out error),
            _ => UnknownAction(out error)
        };
    }

    public async Task<string> ExecuteAsync(DecisionAction action, JsonElement input, CancellationToken cancellationToken = default)
    {
        try
        {
            return action switch
            {
                DecisionAction.GetComplaints => await ExecuteGetComplaintsAsync(input, cancellationToken),
                _ => ErrorResult.Create("Unknown action")
            };
        }
        catch (Exception exception)
        {
            return ErrorResult.Create(exception.Message);
        }
    }

    private async Task<string> ExecuteGetComplaintsAsync(JsonElement input, CancellationToken cancellationToken)
    {
        if (!TryParseGetComplaintsQuery(input, out var query, out var error))
        {
            return ErrorResult.Create(error);
        }

        Console.WriteLine("Mapped query:");
        Console.WriteLine(JsonSerializer.Serialize(query, JsonOptions));

        var result = await _queryDispatcher.DispatchAsync<GetComplaintsQuery, IReadOnlyList<ComplaintCountResult>>(query!, cancellationToken);
        var resultJson = JsonSerializer.Serialize(result, JsonOptions);

        Console.WriteLine("Query execution result:");
        Console.WriteLine(resultJson);

        return resultJson;
    }

    private static bool TryParseGetComplaintsQuery(JsonElement input, out GetComplaintsQuery? query, out string error)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            query = null;
            error = "Input must be a JSON object with a 'month' property.";
            return false;
        }

        try
        {
            query = input.Deserialize<GetComplaintsQuery>(JsonOptions);
        }
        catch (JsonException)
        {
            query = null;
            error = "Input could not be deserialized.";
            return false;
        }

        if (query is null)
        {
            error = "Input could not be deserialized.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(query.Month))
        {
            error = "Month is required.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool UnknownAction(out string error)
    {
        error = "Unknown action";
        return false;
    }
}
