using FluentAssertions;
using Oaza.Application.DTOs;
using Oaza.Application.Validators;

namespace Oaza.Application.Tests.Validators;

public class UploadDocumentRequestValidatorTests
{
    private readonly UploadDocumentRequestValidator _sut = new();

    [Fact]
    public async Task Validate_ValidRequest_Passes()
    {
        var request = new UploadDocumentRequest
        {
            Name = "Stanovy spolku 2025",
            Category = "stanovy",
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("neplatna")]
    [InlineData("DOCUMENTS")]
    [InlineData("")]
    public async Task Validate_InvalidCategory_Fails(string category)
    {
        var request = new UploadDocumentRequest
        {
            Name = "Test dokument",
            Category = category,
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Category");
    }

    [Fact]
    public async Task Validate_EmptyName_Fails()
    {
        var request = new UploadDocumentRequest
        {
            Name = "",
            Category = "stanovy",
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public async Task Validate_NameTooLong_Fails()
    {
        var request = new UploadDocumentRequest
        {
            Name = new string('a', 201),
            Category = "stanovy",
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Theory]
    [InlineData("stanovy")]
    [InlineData("zapisy")]
    [InlineData("smlouvy")]
    [InlineData("ostatni")]
    [InlineData("Stanovy")]
    [InlineData("ZAPISY")]
    public async Task Validate_ValidCategories_Pass(string category)
    {
        var request = new UploadDocumentRequest
        {
            Name = "Test dokument",
            Category = category,
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }
}
