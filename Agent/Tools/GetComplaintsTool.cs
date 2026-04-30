using System.Text.Json;

namespace SimpleAgent.Tools;

public sealed class GetComplaintsTool : ITool
{
    public string Name => "get_complaints";

    public Task<string> ExecuteAsync(string input)
    {
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

    private sealed record ComplaintCount(string Category, int Count);
}
