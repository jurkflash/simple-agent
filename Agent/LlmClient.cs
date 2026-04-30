using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SimpleAgent.Models;

namespace SimpleAgent;

public sealed class LlmClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    public LlmClient(HttpClient httpClient, string apiKey, string model = "gpt-4o-mini")
    {
        _httpClient = httpClient;
        _apiKey = string.IsNullOrWhiteSpace(apiKey)
            ? throw new ArgumentException("OpenAI API key is required.", nameof(apiKey))
            : apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model;
    }

    public async Task<string> GetNextDecisionAsync(AgentState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RequestTimeout);

        var payload = new
        {
            model = _model,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You are an agent planner. Respond with JSON only. Use this schema: { \"action\": \"get_complaints\" | \"format_report\" | \"finish\", \"input\": { ... }, \"output\": \"...\" }. For tool actions, input must always be a JSON object that matches the tool schema exactly. Do not return plain string input."
                },
                new
                {
                    role = "user",
                    content = BuildPrompt(state)
                }
            }
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, timeoutCts.Token);
        }
        catch (OperationCanceledException exception) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("LLM request timed out.", exception);
        }

        using (response)
        {
            var responseContent = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"OpenAI API request failed with status {(int)response.StatusCode}: {responseContent}");
            }

            using var document = JsonDocument.Parse(responseContent);
            var content = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException("OpenAI API returned an empty response.");
            }

            return content;
        }
    }

    private static string BuildPrompt(AgentState state)
    {
        var history = state.History.Count == 0
            ? "(none)"
            : string.Join("\n", state.History);

        return $$"""
Goal:
{{state.Goal}}

History:
{{history}}

Return the next decision as JSON only.

Tool: get_complaints
Input:
{
  "month": "YYYY-MM"
}

Tool: format_report
Input:
{
  "month": "YYYY-MM",
  "complaints": [
    {
      "category": "Noise",
      "count": 12
    }
  ]
}

If more tool work is needed, set action to \"get_complaints\" or \"format_report\" and provide a structured object in \"input\".
If the task is complete, set action to \"finish\" and put the final answer in \"output\".
""";
    }
}
