using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pokok.BuildingBlocks.Cqrs.Dispatching;
using Pokok.BuildingBlocks.Cqrs.Extensions;
using SimpleAgent.Application.Queries;
using SimpleAgent.Models;

namespace SimpleAgent;

internal static class Program
{
    private static async Task Main()
    {
        try
        {
            const string goal = "Generate complaint summary for April";

            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.WriteLine(ErrorResult.Create("OPENAI_API_KEY is required"));
                return;
            }

            using var httpClient = new HttpClient();
            using var serviceProvider = BuildServiceProvider(httpClient, apiKey);
            using var scope = serviceProvider.CreateScope();

            var agent = scope.ServiceProvider.GetRequiredService<Agent>();
            await agent.RunAsync(goal);
        }
        catch (Exception exception)
        {
            Console.WriteLine(ErrorResult.Create(exception.Message));
        }
    }

    private static ServiceProvider BuildServiceProvider(HttpClient httpClient, string apiKey)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddSimpleConsole(options => options.SingleLine = true);
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        services.AddScoped<IQueryDispatcher, QueryDispatcher>();
        services.AddQueryHandler<GetComplaintsQuery, IReadOnlyList<ComplaintCountResult>, GetComplaintsQueryHandler>();

        services.AddScoped(_ => new LlmClient(httpClient, apiKey));
        services.AddScoped<CqrsToolAdapter>();
        services.AddScoped<Agent>();

        return services.BuildServiceProvider();
    }
}
