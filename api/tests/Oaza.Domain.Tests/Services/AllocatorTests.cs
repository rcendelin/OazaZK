using FluentAssertions;
using Oaza.Domain.Services;

namespace Oaza.Domain.Tests.Services;

public class AllocatorTests
{
    [Fact]
    public void S1_Ratio_LargestRemainderGetsTheLeftoverHalere()
    {
        // 1000 Kč in the ratio 8 : 6 : 3 : 1 → 444,444 / 333,333 / 166,666 / 55,555.
        var parts = Allocator.Allocate(1000m, new[] { 8m, 6m, 3m, 1m });

        parts.Should().Equal(444.44m, 333.33m, 166.67m, 55.56m);
        parts.Sum().Should().Be(1000m);
    }

    [Fact]
    public void S7_EqualRemainders_AreResolvedByOrder()
    {
        var parts = Allocator.Allocate(1000m, new[] { 1m, 1m, 1m });

        parts.Should().Equal(333.34m, 333.33m, 333.33m);
    }

    [Fact]
    public void EqualRemainders_AreDeterministic()
    {
        var first = Allocator.Allocate(100m, new[] { 1m, 1m, 1m, 1m, 1m, 1m, 1m });
        var second = Allocator.Allocate(100m, new[] { 1m, 1m, 1m, 1m, 1m, 1m, 1m });

        first.Should().Equal(second);
        first.Should().Equal(14.29m, 14.29m, 14.29m, 14.29m, 14.28m, 14.28m, 14.28m);
    }

    [Fact]
    public void NegativeTotal_MirrorsPositiveAllocation()
    {
        // S2: a component credit of −20 000 Kč split among four houses.
        Allocator.Allocate(-20000m, new[] { 1m, 1m, 1m, 1m }).Should().Equal(-5000m, -5000m, -5000m, -5000m);
        Allocator.Allocate(-1000m, new[] { 1m, 1m, 1m }).Should().Equal(-333.34m, -333.33m, -333.33m);
    }

    [Fact]
    public void ZeroWeight_GetsNothing()
    {
        var parts = Allocator.Allocate(10m, new[] { 1m, 0m, 2m });

        parts.Should().Equal(3.33m, 0m, 6.67m);
    }

    [Fact]
    public void ZeroTotal_AllocatesZeros()
    {
        Allocator.Allocate(0m, new[] { 1m, 2m }).Should().Equal(0m, 0m);
    }

    [Fact]
    public void FractionalWeights_Work()
    {
        // Days in segments, consumption in m³ etc. are not integers.
        var parts = Allocator.Allocate(100m, new[] { 12.5m, 7.25m, 0.25m });

        parts.Should().Equal(62.50m, 36.25m, 1.25m);
    }

    [Theory]
    [InlineData(1234.56, new[] { 3.0, 7.0, 11.0, 13.0 })]
    [InlineData(0.05, new[] { 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 })]
    [InlineData(-999.99, new[] { 0.1, 0.2, 0.3 })]
    [InlineData(1000000, new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0 })]
    public void PartsAlwaysSumToTotal(double total, double[] weights)
    {
        var parts = Allocator.Allocate((decimal)total, weights.Select(w => (decimal)w).ToList());

        parts.Sum().Should().Be((decimal)total);
        parts.Should().OnlyContain(p => decimal.Round(p, 2) == p);
    }

    [Fact]
    public void KeyedVariant_ReturnsPartPerKey()
    {
        var parts = Allocator.Allocate(1000m, new List<KeyValuePair<string, decimal>>
        {
            new("A", 1m), new("B", 1m), new("C", 1m),
        });

        parts.Should().Equal(new Dictionary<string, decimal> { ["A"] = 333.34m, ["B"] = 333.33m, ["C"] = 333.33m });
    }

    [Fact]
    public void InvalidInput_Throws()
    {
        FluentActions.Invoking(() => Allocator.Allocate(10m, Array.Empty<decimal>())).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Allocator.Allocate(10m, new[] { 0m, 0m })).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Allocator.Allocate(10m, new[] { 1m, -1m })).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Allocator.Allocate(10.005m, new[] { 1m })).Should().Throw<ArgumentException>();
    }
}
