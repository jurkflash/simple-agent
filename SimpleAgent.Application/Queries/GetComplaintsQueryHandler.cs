using Pokok.BuildingBlocks.Cqrs.Abstractions;
using Microsoft.Extensions.Logging;
using SimpleAgent.Application.Abstractions;

namespace SimpleAgent.Application.Queries;

public sealed class GetComplaintsQueryHandler : IQueryHandler<GetComplaintsQuery, IReadOnlyList<ComplaintCountResult>>
{
    private readonly ICorrelationContextAccessor _correlationContextAccessor;
    private readonly ILogger<GetComplaintsQueryHandler> _logger;

    public GetComplaintsQueryHandler(
        ICorrelationContextAccessor correlationContextAccessor,
        ILogger<GetComplaintsQueryHandler> logger)
    {
        _correlationContextAccessor = correlationContextAccessor;
        _logger = logger;
    }

    public Task<IReadOnlyList<ComplaintCountResult>> HandleAsync(GetComplaintsQuery query, CancellationToken cancellationToken)
    {
        var correlationId = _correlationContextAccessor.CorrelationId ?? "unknown";
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        _logger.LogInformation(
            "[TraceId: {CorrelationId}] Tool handler starting for {QueryName} with month {Month}",
            correlationId,
            nameof(GetComplaintsQuery),
            query.Month);

        IReadOnlyList<ComplaintCountResult> complaints =
        [
            new ComplaintCountResult("Noise", 12),
            new ComplaintCountResult("Parking", 8)
        ];

        stopwatch.Stop();
        _logger.LogInformation(
            "[TraceId: {CorrelationId}] Tool handler completed for {QueryName} in {DurationMs}ms",
            correlationId,
            nameof(GetComplaintsQuery),
            stopwatch.ElapsedMilliseconds);

        return Task.FromResult(complaints);
    }
}
