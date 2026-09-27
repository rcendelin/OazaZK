using System.Reflection;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Oaza.Domain.Enums;
using Oaza.Functions.Attributes;
using Oaza.Functions.Endpoints;

namespace Oaza.Functions.Tests.Endpoints;

/// <summary>T03 permissions: Admin and Accountant read opening balances, only Admin writes.</summary>
public class OpeningBalanceFunctionsAuthorizationTests
{
    private static List<(string[] Verbs, RequireRoleAttribute? Role)> Functions() =>
        typeof(OpeningBalanceFunctions).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<FunctionAttribute>() is not null)
            .Select(m => (
                m.GetParameters().Select(p => p.GetCustomAttribute<HttpTriggerAttribute>()).Single(a => a is not null)!.Methods ?? [],
                m.GetCustomAttribute<RequireRoleAttribute>()))
            .ToList();

    [Fact]
    public void WritesAreAdminOnly_ReadsAdminAndAccountant()
    {
        var functions = Functions();
        var writes = functions.Where(f => !f.Verbs.Contains("get")).ToList();
        var reads = functions.Where(f => f.Verbs.Contains("get")).ToList();

        writes.Should().HaveCount(4);
        writes.Should().OnlyContain(f => f.Role != null && f.Role.Roles.SequenceEqual(new[] { UserRole.Admin }));
        reads.Should().HaveCount(3);
        reads.Should().OnlyContain(f => f.Role != null && f.Role.Roles.Contains(UserRole.Accountant) && !f.Role.Roles.Contains(UserRole.Member));
    }
}
