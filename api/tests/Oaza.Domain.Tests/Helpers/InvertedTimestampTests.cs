using Oaza.Domain.Helpers;

namespace Oaza.Domain.Tests.Helpers;

public class InvertedTimestampTests
{
    [Fact]
    public void FromDateTime_ShouldReturnNineteenCharacterString()
    {
        var dateTime = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        var result = InvertedTimestamp.FromDateTime(dateTime);

        Assert.Equal(19, result.Length);
    }

    [Fact]
    public void FromDateTime_RoundTrip_ShouldReturnOriginalDateTime()
    {
        var original = new DateTime(2025, 6, 15, 12, 30, 45, DateTimeKind.Utc);

        var inverted = InvertedTimestamp.FromDateTime(original);
        var restored = InvertedTimestamp.ToDateTime(inverted);

        Assert.Equal(original, restored);
    }

    [Fact]
    public void FromDateTime_NewerDatesShouldProduceSmallerValues()
    {
        var older = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        var olderKey = InvertedTimestamp.FromDateTime(older);
        var newerKey = InvertedTimestamp.FromDateTime(newer);

        // Newer dates should produce smaller inverted values (sort first in ascending order)
        Assert.True(string.CompareOrdinal(newerKey, olderKey) < 0,
            "Newer dates should produce smaller inverted timestamp strings for ascending sort.");
    }

    [Fact]
    public void FromDateTime_MinValue_ShouldNotThrow()
    {
        var result = InvertedTimestamp.FromDateTime(DateTime.MinValue);

        Assert.NotNull(result);
        Assert.Equal(19, result.Length);
    }

    [Fact]
    public void FromDateTime_MaxValue_ShouldProduceZeroPaddedString()
    {
        var result = InvertedTimestamp.FromDateTime(DateTime.MaxValue);

        // MaxValue ticks - MaxValue ticks = 0
        Assert.Equal("0000000000000000000", result);
    }

    [Fact]
    public void ToDateTime_ShouldReturnUtcKind()
    {
        var original = new DateTime(2025, 3, 21, 10, 0, 0, DateTimeKind.Utc);
        var inverted = InvertedTimestamp.FromDateTime(original);

        var result = InvertedTimestamp.ToDateTime(inverted);

        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Theory]
    [InlineData(2024, 1, 1)]
    [InlineData(2025, 6, 15)]
    [InlineData(2026, 12, 31)]
    public void FromDateTime_MultipleDates_ShouldRoundTrip(int year, int month, int day)
    {
        var original = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);

        var inverted = InvertedTimestamp.FromDateTime(original);
        var restored = InvertedTimestamp.ToDateTime(inverted);

        Assert.Equal(original, restored);
    }
}
