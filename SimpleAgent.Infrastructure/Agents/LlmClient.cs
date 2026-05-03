using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SimpleAgent.Application.Abstractions;
using SimpleAgent.Application.Agents;
using SimpleAgent.Domain.Models;

namespace SimpleAgent.Infrastructure.Agents;

public sealed class LlmClient : ILlmClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ICorrelationContextAccessor _correlationContextAccessor;
    private readonly ILogger<LlmClient> _logger;
    private readonly string _model;

    public LlmClient(
        HttpClient httpClient,
        string apiKey,
        ICorrelationContextAccessor correlationContextAccessor,
        ILogger<LlmClient> logger,
        string model = "gpt-4o-mini")
    {
        _httpClient = httpClient;
        _apiKey = string.IsNullOrWhiteSpace(apiKey)
            ? throw new ArgumentException("OpenAI API key is required.", nameof(apiKey))
            : apiKey;
        _correlationContextAccessor = correlationContextAccessor;
        _logger = logger;
        _model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model;
    }

    public async Task<string> GetNextDecisionAsync(
        AgentState state,
        int stepNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var correlationId = _correlationContextAccessor.CorrelationId ?? state.CorrelationId;
        var prompt = BuildPrompt(state);
        var requestPayload = new
        {
            model = _model,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You are an agent planner. Respond with JSON only. Use this schema: { \"action\": \"get_complaints\" | \"finish\", \"input\": { ... }, \"output\": \"...\" }. For get_complaints, input must always be a JSON object that matches the schema exactly. Do not return plain string input."
                },
                new
                {
                    role = "user",
                    content = prompt
                }
            }
        };

        // Keep prompt/response logging truncated to reduce the chance of exposing sensitive data in logs.
        _logger.LogInformation(
            "[TraceId: {CorrelationId}] Step {StepNumber}: sending LLM prompt {Prompt}",
            correlationId,
            stepNumber,
            TraceLogSanitizer.Truncate(prompt));

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Headers.Add("X-Correlation-ID", correlationId);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RequestTimeout);

        request.Content = new StringContent(
            JsonSerializer.Serialize(requestPayload),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            response = await _httpClient.SendAsync(request, timeoutCts.Token);
        }
        catch (OperationCanceledException exception) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogError(
                exception,
                "[TraceId: {CorrelationId}] Step {StepNumber}: LLM request timed out after {DurationMs}ms",
                correlationId,
                stepNumber,
                stopwatch.ElapsedMilliseconds);
            throw new TimeoutException("LLM request timed out.", exception);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _logger.LogError(
                exception,
                "[TraceId: {CorrelationId}] Step {StepNumber}: LLM request failed after {DurationMs}ms",
                correlationId,
                stepNumber,
                stopwatch.ElapsedMilliseconds);
            throw;
        }

        using (response)
        {
            var responseContent = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[TraceId: {CorrelationId}] Step {StepNumber}: LLM returned status {StatusCode} in {DurationMs}ms with payload {Response}",
                    correlationId,
                    stepNumber,
                    (int)response.StatusCode,
                    stopwatch.ElapsedMilliseconds,
                    TraceLogSanitizer.Truncate(responseContent));
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

            _logger.LogInformation(
                "[TraceId: {CorrelationId}] Step {StepNumber}: raw LLM response received in {DurationMs}ms {Response}",
                correlationId,
                stepNumber,
                stopwatch.ElapsedMilliseconds,
                TraceLogSanitizer.Truncate(content));

            return content;
        }
    }

    private static string BuildPrompt(AgentState state)
    {
        var history = state.History.Count == 0
            ? "(none)"
            : string.Join("\n", state.History);

        var allowedActions = string.Join(
            "\n",
            AllowedActions.All.Select(action => $"- {action.Name}: {action.Description}"));

        return $$"""
Goal:
{{state.Goal}}

History:
{{history}}

Return the next decision as JSON only.

Allowed actions:
{{allowedActions}}

You MUST only choose from the allowed actions above.
If you are unsure, choose "unknown". If you choose any other action, it will be rejected.

Action: get_complaints
Input:
{
  "month": "YYYY-MM"
}

If more data is needed, set action to "get_complaints" and provide a structured object in "input".
After complaint data is returned, summarize it and respond with action "finish".
If the task is complete, set action to "finish" and put the final answer in "output".
""";
    }
}
