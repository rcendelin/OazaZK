using System.Reflection;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;

namespace Oaza.Functions.Tests.Endpoints;

/// <summary>
/// The Functions host registers endpoints by <c>[Function]</c> name: two methods with the same name silently leave one
/// route out of the deployment (found on DEV: POST /opening-balances answered 404). Names and method+route pairs must
/// be unique.
/// </summary>
public class FunctionRegistrationTests
{
    private static readonly IReadOnlyList<(string Name, string Where, string[] Methods, string? Route)> Functions =
        typeof(Oaza.Functions.Endpoints.ModelEndpoint).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(m => (Method: m, Function: m.GetCustomAttribute<FunctionAttribute>()))
            .Where(x => x.Function is not null)
            .Select(x =>
            {
                var trigger = x.Method.GetParameters().Select(p => p.GetCustomAttribute<HttpTriggerAttribute>()).FirstOrDefault(a => a is not null);
                return (x.Function!.Name, $"{x.Method.DeclaringType!.Name}.{x.Method.Name}", trigger?.Methods ?? [], trigger?.Route);
            })
            .ToList();

    [Fact]
    public void FunctionNames_AreUnique()
    {
        Functions.Should().HaveCountGreaterThan(90);
        var duplicates = Functions.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(f => f.Where))}");
        duplicates.Should().BeEmpty();
    }

    [Fact]
    public void MethodAndRoute_AreUnique()
    {
        var duplicates = Functions.Where(f => f.Route is not null)
            .SelectMany(f => f.Methods.Select(m => (Key: $"{m.ToUpperInvariant()} {f.Route!.ToLowerInvariant()}", f.Where)))
            .GroupBy(x => x.Key).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(x => x.Where))}");
        duplicates.Should().BeEmpty();
    }
}

public class FileResponseTests
{
    [Theory]
    [InlineData("Zápis ze schůze 2026.pdf", "attachment; filename=\"Zapis ze schuze 2026.pdf\"; filename*=UTF-8''Z%C3%A1pis%20ze%20sch%C5%AFze%202026.pdf")]
    [InlineData("stanovy.pdf", "attachment; filename=\"stanovy.pdf\"; filename*=UTF-8''stanovy.pdf")]
    [InlineData("a\"b;c\r\n.pdf", "attachment; filename=\"a_b_c.pdf\"; filename*=UTF-8''a%22b%3Bc.pdf")]
    [InlineData("", "attachment; filename=\"soubor\"; filename*=UTF-8''soubor")]
    public void ContentDisposition_IsAsciiSafe_WithUtf8Name(string name, string expected) =>
        Oaza.Functions.Endpoints.FileResponse.ContentDisposition(name).Should().Be(expected);
}
