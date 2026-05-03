using System.Text.Json.Serialization;
using Pokok.BuildingBlocks.Cqrs.Abstractions;

namespace SimpleAgent.Application.Queries;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GetComplaintsQuery : IQuery<IReadOnlyList<ComplaintCountResult>>
{
    public string? Month { get; init; }
}

public sealed record ComplaintCountResult(string Category, int Count);
