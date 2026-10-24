using System.Linq.Expressions;

namespace ArtDash.Application.Common.Extensions;

public static class QueryExtensions
{
    public const int MaxPageSize = 200;

    public static IQueryable<T> WhereIf<T>(this IQueryable<T> query, bool condition, Expression<Func<T, bool>> predicate) =>
        condition ? query.Where(predicate) : query;

    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, total, page, pageSize);
    }

    /// <summary>Lower-cased, trimmed search term or null — for case-insensitive <c>Contains</c>.</summary>
    public static string? ToSearchTerm(this string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLower();
}

public static class StringExtensions
{
    public static string? NullIfWhiteSpace(this string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string Digits(this string? value) =>
        new((value ?? string.Empty).Where(char.IsDigit).ToArray());
}

/// <summary>Tenant-local calendar math (orders are stored in UTC, "today" depends on the tenant's time zone).</summary>
public static class TenantClock
{
    private static readonly TimeZoneInfo Fallback = TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT");

    public static TimeZoneInfo Resolve(string? timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone)
            ? zone
            : Fallback;

    public static DateTime ToLocal(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    /// <summary>UTC bounds of the local calendar day that contains <paramref name="utcNow"/>.</summary>
    public static (DateTime StartUtc, DateTime EndUtc) DayBoundsUtc(DateTime utcNow, TimeZoneInfo zone, int daysOffset = 0)
    {
        var localDate = ToLocal(utcNow, zone).Date.AddDays(daysOffset);
        var start = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified), zone);
        var end = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDate.AddDays(1), DateTimeKind.Unspecified), zone);
        return (start, end);
    }

    public static DateTime LocalToday(DateTime utcNow, TimeZoneInfo zone) => ToLocal(utcNow, zone).Date;
}
