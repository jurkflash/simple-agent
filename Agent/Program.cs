using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pokok.BuildingBlocks.Messaging.RabbitMQ;
using SimpleAgent.Application;
using SimpleAgent.Infrastructure;

namespace SimpleAgent;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.IncludeScopes = true;
            options.TimestampFormat = "HH:mm:ss.fff ";
        });
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        var apiKey = builder.Configuration["OPENAI_API_KEY"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OPENAI_API_KEY is required.");
        }

        builder.Services.Configure<RabbitMQOptions>(builder.Configuration.GetSection("RabbitMQ"));
        builder.Services.AddSingleton<RabbitMQConnection>();
        builder.Services.AddSingleton<IRabbitMQConnection>(serviceProvider => serviceProvider.GetRequiredService<RabbitMQConnection>());
        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(apiKey);
        builder.Services.AddHostedService<AgentWorker>();

        using var host = builder.Build();
        await host.RunAsync();
    }
}
