using ArtDash.Application.Features.Reports;
using ArtDash.Shared.Contracts.Reports;

namespace ArtDash.Application.Tests;

public sealed class ReportValidationTests
{
    [Fact]
    public void Omitted_dates_and_channel_allow_the_tenant_defaults() =>
        Validate(new ReportQuery()).ShouldBeTrue();

    [Theory]
    [InlineData("2024-01-01", "2024-12-31", true)]
    [InlineData("2024-01-01", "2025-01-01", false)]
    [InlineData("2026-10-22", "2026-10-22", true)]
    [InlineData("2026-10-23", "2026-10-22", false)]
    public void Periods_must_be_ordered_and_at_most_366_inclusive_days(string from, string to, bool valid) =>
        Validate(new ReportQuery { From = DateOnly.Parse(from), To = DateOnly.Parse(to) }).ShouldBe(valid);

    [Theory]
    [InlineData(OrderChannel.Storefront, true)]
    [InlineData(OrderChannel.Backoffice, true)]
    [InlineData((OrderChannel)999, false)]
    public void Channels_must_be_known(OrderChannel channel, bool valid) =>
        Validate(new ReportQuery { Channel = channel }).ShouldBe(valid);

    [Fact]
    public void The_last_calendar_date_is_rejected_before_its_exclusive_end_can_overflow() =>
        Validate(new ReportQuery { From = DateOnly.MaxValue, To = DateOnly.MaxValue }).ShouldBeFalse();

    private static bool Validate(ReportQuery filter) =>
        new GetReportsQueryValidator().Validate(new GetReportsQuery(filter)).IsValid;
}