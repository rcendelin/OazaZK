using FluentAssertions;
using Oaza.Domain.Helpers;

namespace Oaza.Domain.Tests.Helpers;

public class BankAccountNumberTests
{
    [Theory]
    [InlineData("107-2222222222/0100", "107-2222222222_0100", "107-2222222222/0100")]
    [InlineData("111111111/0300", "111111111_0300", "111111111/0300")]
    [InlineData(" 000107-0002222222222 / 0100 ", "107-2222222222_0100", "107-2222222222/0100")]
    [InlineData("0-111111111/0300", "111111111_0300", "111111111/0300")]
    public void TryParse_NormalizesSpellingsOfTheSameAccount(string input, string expectedKey, string expectedDisplay)
    {
        BankAccountNumber.TryParse(input, out var account).Should().BeTrue();

        account!.ToKey().Should().Be(expectedKey);
        account.ToString().Should().Be(expectedDisplay);
        BankAccountNumber.KeyToDisplay(account.ToKey()).Should().Be(expectedDisplay);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("111111111")]        // missing bank code
    [InlineData("111111111/300")]    // bank code must have 4 digits
    [InlineData("abc/0300")]
    [InlineData("0000000/0300")]     // number of zeros only
    public void TryParse_RejectsInvalidInput(string? input)
    {
        BankAccountNumber.TryParse(input, out var account).Should().BeFalse();
        account.Should().BeNull();
    }

    [Fact]
    public void FromParts_CombinesCsvColumns()
    {
        BankAccountNumber.FromParts("107-2222222222", "0100")!.ToKey().Should().Be("107-2222222222_0100");
        BankAccountNumber.FromParts(null, "0100").Should().BeNull();
        BankAccountNumber.FromParts("111111111", "").Should().BeNull();
    }
}
