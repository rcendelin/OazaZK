using FluentAssertions;
using Oaza.Domain.Services;
using Oaza.Domain.Time;

namespace Oaza.Domain.Tests.Services;

public class RecurringAdvanceTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact]
    public void MonthlyForAYearGivesTwelvePeriods()
    {
        var periods = RecurringAdvance.Periods(D(2023, 11, 1), D(2024, 10, 31), AdvancePeriodicity.Monthly);

        periods.Should().HaveCount(12);
        periods[0].Should().Be(new DateRange(D(2023, 11, 1), D(2023, 11, 30)));
        periods[3].Should().Be(new DateRange(D(2024, 2, 1), D(2024, 2, 29)));
        periods[^1].Should().Be(new DateRange(D(2024, 10, 1), D(2024, 10, 31)));
    }

    [Fact]
    public void QuarterlyAndTruncatedLastPeriod()
    {
        RecurringAdvance.Periods(D(2024, 1, 1), D(2024, 8, 15), AdvancePeriodicity.Quarterly).Should().Equal(
            new DateRange(D(2024, 1, 1), D(2024, 3, 31)),
            new DateRange(D(2024, 4, 1), D(2024, 6, 30)),
            new DateRange(D(2024, 7, 1), D(2024, 8, 15)));
        RecurringAdvance.Periods(D(2024, 1, 1), D(2025, 12, 31), AdvancePeriodicity.HalfYearly).Should().HaveCount(4);
        RecurringAdvance.Periods(D(2024, 1, 1), D(2025, 12, 31), AdvancePeriodicity.Yearly).Should().HaveCount(2);
    }

    [Fact]
    public void StartOnThe31stDoesNotDrift()
    {
        RecurringAdvance.Periods(D(2025, 1, 31), D(2025, 4, 29), AdvancePeriodicity.Monthly).Select(p => p.From)
            .Should().Equal(D(2025, 1, 31), D(2025, 2, 28), D(2025, 3, 31));
    }

    [Fact]
    public void RejectsReversedTooLongAndUnknown()
    {
        var reversed = () => RecurringAdvance.Periods(D(2025, 2, 1), D(2025, 1, 1), AdvancePeriodicity.Monthly);
        reversed.Should().Throw<ArgumentException>();
        var tooLong = () => RecurringAdvance.Periods(D(2000, 1, 1), D(2030, 1, 1), AdvancePeriodicity.Monthly);
        tooLong.Should().Throw<ArgumentException>().WithMessage("*120*");
        var unknown = () => RecurringAdvance.Periods(D(2025, 1, 1), D(2025, 2, 1), (AdvancePeriodicity)5);
        unknown.Should().Throw<ArgumentOutOfRangeException>();
    }
}
