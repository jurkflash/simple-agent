using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pokok.BuildingBlocks.Cqrs.Dispatching;
using SimpleAgent.Application.Abstractions;
using SimpleAgent.Infrastructure.Agents;

namespace SimpleAgent.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string apiKey)
    {
        services.AddSingleton<ICorrelationContextAccessor, CorrelationContextAccessor>();
        services.AddSingleton<HttpClient>();
        services.AddScoped<IQueryDispatcher, QueryDispatcher>();
        services.AddScoped<IAgentPermissionService, DefaultAgentPermissionService>();
        services.AddScoped<LlmClient>(serviceProvider => new LlmClient(
            serviceProvider.GetRequiredService<HttpClient>(),
            apiKey,
            serviceProvider.GetRequiredService<ICorrelationContextAccessor>(),
            serviceProvider.GetRequiredService<ILogger<LlmClient>>()));
        services.AddScoped<ILlmClient>(serviceProvider => serviceProvider.GetRequiredService<LlmClient>());
        services.AddScoped<CqrsToolAdapter>();
        services.AddScoped<IAgentToolExecutor>(serviceProvider => serviceProvider.GetRequiredService<CqrsToolAdapter>());
        return services;
    }
}
