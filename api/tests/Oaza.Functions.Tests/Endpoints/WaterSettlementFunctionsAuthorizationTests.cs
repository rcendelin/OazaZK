using System.Reflection;
using FluentAssertions;
using Oaza.Domain.Enums;
using Oaza.Functions.Attributes;
using Oaza.Functions.Endpoints;

namespace Oaza.Functions.Tests.Endpoints;

public class WaterSettlementFunctionsAuthorizationTests
{
    [Fact]
    public void OverviewIsForAdminAndAccountant()
    {
        var role = typeof(WaterSettlementFunctions).GetMethod(nameof(WaterSettlementFunctions.GetAsync))!.GetCustomAttribute<RequireRoleAttribute>();

        role!.Roles.Should().BeEquivalentTo(new[] { UserRole.Admin, UserRole.Accountant });
    }
}
