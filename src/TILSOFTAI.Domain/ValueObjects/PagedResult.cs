namespace TILSOFTAI.Domain.ValueObjects;

public sealed class PagedResult<T>
{
    public required IReadOnlyCollection<T> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int PageNumber { get; init; }
    public required int PageSize { get; init; }
}
