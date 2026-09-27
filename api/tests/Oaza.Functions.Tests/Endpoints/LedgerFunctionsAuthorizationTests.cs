using System.Reflection;
using FluentAssertions;
using Oaza.Functions.Attributes;
using Oaza.Functions.Endpoints;

namespace Oaza.Functions.Tests.Endpoints;

/// <summary>T07: the ledger is for every signed-in user (never anonymous); per-house scoping is in the use case.</summary>
public class LedgerFunctionsAuthorizationTests
{
    [Theory]
    [InlineData(nameof(LedgerFunctions.GetHouseLedgerAsync))]
    [InlineData(nameof(LedgerFunctions.GetOverviewAsync))]
    public void RequiresSignIn_NoRoleRestriction(string method)
    {
        var info = typeof(LedgerFunctions).GetMethod(method)!;

        info.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        info.GetCustomAttribute<RequireRoleAttribute>().Should().BeNull();
    }
}
