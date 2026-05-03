using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimpleAgent.Domain.Models;

public sealed class Decision
{
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)
        }
    };

    public DecisionAction Action { get; init; }

    public JsonElement Input { get; init; }

    public string? Output { get; init; }

    public static bool TryFromJson(string json, out Decision? decision, out string error)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("action", out var actionElement) ||
                actionElement.ValueKind != JsonValueKind.String)
            {
                decision = null;
                error = "Unknown action";
                return false;
            }

            var action = actionElement.GetString();
            if (action is not ("get_complaints" or "finish"))
            {
                decision = null;
                error = "Unknown action";
                return false;
            }

            decision = JsonSerializer.Deserialize<Decision>(json, SerializerOptions);
            if (decision is null)
            {
                error = "LLM returned an empty decision.";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (JsonException exception)
        {
            decision = null;
            error = $"Decision JSON is invalid: {exception.Message}";
            return false;
        }
    }
}

public enum DecisionAction
{
    GetComplaints,
    Finish
}
