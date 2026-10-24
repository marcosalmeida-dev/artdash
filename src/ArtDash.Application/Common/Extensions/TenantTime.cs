namespace ArtDash.Application.Common.Extensions;

/// <summary>Reads the current tenant's time zone, for "today", due dates and date filters.</summary>
public static class TenantTime
{
    public static async Task<TimeZoneInfo> GetZoneAsync(IApplicationDbContext db, ICurrentTenant currentTenant, CancellationToken cancellationToken)
    {
        var tenantId = currentTenant.RequiredId();
        var zoneId = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Store.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);

        return TenantClock.Resolve(zoneId);
    }

    public static DateOnly Today(DateTime utcNow, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TenantClock.LocalToday(utcNow, zone));

    /// <summary>First UTC instant of a local calendar date, including midnight clock changes.</summary>
    public static DateTime StartOfDayUtc(DateTime localDate, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            // Some zones advance the clock at midnight, so that calendar day starts later.
            // UTC offsets are bounded by fourteen hours. Search actual UTC instants so that
            // historical transition precision and entirely skipped dates are handled as well.
            var earliest = Math.Max(DateTime.MinValue.Ticks, local.Ticks - 14 * TimeSpan.TicksPerHour);
            var latest = Math.Min(DateTime.MaxValue.Ticks, local.Ticks + 14 * TimeSpan.TicksPerHour);
            while (earliest < latest)
            {
                var middle = earliest + (latest - earliest) / 2;
                var mappedDate = TenantClock.ToLocal(new DateTime(middle, DateTimeKind.Utc), zone).Date;
                if (mappedDate < local.Date)
                {
                    earliest = middle + 1;
                }
                else
                {
                    latest = middle;
                }
            }

            return new DateTime(earliest, DateTimeKind.Utc);
        }

        // A repeated midnight belongs to the date twice: include its earlier occurrence too.
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        var utcTicks = Math.Clamp(local.Ticks - offset.Ticks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks);
        return new DateTime(utcTicks, DateTimeKind.Utc);
    }

    /// <summary>UTC start of the day after a local calendar date (exclusive upper bound).</summary>
    public static DateTime EndOfDayUtc(DateTime localDate, TimeZoneInfo zone) =>
        StartOfDayUtc(localDate.Date.AddDays(1), zone);
}
