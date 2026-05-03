using System.Text.Json;

namespace SimpleAgent.Application.Agents;

public static class TraceLogSanitizer
{
    private const int DefaultMaxLength = 1_000;

    public static string Truncate(string? value, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(empty)";
        }

        return value.Length <= maxLength
            ? value
            : $"{value[..maxLength]}...(truncated)";
    }

    public static string FormatJson(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return "null";
        }

        return JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    public static string FormatOutput(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (JsonException)
        {
            return output;
        }
    }
}
