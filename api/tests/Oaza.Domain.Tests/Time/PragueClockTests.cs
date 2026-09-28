using FluentAssertions;
using Oaza.Domain.Time;

namespace Oaza.Domain.Tests.Time;

public class PragueClockTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static PragueClock At(string utc) =>
        new(new FixedTimeProvider(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    // Winter (CET, UTC+1): 23:30 UTC is already the next day in Prague, 22:30 UTC is not.
    [InlineData("2026-01-31T23:30:00Z", "2026-02-01")]
    [InlineData("2026-01-31T22:30:00Z", "2026-01-31")]
    // Summer (CEST, UTC+2): 22:30 UTC is already the next day, 21:30 UTC is not.
    [InlineData("2026-06-30T22:30:00Z", "2026-07-01")]
    [InlineData("2026-06-30T21:30:00Z", "2026-06-30")]
    // New Year's night: the accounting year changes by Prague time.
    [InlineData("2025-12-31T23:15:00Z", "2026-01-01")]
    public void TodayIsTheCalendarDayInPrague(string utcNow, string expected)
    {
        At(utcNow).Today.Should().Be(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    // Daylight saving switches (last Sunday of March / October 2026).
    [InlineData("2026-03-28T23:30:00Z", "2026-03-29")]
    [InlineData("2026-03-29T21:59:00Z", "2026-03-29")]
    [InlineData("2026-03-29T22:00:00Z", "2026-03-30")]
    [InlineData("2026-10-24T21:59:00Z", "2026-10-24")]
    [InlineData("2026-10-24T22:00:00Z", "2026-10-25")]
    [InlineData("2026-10-25T22:59:00Z", "2026-10-25")]
    [InlineData("2026-10-25T23:00:00Z", "2026-10-26")]
    public void TodayFollowsDaylightSavingSwitches(string utcNow, string expected)
    {
        At(utcNow).Today.Should().Be(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void NowIsTheProviderInstant()
    {
        var instant = new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);
        new PragueClock(new FixedTimeProvider(instant)).Now.Should().Be(instant);
    }

    [Fact]
    public void AsUtcMidnightKeepsTheDay()
    {
        var midnight = PragueClock.AsUtcMidnight(new DateOnly(2026, 9, 27));
        midnight.Should().Be(new DateTime(2026, 9, 27, 0, 0, 0, DateTimeKind.Utc));
        midnight.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void SystemClockAndZoneExist()
    {
        PragueClock.Zone.BaseUtcOffset.Should().Be(TimeSpan.FromHours(1));
        PragueClock.System.Today.Should().BeOnOrAfter(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public void RejectsNullProvider()
    {
        var act = () => new PragueClock(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
