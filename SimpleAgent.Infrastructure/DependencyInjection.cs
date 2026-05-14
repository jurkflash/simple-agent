using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pokok.BuildingBlocks.Cqrs.Dispatching;
using Pokok.BuildingBlocks.Common;
using SimpleAgent.Application.Abstractions;
using Pokok.BuildingBlocks.Persistence.Abstractions;
using Pokok.BuildingBlocks.Persistence.Base;
using SimpleAgent.Domain.Agents;
using SimpleAgent.Infrastructure.Agents;
using SimpleAgent.Infrastructure.Persistence;

namespace SimpleAgent.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, string? apiKey = null)
    {
        var connectionString = configuration.GetConnectionString("AgentRuns") ?? "Data Source=agent-runs.db";

        services.AddSingleton<ICorrelationContextAccessor, CorrelationContextAccessor>();
        services.AddSingleton<HttpClient>();
        services.AddDbContext<AgentDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<ICommandDispatcher, CommandDispatcher>();
        services.AddScoped<IQueryDispatcher, QueryDispatcher>();
        services.AddScoped<ICurrentUserService, AgentCurrentUserService>();
        services.AddScoped<IUnitOfWork, AgentUnitOfWork>();
        services.AddScoped<IAgentPermissionService, DefaultAgentPermissionService>();
        services.AddScoped<IAgentRunRepository, AgentRunRepository>();

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            services.AddScoped<LlmClient>(serviceProvider => new LlmClient(
                serviceProvider.GetRequiredService<HttpClient>(),
                apiKey,
                serviceProvider.GetRequiredService<ICorrelationContextAccessor>(),
                serviceProvider.GetRequiredService<ILogger<LlmClient>>()));
            services.AddScoped<ILlmClient>(serviceProvider => serviceProvider.GetRequiredService<LlmClient>());
            services.AddScoped<CqrsToolAdapter>();
            services.AddScoped<IAgentToolExecutor>(serviceProvider => serviceProvider.GetRequiredService<CqrsToolAdapter>());
        }

        return services;
    }
}
