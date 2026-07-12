using Svac.DomainCore.Contracts;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// 1A module-boundary proof (SLICE_S1_CONTRACT.md §1a). Two halves:
/// (1) Svac.DomainCore.Contracts itself carries zero EF/ASP.NET/IO dependencies — referencing it can
///     never accidentally pull in the internal implementation assembly's surface.
/// (2) SLICE_S3_CONTRACT.md §8/§0 item 8's "first two-feature-module exercise": feature modules are
///     SIBLINGS, never coupled to each other — identity and AimlRouter must mutually never reference one
///     another's Contracts OR internal assembly. This SUPERSEDES the Phase-1-era blanket rule ("no module
///     may reference Svac.DomainCore/.Hosting directly") — that blanket rule is now known to be WRONG: S3
///     BUILD's IdentityDbContext + session-backed IBearerAuthenticator legitimately need
///     Svac.DomainCore/Svac.DomainCore.Hosting (the SHARED substrate every host already references
///     directly, S1's own Svac.PublicApi included) — the real boundary that must hold forever is
///     module-to-module, not module-to-substrate.
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
    public void FeatureModules_Identity_And_AimlRouter_NeverReferenceEachOther()
    {
        var repoRoot = FindRepoRoot();
        var identityDir = Path.Combine(repoRoot, "backend", "modules", "identity");
        var aimlRouterDir = Path.Combine(repoRoot, "backend", "modules", "AimlRouter");

        Assert.True(Directory.Exists(identityDir), "backend/modules/identity is expected to exist by S3 BUILD.");
        Assert.True(Directory.Exists(aimlRouterDir), "backend/modules/AimlRouter is expected to exist since S2.");

        var identityViolations = FindReferencesTo(identityDir, "Svac.AimlRouter");
        var aimlRouterViolations = FindReferencesTo(aimlRouterDir, "Svac.Identity");

        Assert.Empty(identityViolations);
        Assert.Empty(aimlRouterViolations);
    }

    [Fact]
    public void Identity_ReferencesOnlyItsOwnContractsAndDomainCore_NeverAnotherFeatureModule()
    {
        var repoRoot = FindRepoRoot();
        var identityDir = Path.Combine(repoRoot, "backend", "modules", "identity");
        var modulesDir = Path.Combine(repoRoot, "backend", "modules");

        var otherModuleNames = Directory.EnumerateDirectories(modulesDir)
            .Select(Path.GetFileName)
            .Where(name => name is not null && name != "identity")
            .Select(name => name!)
            .ToList();
        Assert.NotEmpty(otherModuleNames); // AimlRouter, at minimum — proves this check is non-vacuous.

        var violations = new List<string>();
        foreach (var csproj in EnumerateRealCsproj(identityDir))
        {
            foreach (var refPath in ProjectReferencePaths(csproj))
            {
                foreach (var otherModule in otherModuleNames)
                {
                    if (refPath.Contains($"modules\\{otherModule}\\", StringComparison.OrdinalIgnoreCase) ||
                        refPath.Contains($"modules/{otherModule}/", StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add($"{csproj} references sibling module \"{otherModule}\" via {refPath}");
                    }
                }
            }
        }

        Assert.Empty(violations);
    }

    /// <summary>*.csproj files, excluding bin/obj build output (which can contain generated copies/props with unrelated content) — never the source tree's own doc comments (which legitimately name a sibling module's file as prose, e.g. "mirrors backend/modules/AimlRouter/...csproj's shape").</summary>
    private static IEnumerable<string> EnumerateRealCsproj(string dir) =>
        Directory.EnumerateFiles(dir, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>The actual `&lt;ProjectReference Include="..."&gt;` path values — XML comments stripped first, so a doc comment merely NAMING a sibling module's csproj (as prose, e.g. "mirrors ...AimlRouter....csproj's shape") is never mistaken for a real reference.</summary>
    private static IEnumerable<string> ProjectReferencePaths(string csproj)
    {
        var content = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(csproj), "<!--[\\s\\S]*?-->", string.Empty);
        var matches = System.Text.RegularExpressions.Regex.Matches(content, "<ProjectReference\\s+Include=\"([^\"]+)\"");
        return matches.Select(m => m.Groups[1].Value);
    }

    private static List<string> FindReferencesTo(string moduleDir, string forbiddenAssemblyNamePrefix)
    {
        var violations = new List<string>();
        foreach (var csproj in EnumerateRealCsproj(moduleDir))
        {
            foreach (var refPath in ProjectReferencePaths(csproj))
            {
                if (refPath.Contains($"{forbiddenAssemblyNamePrefix}.csproj", StringComparison.Ordinal) ||
                    refPath.Contains($"{forbiddenAssemblyNamePrefix}.Contracts.csproj", StringComparison.Ordinal))
                {
                    violations.Add($"{csproj} -> {refPath}");
                }
            }
        }
        return violations;
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
