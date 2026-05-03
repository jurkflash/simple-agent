using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pokok.BuildingBlocks.Cqrs.Dispatching;
using SimpleAgent.Application.Abstractions;
using SimpleAgent.Infrastructure.Agents;

namespace SimpleAgent.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, HttpClient httpClient, string apiKey)
    {
        services.AddSingleton<ICorrelationContextAccessor, CorrelationContextAccessor>();
        services.AddScoped<IQueryDispatcher, QueryDispatcher>();
        services.AddScoped<IAgentPermissionService, DefaultAgentPermissionService>();
        services.AddScoped<ILlmClient>(serviceProvider => new LlmClient(
            httpClient,
            apiKey,
            serviceProvider.GetRequiredService<ICorrelationContextAccessor>(),
            serviceProvider.GetRequiredService<ILogger<LlmClient>>()));
        services.AddScoped<IAgentToolExecutor, CqrsToolAdapter>();
        return services;
    }
}
