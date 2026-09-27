using FluentAssertions;
using Oaza.Application.Deployment;

namespace Oaza.Application.Tests.Deployment;

public class StorageIsolationTests
{
    [Theory]
    [InlineData("prod", "stoaza")]
    [InlineData("production", "stoazaprod")]
    [InlineData("test", "stoazatest")]
    [InlineData("test", "devstoreaccount1")]
    [InlineData("dev", "stoazadev")]
    [InlineData("dev", "devstoreaccount1")]
    [InlineData("", "stoaza")]
    [InlineData("something", "stoazatest")]
    public void OwnAccount_IsFine(string environment, string account) =>
        StorageIsolation.Violation(environment, account).Should().BeNull();

    [Theory]
    [InlineData("prod", "stoazatest", "Produkce")]
    [InlineData("prod", "stoazadev", "Produkce")]
    [InlineData("PROD", "devstoreaccount1", "Produkce")]
    [InlineData("test", "stoaza", "Testovací")]
    [InlineData("test", "stoazadev", "Testovací")]
    [InlineData("dev", "stoaza", "Vývojové")]
    [InlineData("dev", "stoazatest", "Vývojové")]
    public void AnotherEnvironmentsAccount_IsRefused(string environment, string account, string start) =>
        StorageIsolation.Violation(environment, account).Should().StartWith(start).And.Contain(account);
}
