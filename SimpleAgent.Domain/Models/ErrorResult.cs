using System.Text.Json;

namespace SimpleAgent.Domain.Models;

public sealed class ErrorResult
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string Error { get; init; } = string.Empty;

    public static string Create(string message)
    {
        return JsonSerializer.Serialize(new ErrorResult { Error = message }, SerializerOptions);
    }

    public static bool TryGetMessage(string value, out string error)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("error", out var errorElement) &&
                errorElement.ValueKind == JsonValueKind.String)
            {
                error = errorElement.GetString() ?? string.Empty;
                return true;
            }
        }
        catch (JsonException)
        {
        }

        error = string.Empty;
        return false;
    }
}
