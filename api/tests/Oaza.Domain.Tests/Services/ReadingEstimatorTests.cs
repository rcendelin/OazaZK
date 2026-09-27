using FluentAssertions;
using Oaza.Domain.Entities;
using Oaza.Domain.Services;

namespace Oaza.Domain.Tests.Services;

public class ReadingEstimatorTests
{
    private static MeterReading R(int month, int day, decimal value) => new()
    {
        MeterId = "m1",
        ReadingDate = new DateTime(2026, month, day, 0, 0, 0, DateTimeKind.Utc),
        Value = value,
    };

    private static readonly DateTime Jan16 = new(2026, 1, 16);

    [Fact]
    public void ExactDate_ReturnsRealReading_NotAnEstimate()
    {
        var result = ReadingEstimator.Estimate(new[] { R(1, 1, 250m), R(1, 16, 258.4m), R(1, 31, 271.71m) }, Jan16);

        result.Value.Should().Be(258.4m);
        result.Method.Should().Be(ReadingEstimateMethod.Exact);
        result.IsEstimate.Should().BeFalse();
    }

    [Fact]
    public void BetweenTwo_InterpolatesByDays_ToThreeDecimals()
    {
        // 250 on 1 Jan, 271.71 on 31 Jan (30 days); 16 Jan is day 15 → 250 + 21.71 × 15/30 = 260.855 (cf. S8).
        var result = ReadingEstimator.Estimate(new[] { R(1, 31, 271.71m), R(1, 1, 250m) }, Jan16);

        result.Value.Should().Be(260.855m);
        result.Method.Should().Be(ReadingEstimateMethod.Interpolated);
        result.IsEstimate.Should().BeTrue();
        result.Note.Should().Contain("interpolací").And.Contain("1. 1. 2026").And.Contain("31. 1. 2026").And.Contain("den 15 z 30");
    }

    [Fact]
    public void BetweenTwo_UsesNearestNeighbours_AndRoundsHalfAwayFromZero()
    {
        // Nearest before = 10 Jan (100), nearest after = 13 Jan (100.0015); 11 Jan is day 1 of 3 → 100.0005 → 100.001.
        var readings = new[] { R(1, 1, 90m), R(1, 10, 100m), R(1, 13, 100.0015m), R(2, 1, 120m) };

        var result = ReadingEstimator.Estimate(readings, new DateTime(2026, 1, 11));

        result.Value.Should().Be(100.001m);
    }

    [Fact]
    public void OnlyBefore_UsesNearestEarlierReading_WithDistance()
    {
        var result = ReadingEstimator.Estimate(new[] { R(1, 1, 250m), R(1, 6, 252m) }, Jan16);

        result.Value.Should().Be(252m);
        result.Method.Should().Be(ReadingEstimateMethod.NearestBefore);
        result.IsEstimate.Should().BeTrue();
        result.Note.Should().Contain("10 dní před");
    }

    [Fact]
    public void OnlyAfter_UsesNearestLaterReading_WithDistance()
    {
        var result = ReadingEstimator.Estimate(new[] { R(1, 31, 271.71m), R(1, 20, 262m) }, Jan16);

        result.Value.Should().Be(262m);
        result.Method.Should().Be(ReadingEstimateMethod.NearestAfter);
        result.Note.Should().Contain("4 dní po");
    }

    [Fact]
    public void NoReadings_ReturnsNone()
    {
        var result = ReadingEstimator.Estimate(Array.Empty<MeterReading>(), Jan16);

        result.Value.Should().BeNull();
        result.Method.Should().Be(ReadingEstimateMethod.None);
        result.IsEstimate.Should().BeTrue();
    }

    [Fact]
    public void TimeOfDayIsIgnored()
    {
        var withTime = new MeterReading { ReadingDate = new DateTime(2026, 1, 16, 23, 0, 0, DateTimeKind.Utc), Value = 5m };

        ReadingEstimator.Estimate(new[] { withTime }, new DateTime(2026, 1, 16, 8, 0, 0)).Method.Should().Be(ReadingEstimateMethod.Exact);
    }
}
