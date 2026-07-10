using Svac.DomainCore.Contracts;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// 1A module-boundary proof (SLICE_S1_CONTRACT.md §1a): Svac.DomainCore.Contracts is the ONLY assembly a
/// future feature module may reference. No `backend/modules/*` exist yet (S2+), so the cross-module half
/// of this rule is vacuous at S1 by construction — recorded here rather than silently skipped, and armed
/// the moment the first module assembly appears (this test enumerates `backend/modules/*.csproj` and
/// fails the instant one references Svac.DomainCore or Svac.DomainCore.Hosting directly). The half that
/// IS checkable today: Svac.DomainCore.Contracts itself carries zero EF/ASP.NET/IO dependencies, so
/// referencing it can never accidentally pull in the internal implementation assembly's surface.
/// </summary>
public sealed class ModuleBoundaryTests
{
    [Fact]
    public void ContractsAssembly_HasNoEfCoreOrAspNetCoreDependency()
    {
        var assembly = typeof(RequestContext).Assembly;
        var forbidden = new[] { "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql" };

        var violations = assembly.GetReferencedAssemblies()
            .Where(r => forbidden.Any(f => r.Name?.StartsWith(f, StringComparison.OrdinalIgnoreCase) == true))
            .Select(r => r.Name)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void NoFeatureModuleExistsYet_TheCrossModuleHalfOfThisRuleIsVacuousByRecordedDesign()
    {
        var repoRoot = FindRepoRoot();
        var modulesDir = Path.Combine(repoRoot, "backend", "modules");

        // §0: "S1 does NOT ... build any feature module (backend/modules/* are S2+)". If a module
        // lands, its .csproj must reference ONLY *.Contracts assemblies from domain-core — this test
        // enumerates that the moment it becomes non-vacuous.
        if (!Directory.Exists(modulesDir))
        {
            return; // guarded — recorded, not silently skipped: this branch IS the assertion at S1.
        }

        var violations = new List<string>();
        foreach (var csproj in Directory.EnumerateFiles(modulesDir, "*.csproj", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(csproj);
            if (content.Contains("Svac.DomainCore.csproj", StringComparison.Ordinal) ||
                content.Contains("Svac.DomainCore.Hosting.csproj", StringComparison.Ordinal))
            {
                violations.Add(csproj);
            }
        }

        Assert.Empty(violations);
    }

    private static string FindRepoRoot()
    {
        // Marker is docker-compose.yml at the true repo root (backend/Svac.sln lives one level BELOW
        // that and would misidentify backend/ itself as root).
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "docker-compose.yml")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException("could not locate repo root from " + AppContext.BaseDirectory);
    }
}
