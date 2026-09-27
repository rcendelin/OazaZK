using System.Reflection;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Oaza.Domain.Enums;
using Oaza.Functions.Attributes;
using Oaza.Functions.Endpoints;

namespace Oaza.Functions.Tests.Endpoints;

public class OffBookFundFunctionsAuthorizationTests
{
    [Fact]
    public void MembersReadAdminWrites_NothingAnonymous()
    {
        var functions = typeof(OffBookFundFunctions).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<FunctionAttribute>() is not null)
            .Select(m => (
                Verbs: m.GetParameters().Select(p => p.GetCustomAttribute<HttpTriggerAttribute>()).Single(a => a is not null)!.Methods ?? [],
                Role: m.GetCustomAttribute<RequireRoleAttribute>(),
                Anonymous: m.GetCustomAttribute<AllowAnonymousAttribute>()))
            .ToList();

        functions.Should().OnlyContain(f => f.Anonymous == null);
        functions.Where(f => f.Verbs.Contains("get")).Should().HaveCount(3).And.OnlyContain(f => f.Role == null);
        functions.Where(f => !f.Verbs.Contains("get")).Should().HaveCount(4)
            .And.OnlyContain(f => f.Role != null && f.Role.Roles.SequenceEqual(new[] { UserRole.Admin }));
    }
}
