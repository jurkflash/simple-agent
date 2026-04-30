using System.Text;
using System.Text.Json;
using SimpleAgent.Models;

namespace SimpleAgent.Tools;

public sealed class FormatReportTool : ITool
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string Name => "format_report";

    public bool TryValidateInput(JsonElement input, out string error)
    {
        return TryParseInput(input, out _, out error);
    }

    public Task<string> ExecuteAsync(JsonElement input)
    {
        if (!TryParseInput(input, out var toolInput, out var error))
        {
            return Task.FromResult(ErrorResult.Create(error));
        }

        var lines = new List<string>();

        foreach (var item in toolInput!.Complaints!)
        {
            lines.Add($"- {item.Category}: {item.Count}");
        }

        var summary = new StringBuilder()
            .AppendLine($"Complaint summary for {toolInput.Month}")
            .Append(string.Join(Environment.NewLine, lines))
            .ToString();

        return Task.FromResult(summary);
    }

    private static bool TryParseInput(JsonElement input, out FormatReportToolInput? toolInput, out string error)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            toolInput = null;
            error = "Input must be a JSON object with 'month' and 'complaints' properties.";
            return false;
        }

        try
        {
            toolInput = input.Deserialize<FormatReportToolInput>(JsonOptions);
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

        if (toolInput.Complaints is null || toolInput.Complaints.Count == 0)
        {
            error = "Complaints must contain at least one item.";
            return false;
        }

        for (var index = 0; index < toolInput.Complaints.Count; index++)
        {
            var complaint = toolInput.Complaints[index];
            if (string.IsNullOrWhiteSpace(complaint.Category))
            {
                error = $"Complaints[{index}].Category is required.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    public sealed class FormatReportToolInput
    {
        public string? Month { get; init; }

        public List<ComplaintItem>? Complaints { get; init; }
    }

    public sealed class ComplaintItem
    {
        public string? Category { get; init; }

        public int Count { get; init; }
    }
}
