using System.Reflection;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Oaza.Domain.Enums;
using Oaza.Functions.Attributes;
using Oaza.Functions.Endpoints;

namespace Oaza.Functions.Tests.Endpoints;

public class InterimClosingFunctionsAuthorizationTests
{
    [Fact]
    public void WritesAreAdminOnly_ReadsAdminAndAccountant()
    {
        var functions = typeof(InterimClosingFunctions).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<FunctionAttribute>() is not null)
            .Select(m => (
                Verbs: m.GetParameters().Select(p => p.GetCustomAttribute<HttpTriggerAttribute>()).Single(a => a is not null)!.Methods ?? [],
                Role: m.GetCustomAttribute<RequireRoleAttribute>()))
            .ToList();

        functions.Where(f => !f.Verbs.Contains("get")).Should().HaveCount(2)
            .And.OnlyContain(f => f.Role != null && f.Role.Roles.SequenceEqual(new[] { UserRole.Admin }));
        functions.Where(f => f.Verbs.Contains("get")).Should().HaveCount(2)
            .And.OnlyContain(f => f.Role != null && f.Role.Roles.Contains(UserRole.Accountant) && !f.Role.Roles.Contains(UserRole.Member));
    }
}
