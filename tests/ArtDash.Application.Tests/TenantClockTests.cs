using ArtDash.Application.Common.Extensions;

namespace ArtDash.Application.Tests;

/// <summary>"Today" belongs to the store's time zone, not to UTC: an order placed at 23h in São Paulo is today's.</summary>
public sealed class TenantClockTests
{
    private static readonly TimeZoneInfo SaoPaulo = TenantClock.Resolve("America/Sao_Paulo");

    [Fact]
    public void Day_bounds_follow_the_store_time_zone()
    {
        // 02:00 UTC on the 21st is still 23:00 on the 20th in São Paulo (UTC-3).
        var utcNow = new DateTime(2026, 10, 21, 2, 0, 0, DateTimeKind.Utc);

        var (start, end) = TenantClock.DayBoundsUtc(utcNow, SaoPaulo);

        start.ShouldBe(new DateTime(2026, 10, 20, 3, 0, 0));
        end.ShouldBe(new DateTime(2026, 10, 21, 3, 0, 0));
        TenantClock.LocalToday(utcNow, SaoPaulo).ShouldBe(new DateTime(2026, 10, 20));
    }

    [Fact]
    public void Day_offsets_move_whole_local_days()
    {
        var utcNow = new DateTime(2026, 10, 21, 15, 0, 0, DateTimeKind.Utc);

        var (start, _) = TenantClock.DayBoundsUtc(utcNow, SaoPaulo, -6);

        start.ShouldBe(new DateTime(2026, 10, 15, 3, 0, 0));
    }

    [Fact]
    public void Unknown_time_zones_fall_back_to_brasilia_time()
    {
        var zone = TenantClock.Resolve("Mars/Olympus_Mons");

        zone.GetUtcOffset(DateTime.UtcNow).ShouldBe(TimeSpan.FromHours(-3));
    }

    [Fact]
    public void Local_dates_convert_to_utc_filter_bounds()
    {
        var day = new DateTime(2026, 12, 31);

        TenantTime.StartOfDayUtc(day, SaoPaulo).ShouldBe(new DateTime(2026, 12, 31, 3, 0, 0));
        TenantTime.EndOfDayUtc(day, SaoPaulo).ShouldBe(new DateTime(2027, 1, 1, 3, 0, 0));
    }

    [Fact]
    public void A_day_with_a_skipped_midnight_starts_at_its_first_valid_instant()
    {
        var day = new DateTime(2018, 11, 4);

        var start = TenantTime.StartOfDayUtc(day, SaoPaulo);
        var end = TenantTime.EndOfDayUtc(day, SaoPaulo);

        // Historical time-zone transitions can have millisecond precision on Windows.
        start.ShouldBe(new DateTime(2018, 11, 4, 3, 0, 0, DateTimeKind.Utc), TimeSpan.FromMilliseconds(1));
        end.ShouldBe(new DateTime(2018, 11, 5, 2, 0, 0, DateTimeKind.Utc));
        (end - start).ShouldBe(TimeSpan.FromHours(23), TimeSpan.FromMilliseconds(1));
        TenantClock.ToLocal(start, SaoPaulo).Date.ShouldBe(day);
        TenantClock.ToLocal(start.AddTicks(-1), SaoPaulo).Date.ShouldBe(day.AddDays(-1));
    }

    [Fact]
    public void A_repeated_midnight_includes_both_occurrences()
    {
        var startTransition = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1), 10, 1);
        var endTransition = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 1, 0, 0), 11, 1);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1), startTransition, endTransition);
        var zone = TimeZoneInfo.CreateCustomTimeZone("RepeatedMidnight", TimeSpan.FromHours(-3),
            "Repeated midnight", "Standard", "Daylight", [rule]);
        var day = new DateTime(2026, 11, 1);

        var start = TenantTime.StartOfDayUtc(day, zone);
        var end = TenantTime.EndOfDayUtc(day, zone);

        start.ShouldBe(new DateTime(2026, 11, 1, 2, 0, 0, DateTimeKind.Utc));
        end.ShouldBe(new DateTime(2026, 11, 2, 3, 0, 0, DateTimeKind.Utc));
        (end - start).ShouldBe(TimeSpan.FromHours(25));
    }

    [Theory]
    [InlineData(-14)]
    [InlineData(0)]
    [InlineData(14)]
    public void Extreme_supported_report_dates_remain_valid_utc_boundaries(int hours)
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone($"Offset{hours}", TimeSpan.FromHours(hours), "Fixed", "Fixed");
        var minimum = DateOnly.MinValue.ToDateTime(TimeOnly.MinValue);
        var maximum = DateOnly.MaxValue.AddDays(-1).ToDateTime(TimeOnly.MinValue);

        var first = TenantTime.StartOfDayUtc(minimum, zone);
        var firstEnd = TenantTime.EndOfDayUtc(minimum, zone);
        var last = TenantTime.StartOfDayUtc(maximum, zone);
        var lastEnd = TenantTime.EndOfDayUtc(maximum, zone);

        first.Kind.ShouldBe(DateTimeKind.Utc);
        lastEnd.Kind.ShouldBe(DateTimeKind.Utc);
        firstEnd.ShouldBeGreaterThan(first);
        lastEnd.ShouldBeGreaterThan(last);
    }
}
