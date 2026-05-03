using Microsoft.Extensions.DependencyInjection;
using Pokok.BuildingBlocks.Cqrs.Extensions;
using SimpleAgent.Application.Agents;
using SimpleAgent.Application.Queries;

namespace SimpleAgent.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<Agent>();
        services.AddQueryHandler<GetComplaintsQuery, IReadOnlyList<ComplaintCountResult>, GetComplaintsQueryHandler>();
        return services;
    }
}
