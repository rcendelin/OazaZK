using FluentAssertions;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Services;

namespace Oaza.Domain.Tests.Services;

public class ComponentValidationTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static Participation P(string house, DateOnly from, DateOnly? to = null, decimal? weight = null) =>
        new() { ComponentId = "c", HouseId = house, ValidFrom = from, ValidTo = to, Weight = weight };

    private static ComponentAllocationRule R(AllocationMethod method, DateOnly from, DateOnly? to = null) =>
        new() { ComponentId = "c", Method = method, ValidFrom = from, ValidTo = to };

    [Fact]
    public void AdjacentParticipationsOfOneHouseAreValid()
    {
        ComponentValidation.CheckParticipations([P("A", D(2023, 11, 1), D(2026, 3, 14)), P("A", D(2026, 3, 15)), P("B", D(2023, 11, 1))])
            .Should().BeEmpty();
    }

    [Fact]
    public void OverlappingParticipationIsRejectedWithDates()
    {
        ComponentValidation.CheckParticipations([P("A", D(2023, 11, 1), D(2026, 3, 15)), P("A", D(2026, 3, 15))])
            .Should().ContainSingle().Which.Should().Be("Účast domu A se překrývá: 1. 11. 2023–15. 3. 2026 a od 15. 3. 2026.");
    }

    [Fact]
    public void OpenEndedParticipationOverlapsAnyLater()
    {
        ComponentValidation.CheckParticipations([P("A", D(2023, 11, 1)), P("A", D(2026, 1, 1), D(2026, 2, 1))])
            .Should().ContainSingle().Which.Should().Contain("od 1. 11. 2023");
    }

    [Fact]
    public void ReversedIntervalAndNegativeWeightAreRejected()
    {
        ComponentValidation.CheckParticipations([P("A", D(2026, 2, 2), D(2026, 2, 1)), P("B", D(2026, 1, 1), weight: -1)])
            .Should().BeEquivalentTo(
                "Účast domu A končí (1. 2. 2026) dřív, než začíná (2. 2. 2026).",
                "Váha účasti domu B od 1. 1. 2026 nesmí být záporná.");
    }

    [Fact]
    public void RulesMustNotOverlap()
    {
        ComponentValidation.CheckRules([R(AllocationMethod.Equal, D(2023, 11, 1), D(2026, 9, 30)), R(AllocationMethod.Ratio, D(2026, 10, 1))])
            .Should().BeEmpty();
        ComponentValidation.CheckRules([R(AllocationMethod.Equal, D(2023, 11, 1)), R(AllocationMethod.Ratio, D(2026, 10, 1))])
            .Should().ContainSingle().Which.Should().Be("Pravidla rozpočtu se překrývají: od 1. 11. 2023 a od 1. 10. 2026.");
        ComponentValidation.CheckRules([R(AllocationMethod.Equal, D(2026, 2, 2), D(2026, 2, 1))])
            .Should().ContainSingle().Which.Should().StartWith("Pravidlo rozpočtu končí");
    }

    [Fact]
    public void PercentSummingTo100IsValid()
    {
        var start = D(2023, 11, 1);
        ComponentValidation.CheckPercentSums(
                [R(AllocationMethod.Percent, start)],
                [P("A", start, weight: 40), P("B", start, weight: 35), P("C", start, weight: 25)])
            .Should().BeEmpty();
    }

    [Fact]
    public void PercentOffByOneHundredthIsRejectedWithDateAndSum()
    {
        var start = D(2023, 11, 1);
        ComponentValidation.CheckPercentSums(
                [R(AllocationMethod.Percent, start)],
                [P("A", start, weight: 33.33m), P("B", start, weight: 33.33m), P("C", start, weight: 33.33m)])
            .Should().ContainSingle().Which.Should().Be("K 1. 11. 2023 je součet procent účastníků 99,99 %, musí být 100 %.");
    }

    [Fact]
    public void PercentBreaksWhenAHouseLeavesWithoutReweighting()
    {
        var start = D(2023, 11, 1);
        ComponentValidation.CheckPercentSums(
                [R(AllocationMethod.Percent, start)],
                [P("A", start, weight: 50), P("B", start, D(2026, 3, 14), weight: 50)])
            .Should().ContainSingle().Which.Should().Be("K 15. 3. 2026 je součet procent účastníků 50 %, musí být 100 %.");
    }

    [Fact]
    public void PercentWithoutParticipantsIsRejected_OtherMethodsAreIgnored()
    {
        ComponentValidation.CheckPercentSums([R(AllocationMethod.Percent, D(2026, 1, 1), D(2026, 1, 31))], [])
            .Should().ContainSingle().Which.Should().Contain("0 %");
        ComponentValidation.CheckPercentSums([R(AllocationMethod.Equal, D(2026, 1, 1))], [P("A", D(2026, 1, 1), weight: 3)])
            .Should().BeEmpty();
    }

    [Fact]
    public void ChangeIntoClosedPeriodIsRejected()
    {
        ComponentValidation.CheckNotClosed(D(2026, 3, 14), D(2026, 3, 14))
            .Should().ContainSingle().Which.Should().Be("Změna od 14. 3. 2026 zasahuje do uzavřeného období (mezizávěrka k 14. 3. 2026).");
        ComponentValidation.CheckNotClosed(D(2026, 3, 15), D(2026, 3, 14)).Should().BeEmpty();
        ComponentValidation.CheckNotClosed(D(2020, 1, 1), null).Should().BeEmpty();
    }
}
