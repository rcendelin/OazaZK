using FluentAssertions;
using Oaza.Application.DTOs;
using Oaza.Application.Validators;

namespace Oaza.Application.Tests.Validators;

public class CreateFinanceRequestValidatorTests
{
    private readonly CreateFinanceRequestValidator _sut = new();

    [Fact]
    public async Task Validate_ValidRequest_Passes()
    {
        var request = new CreateFinanceRequest
        {
            Type = "Income",
            Category = "voda",
            Amount = 15000m,
            Date = new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            Description = "Platba za vodu Q2 2025",
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("Invalid")]
    [InlineData("")]
    [InlineData("credit")]
    public async Task Validate_InvalidType_Fails(string type)
    {
        var request = new CreateFinanceRequest
        {
            Type = type,
            Category = "voda",
            Amount = 1000m,
            Date = new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            Description = "Test",
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Type");
    }

    [Theory]
    [InlineData("neplatna")]
    [InlineData("")]
    [InlineData("water")]
    public async Task Validate_InvalidCategory_Fails(string category)
    {
        var request = new CreateFinanceRequest
        {
            Type = "Expense",
            Category = category,
            Amount = 1000m,
            Date = new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            Description = "Test",
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Category");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(-0.01)]
    public async Task Validate_AmountLessThanOrEqualToZero_Fails(decimal amount)
    {
        var request = new CreateFinanceRequest
        {
            Type = "Income",
            Category = "voda",
            Amount = amount,
            Date = new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            Description = "Test",
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Amount");
    }

    [Fact]
    public async Task Validate_EmptyDescription_Fails()
    {
        var request = new CreateFinanceRequest
        {
            Type = "Expense",
            Category = "elektro",
            Amount = 5000m,
            Date = new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            Description = "",
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Description");
    }
}
