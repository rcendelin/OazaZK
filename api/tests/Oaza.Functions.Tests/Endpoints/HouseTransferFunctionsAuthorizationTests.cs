using System.Reflection;
using FluentAssertions;
using Oaza.Domain.Enums;
using Oaza.Functions.Attributes;
using Oaza.Functions.Endpoints;

namespace Oaza.Functions.Tests.Endpoints;

public class HouseTransferFunctionsAuthorizationTests
{
    [Theory]
    [InlineData(nameof(HouseTransferFunctions.PreviewAsync))]
    [InlineData(nameof(HouseTransferFunctions.TransferAsync))]
    public void TransferIsAdminOnly(string method)
    {
        typeof(HouseTransferFunctions).GetMethod(method)!.GetCustomAttribute<RequireRoleAttribute>()!.Roles
            .Should().Equal(UserRole.Admin);
    }
}
