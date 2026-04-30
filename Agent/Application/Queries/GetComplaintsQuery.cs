using Pokok.BuildingBlocks.Cqrs.Abstractions;

namespace SimpleAgent.Application.Queries;

public sealed record GetComplaintsQuery(string Month) : IQuery<IReadOnlyList<ComplaintCountResult>>;

public sealed record ComplaintCountResult(string Category, int Count);
