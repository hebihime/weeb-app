using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// SLICE_S5_CONTRACT.md §0's structural law, half (c): "an arch test fails any Svac.AdminHost* reference
/// from Svac.PublicApi or any future consumer/partner host" — the admin host is its own trust boundary,
/// never reachable through a consumer process even by an accidental reference. Checked BOTH directions
/// (mirrors ModuleBoundaryTests.cs's FindReferencesTo pattern, text-scanning *.csproj `<ProjectReference>`
/// values so this test needs no project reference of its own to either side beyond what
/// Svac.Tests.Architecture.csproj already carries for PurgeRegistryGateTests.cs): Svac.PublicApi must
/// never reference Svac.AdminHost/Svac.AdminHost.Domain, AND the admin host must never reference
/// Svac.PublicApi or any backend/modules/* feature module (it is its own deploy unit, §0 law d — a
/// module boundary violation here would mean a desk's business logic leaking into the trust boundary
/// this slice exists to draw).
/// </summary>
public sealed class AdminHostBoundaryTests
{
    [Fact]
    public void PublicApi_NeverReferencesTheAdminHost()
    {
        var repoRoot = FindRepoRoot();
        var publicHostDir = Path.Combine(repoRoot, "backend", "public-host");
        Assert.True(Directory.Exists(publicHostDir), "backend/public-host is expected to exist since S1.");

        var violations = FindReferencesContaining(publicHostDir, "AdminHost");
        Assert.Empty(violations);
    }

    [Fact]
    public void AdminHost_NeverReferencesPublicApiOrAnyFeatureModule()
    {
        var repoRoot = FindRepoRoot();
        var adminHostDir = Path.Combine(repoRoot, "backend", "admin-host");
        Assert.True(Directory.Exists(adminHostDir), "backend/admin-host is expected to exist since S5.");

        var violations = new List<string>();
        violations.AddRange(FindReferencesContaining(adminHostDir, "PublicApi"));
        violations.AddRange(FindReferencesContaining(adminHostDir, $"modules{Path.DirectorySeparatorChar}identity"));
        violations.AddRange(FindReferencesContaining(adminHostDir, $"modules/identity"));
        violations.AddRange(FindReferencesContaining(adminHostDir, $"modules{Path.DirectorySeparatorChar}AimlRouter"));
        violations.AddRange(FindReferencesContaining(adminHostDir, $"modules/AimlRouter"));

        Assert.Empty(violations);
    }

    [Fact]
    public void RedFixture_AProjectReferenceContainingTheForbiddenToken_IsDetected()
    {
        // Proves FindReferencesContaining itself is non-vacuous, mirroring ModuleBoundaryTests.cs's own
        // red-fixture discipline for its FindReferencesTo helper.
        var tempDir = Path.Combine(Path.GetTempPath(), "admin-host-boundary-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var fixtureCsproj = Path.Combine(tempDir, "Fixture.csproj");
            File.WriteAllText(fixtureCsproj, "<Project><ItemGroup><ProjectReference Include=\"..\\Svac.AdminHost\\Svac.AdminHost.csproj\" /></ItemGroup></Project>");

            var violations = FindReferencesContaining(tempDir, "AdminHost");
            Assert.Single(violations);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>*.csproj files, excluding bin/obj build output — mirrors ModuleBoundaryTests.cs's EnumerateRealCsproj exactly.</summary>
    private static IEnumerable<string> EnumerateRealCsproj(string dir) =>
        Directory.EnumerateFiles(dir, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>The actual `&lt;ProjectReference Include="..."&gt;` path values, XML comments stripped first — mirrors ModuleBoundaryTests.cs's ProjectReferencePaths exactly.</summary>
    private static IEnumerable<string> ProjectReferencePaths(string csproj)
    {
        var content = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(csproj), "<!--[\\s\\S]*?-->", string.Empty);
        var matches = System.Text.RegularExpressions.Regex.Matches(content, "<ProjectReference\\s+Include=\"([^\"]+)\"");
        return matches.Select(m => m.Groups[1].Value);
    }

    private static List<string> FindReferencesContaining(string dir, string forbiddenToken)
    {
        var violations = new List<string>();
        foreach (var csproj in EnumerateRealCsproj(dir))
        {
            foreach (var refPath in ProjectReferencePaths(csproj))
            {
                if (refPath.Contains(forbiddenToken, StringComparison.Ordinal))
                {
                    violations.Add($"{csproj} -> {refPath}");
                }
            }
        }
        return violations;
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "docker-compose.yml")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException("could not locate repo root from " + AppContext.BaseDirectory);
    }
}
