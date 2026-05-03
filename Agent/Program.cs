using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SimpleAgent.Application;
using SimpleAgent.Application.Agents;
using SimpleAgent.Domain.Models;
using SimpleAgent.Infrastructure;

namespace SimpleAgent;

internal static class Program
{
    private static async Task Main()
    {
        using var bootstrapLoggerFactory = CreateLoggerFactory();
        var logger = bootstrapLoggerFactory.CreateLogger("Program");

        try
        {
            const string goal = "Generate complaint summary for April";
            var correlationId = Guid.NewGuid().ToString();

            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                logger.LogError(
                    "[TraceId: {CorrelationId}] {Error}",
                    correlationId,
                    ErrorResult.Create("OPENAI_API_KEY is required"));
                return;
            }

            using var httpClient = new HttpClient();
            using var serviceProvider = BuildServiceProvider(httpClient, apiKey);
            using var scope = serviceProvider.CreateScope();

            var agent = scope.ServiceProvider.GetRequiredService<Agent>();
            var result = await agent.RunAsync(goal, correlationId);

            logger.LogInformation(
                "[TraceId: {CorrelationId}] Agent run summary {Summary}",
                correlationId,
                JsonSerializer.Serialize(result.Summary, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));
            logger.LogInformation(
                "[TraceId: {CorrelationId}] Final output {Output}",
                correlationId,
                TraceLogSanitizer.Truncate(result.Output));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "{Error}", ErrorResult.Create(exception.Message));
        }
    }

    private static ServiceProvider BuildServiceProvider(HttpClient httpClient, string apiKey)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.IncludeScopes = true;
                options.TimestampFormat = "HH:mm:ss.fff ";
            });
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddApplication();
        services.AddInfrastructure(httpClient, apiKey);

        return services.BuildServiceProvider();
    }

    private static ILoggerFactory CreateLoggerFactory()
    {
        return LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.IncludeScopes = true;
                options.TimestampFormat = "HH:mm:ss.fff ";
            });
            builder.SetMinimumLevel(LogLevel.Information);
        });
    }
}
