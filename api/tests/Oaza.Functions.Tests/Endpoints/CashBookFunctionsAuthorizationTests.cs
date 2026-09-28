using System.Reflection;
using FluentAssertions;
using Oaza.Domain.Enums;
using Oaza.Functions.Attributes;
using Oaza.Functions.Endpoints;

namespace Oaza.Functions.Tests.Endpoints;

/// <summary>T09: members read the cash book (transparency); Admin and Accountant write and export.</summary>
public class CashBookFunctionsAuthorizationTests
{
    [Fact]
    public void MembersReadAdminAndAccountantWrite()
    {
        var type = typeof(CashBookFunctions);
        type.GetMethod(nameof(CashBookFunctions.ListAsync))!.GetCustomAttribute<RequireRoleAttribute>().Should().BeNull();
        type.GetMethod(nameof(CashBookFunctions.ListAsync))!.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        foreach (var method in new[] { nameof(CashBookFunctions.CreateAsync), nameof(CashBookFunctions.StornoAsync), nameof(CashBookFunctions.ExportAsync) })
            type.GetMethod(method)!.GetCustomAttribute<RequireRoleAttribute>()!.Roles.Should().BeEquivalentTo(new[] { UserRole.Admin, UserRole.Accountant });
    }
}
