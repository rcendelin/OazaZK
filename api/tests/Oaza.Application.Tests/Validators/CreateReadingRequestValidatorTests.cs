using FluentAssertions;
using Oaza.Application.DTOs;
using Oaza.Application.Validators;
using Oaza.Domain.Time;

namespace Oaza.Application.Tests.Validators;

/// <summary>X5: "not in the future" means after today's calendar day in Europe/Prague.</summary>
public class CreateReadingRequestValidatorTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // 1. 2. 2026 00:30 in Prague (CET) is still 31. 1. 2026 in UTC.
    private static readonly PragueClock JustAfterMidnightInPrague =
        new(new FixedTimeProvider(new DateTimeOffset(2026, 1, 31, 23, 30, 0, TimeSpan.Zero)));

    private static CreateReadingRequest Reading(DateTime date) => new()
    {
        MeterId = Guid.NewGuid().ToString(),
        ReadingDate = date,
        Value = 100m,
    };

    [Fact]
    public async Task PragueTodayIsAllowedEvenWhenUtcIsStillYesterday()
    {
        var sut = new CreateReadingRequestValidator(JustAfterMidnightInPrague);

        var result = await sut.ValidateAsync(Reading(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task TomorrowInPragueIsRejected()
    {
        var sut = new CreateReadingRequestValidator(JustAfterMidnightInPrague);

        var result = await sut.ValidateAsync(Reading(new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "Datum odečtu nesmí být v budoucnosti.");
    }

    [Fact]
    public async Task DefaultConstructorUsesSystemPragueClock()
    {
        var result = await new CreateReadingRequestValidator().ValidateAsync(Reading(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        result.IsValid.Should().BeTrue();
    }
}
