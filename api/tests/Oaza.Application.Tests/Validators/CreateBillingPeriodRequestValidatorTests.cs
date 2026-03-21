using FluentAssertions;
using Oaza.Application.DTOs;
using Oaza.Application.Validators;

namespace Oaza.Application.Tests.Validators;

public class CreateBillingPeriodRequestValidatorTests
{
    private readonly CreateBillingPeriodRequestValidator _sut = new();

    [Fact]
    public async Task Validate_ValidRequest_Passes()
    {
        var request = new CreateBillingPeriodRequest
        {
            Name = "1. pololetí 2025",
            DateFrom = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DateTo = new DateTime(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc),
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_DateFromAfterDateTo_Fails()
    {
        var request = new CreateBillingPeriodRequest
        {
            Name = "Invalid Period",
            DateFrom = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            DateTo = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DateFrom");
    }

    [Fact]
    public async Task Validate_DateFromEqualsDateTo_Fails()
    {
        var sameDate = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var request = new CreateBillingPeriodRequest
        {
            Name = "Same Day Period",
            DateFrom = sameDate,
            DateTo = sameDate,
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DateFrom");
    }

    [Fact]
    public async Task Validate_EmptyName_Fails()
    {
        var request = new CreateBillingPeriodRequest
        {
            Name = "",
            DateFrom = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DateTo = new DateTime(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc),
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public async Task Validate_WhitespaceName_Fails()
    {
        var request = new CreateBillingPeriodRequest
        {
            Name = "   ",
            DateFrom = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DateTo = new DateTime(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc),
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public async Task Validate_NameExceeds100Characters_Fails()
    {
        var request = new CreateBillingPeriodRequest
        {
            Name = new string('x', 101),
            DateFrom = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DateTo = new DateTime(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc),
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }
}
