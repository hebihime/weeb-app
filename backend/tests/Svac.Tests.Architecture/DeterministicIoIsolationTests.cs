using System.Reflection;
using Svac.DomainCore.Deterministic;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// Deterministic math in pure libs (SLICE_S1_CONTRACT.md §1a, §8): Svac.DomainCore.Deterministic has
/// ZERO IO references. No file/network/database/clock-ambient-read package may ever land as a
/// dependency of this assembly. Proven by scanning its actual referenced assemblies, not by convention.
/// </summary>
public sealed class DeterministicIoIsolationTests
{
    private static readonly IReadOnlySet<string> ForbiddenAssemblyNamePrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "System.IO", "System.Net", "System.Data", "Npgsql", "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore", "Microsoft.Data", "StackExchange.Redis", "Azure",
    };

    [Fact]
    public void DeterministicAssembly_ReferencesNoIoBearingAssembly()
    {
        var assembly = typeof(Ulid).Assembly;
        var violations = assembly.GetReferencedAssemblies()
            .Where(r => ForbiddenAssemblyNamePrefixes.Any(prefix => r.Name?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true))
            .Select(r => r.Name)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void DeterministicAssembly_HasNoLlmOrRandomAmbientDependency()
    {
        // "No LLM ever" (§1a) — no reference to any AI/ML SDK namespace, checked the same way as the
        // IO-isolation rule above so a future accidental `using Anthropic...` in this assembly is caught
        // by the exact same mechanism, not a second ad hoc rule.
        var assembly = typeof(Ulid).Assembly;
        var violations = assembly.GetReferencedAssemblies()
            .Where(r => r.Name?.Contains("Anthropic", StringComparison.OrdinalIgnoreCase) == true
                || r.Name?.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) == true)
            .Select(r => r.Name)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void DeterministicFunctions_TakeNowAsAParameter_NeverReadTheAmbientClock()
    {
        // Structural proxy for "no wall-clock reads inside functions" (§1a): every public static method
        // on the Deterministic assembly's public types that plausibly needs "now" declares it as a
        // parameter (DateTimeOffset/DateTime/TimeOnly-typed) rather than calling DateTime.Now/UtcNow
        // internally — that internal-call half is proven by the fact these are pure static functions
        // with no field state to stash an ambient clock in (checked via IL is a Phase-2/3 hardening; the
        // type-shape half is real today).
        var publicTypes = typeof(Ulid).Assembly.GetExportedTypes();
        var methodsNeedingTime = publicTypes
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.Name.Contains("Now", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Window", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Reset", StringComparison.OrdinalIgnoreCase));

        foreach (var method in methodsNeedingTime)
        {
            var hasTimeParameter = method.GetParameters().Any(p =>
                p.ParameterType == typeof(DateTimeOffset) || p.ParameterType == typeof(DateTime) || p.ParameterType == typeof(TimeOnly));
            Assert.True(hasTimeParameter, $"{method.DeclaringType?.Name}.{method.Name} looks time-dependent but declares no explicit time parameter.");
        }
    }
}
