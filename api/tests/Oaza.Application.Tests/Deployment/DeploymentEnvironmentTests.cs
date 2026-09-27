using FluentAssertions;
using Oaza.Application.Deployment;

namespace Oaza.Application.Tests.Deployment;

public class DeploymentEnvironmentTests
{
    [Theory]
    [InlineData("dev", "dev")]
    [InlineData(" Development ", "dev")]
    [InlineData("TEST", "test")]
    [InlineData("testing", "test")]
    [InlineData("prod", "prod")]
    [InlineData("Production", "prod")]
    public void Normalize_KnownValues(string input, string expected)
    {
        DeploymentEnvironment.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("staging")]
    public void Normalize_MissingOrUnknown_IsUnknownSoTheBannerStaysVisible(string? input)
    {
        DeploymentEnvironment.Normalize(input).Should().Be(DeploymentEnvironment.Unknown);
    }
}
