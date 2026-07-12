using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// SECURITY_REVIEW_S5.md S5-04's own arch guard: "AddDataProtection is followed by a key-protection
/// call." A text scan (mirrors AdminActionChokepointArchTests.cs's own text-scan discipline for a
/// cross-cutting invariant a pure assembly-reference/reflection check cannot express) over every real
/// <c>.cs</c> file under <c>backend/admin-host</c> — any file calling <c>.AddDataProtection(</c> must ALSO
/// call <c>.ProtectKeysWithAzureKeyVault(</c> somewhere in that SAME file. Before S5-04's fix,
/// StaffAuthServiceCollectionExtensions.cs called <c>.AddDataProtection()</c> and chained ONLY
/// <c>.AddKeyManagementOptions(... CoreDbXmlRepository ...)</c> — the key ring's XML payload (raw
/// cookie/antiforgery signing key material) was persisted to <c>core.data_protection_keys</c> in
/// PLAINTEXT unconditionally, prod/staging included. This guard exists so a future refactor can never
/// silently strip the Key Vault chaining back out without a red arch-test catching it — the SAME "the
/// door cannot be walked around" property AdminActionChokepointArchTests already enforces for the
/// audited-action chokepoint.
/// </summary>
public sealed class DataProtectionKeyProtectionArchTests
{
    [Fact]
    public void EveryFileThatCallsAddDataProtection_AlsoCallsProtectKeysWithAzureKeyVault_InTheSameFile()
    {
        var adminHostDir = Path.Combine(FindRepoRoot(), "backend", "admin-host");
        Assert.True(Directory.Exists(adminHostDir), "backend/admin-host is expected to exist since S5.");

        var violations = new List<string>();
        foreach (var path in EnumerateRealCsFiles(adminHostDir))
        {
            var content = File.ReadAllText(path);
            if (ViolatesTheRule(content))
            {
                violations.Add(path);
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void RedFixture_AddDataProtectionWithNoKeyProtectionCall_IsFlagged()
    {
        const string fixtureSource = """
            namespace Fixture;

            public static class RogueDataProtectionSetup
            {
                public static IServiceCollection AddSomething(this IServiceCollection services, string connectionString)
                {
                    services.AddDataProtection()
                        .SetApplicationName("rogue")
                        .AddKeyManagementOptions(o => o.XmlRepository = new CoreDbXmlRepository(connectionString));
                    return services;
                }
            }
            """;

        Assert.True(ViolatesTheRule(fixtureSource));
    }

    [Fact]
    public void GreenFixture_AddDataProtectionChainedWithProtectKeysWithAzureKeyVault_IsNotFlagged()
    {
        const string fixtureSource = """
            namespace Fixture;

            public static class RealDataProtectionSetup
            {
                public static IServiceCollection AddSomething(this IServiceCollection services, string connectionString, Uri keyIdentifier)
                {
                    var builder = services.AddDataProtection()
                        .SetApplicationName("real")
                        .AddKeyManagementOptions(o => o.XmlRepository = new CoreDbXmlRepository(connectionString));
                    builder.ProtectKeysWithAzureKeyVault(keyIdentifier, new DefaultAzureCredential());
                    return services;
                }
            }
            """;

        Assert.False(ViolatesTheRule(fixtureSource));
    }

    [Fact]
    public void GreenFixture_NoAddDataProtectionCallAtAll_IsNotFlagged()
    {
        const string fixtureSource = """
            namespace Fixture;

            public static class UnrelatedSetup
            {
                public static void DoSomethingElse() { }
            }
            """;

        Assert.False(ViolatesTheRule(fixtureSource));
    }

    private static bool ViolatesTheRule(string content) =>
        content.Contains(".AddDataProtection(", StringComparison.Ordinal)
        && !content.Contains(".ProtectKeysWithAzureKeyVault(", StringComparison.Ordinal);

    private static IEnumerable<string> EnumerateRealCsFiles(string dir) =>
        Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !p.EndsWith(".g.cs", StringComparison.Ordinal)
                     && !p.EndsWith(".AssemblyInfo.cs", StringComparison.Ordinal));

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
