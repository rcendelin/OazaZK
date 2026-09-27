using System.Reflection;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Oaza.Domain.Enums;
using Oaza.Functions.Attributes;
using Oaza.Functions.Endpoints;

namespace Oaza.Functions.Tests.Endpoints;

/// <summary>T02 permissions: Admin and Accountant read cost components, only Admin writes.</summary>
public class CostComponentFunctionsAuthorizationTests
{
    private static IEnumerable<(MethodInfo Method, string[] Verbs, RequireRoleAttribute? Role)> Functions() =>
        typeof(CostComponentFunctions).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<FunctionAttribute>() is not null)
            .Select(m => (
                m,
                m.GetParameters().Select(p => p.GetCustomAttribute<HttpTriggerAttribute>()).Single(a => a is not null)!.Methods ?? [],
                m.GetCustomAttribute<RequireRoleAttribute>()));

    [Fact]
    public void WritesAreAdminOnly()
    {
        var writes = Functions().Where(f => !f.Verbs.Contains("get")).ToList();

        writes.Should().HaveCount(7);
        writes.Should().OnlyContain(f => f.Role != null && f.Role.Roles.SequenceEqual(new[] { UserRole.Admin }));
    }

    [Fact]
    public void ReadsAreAdminAndAccountant_NotMembers()
    {
        var reads = Functions().Where(f => f.Verbs.Contains("get")).ToList();

        reads.Should().HaveCount(3);
        reads.Should().OnlyContain(f => f.Role != null
            && f.Role.Roles.Contains(UserRole.Admin) && f.Role.Roles.Contains(UserRole.Accountant) && !f.Role.Roles.Contains(UserRole.Member));
    }
}
