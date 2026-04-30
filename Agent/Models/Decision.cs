using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimpleAgent.Models;

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

    public string? Input { get; init; }

    public string? Output { get; init; }

    public static Decision FromJson(string json)
    {
        var decision = JsonSerializer.Deserialize<Decision>(json, SerializerOptions);
        return decision ?? throw new InvalidOperationException("LLM returned an empty decision.");
    }
}

public enum DecisionAction
{
    GetComplaints,
    Finish
}
