namespace MyCollection.Application.Common;

public record PagedResult<T>(IReadOnlyList<T> Items, long Total, int Page, int PageSize)
{
    public static PagedResult<T> Empty(int page, int pageSize) => new([], 0, page, pageSize);
}
