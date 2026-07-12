using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// SLICE_S5_CONTRACT.md §1e: "Zero paths, zero components — asserted, drift-gated (the S2 pattern).
/// contracts/openapi.v0.json + contracts/message-keys.json byte-identical across S5; a test fails if any
/// Svac.AdminHost* type appears in the document." Structurally guaranteed by construction (Svac.
/// PublicApi's OpenApiContractEmitter/Endpoints.MapAll never references Svac.AdminHost*, proven by
/// AdminHostBoundaryTests.PublicApi_NeverReferencesTheAdminHost) — this test is the standing, permanent
/// regression proof over the COMMITTED artifacts themselves, so a future slice that somehow smuggled an
/// admin type into either file (e.g. a careless copy-paste into a shared DTO) fails loudly here, forever.
/// </summary>
public sealed class AdminHostContractDriftTests
{
    [Theory]
    [InlineData("openapi.v0.json")]
    [InlineData("message-keys.json")]
    public void ContractArtifact_NeverMentionsSvacAdminHost(string fileName)
    {
        var path = Path.Combine(FindRepoRoot(), "contracts", fileName);
        Assert.True(File.Exists(path), $"expected {path} to exist (committed since S1).");

        var content = File.ReadAllText(path);
        Assert.DoesNotContain("Svac.AdminHost", content, StringComparison.Ordinal);
        Assert.DoesNotContain("AdminHost", content, StringComparison.Ordinal);
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
