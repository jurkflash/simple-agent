using Pokok.BuildingBlocks.Cqrs.Abstractions;

namespace SimpleAgent.Application.Queries;

public sealed class GetComplaintsQueryHandler : IQueryHandler<GetComplaintsQuery, IReadOnlyList<ComplaintCountResult>>
{
    public Task<IReadOnlyList<ComplaintCountResult>> HandleAsync(GetComplaintsQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<ComplaintCountResult> complaints =
        [
            new ComplaintCountResult("Noise", 12),
            new ComplaintCountResult("Parking", 8)
        ];

        return Task.FromResult(complaints);
    }
}
