using System.Text.Json;
using SimpleAgent.Models;

namespace SimpleAgent.Tools;

public sealed class GetComplaintsTool : ITool
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string Name => "get_complaints";

    public bool TryValidateInput(JsonElement input, out string error)
    {
        return TryParseInput(input, out _, out error);
    }

    public Task<string> ExecuteAsync(JsonElement input)
    {
        if (!TryParseInput(input, out _, out var error))
        {
            return Task.FromResult(ErrorResult.Create(error));
        }

        var complaints = new[]
        {
            new ComplaintCount("Noise", 12),
            new ComplaintCount("Parking", 8)
        };

        var json = JsonSerializer.Serialize(complaints, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        return Task.FromResult(json);
    }

    private static bool TryParseInput(JsonElement input, out GetComplaintsToolInput? toolInput, out string error)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            toolInput = null;
            error = "Input must be a JSON object with a 'month' property.";
            return false;
        }

        try
        {
            toolInput = input.Deserialize<GetComplaintsToolInput>(JsonOptions);
            if (toolInput is null)
            {
                error = "Input could not be deserialized.";
                return false;
            }
        }
        catch (JsonException)
        {
            toolInput = null;
            error = "Input could not be deserialized.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(toolInput.Month))
        {
            error = "Month is required.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public sealed class GetComplaintsToolInput
    {
        public string? Month { get; init; }
    }

    private sealed record ComplaintCount(string Category, int Count);
}
