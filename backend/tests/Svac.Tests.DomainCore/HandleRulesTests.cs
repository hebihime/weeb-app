using System.Text.Json;
using System.Text.Json.Serialization;
using Svac.DomainCore.Deterministic;
using Xunit;

namespace Svac.Tests.DomainCore;

/// <summary>
/// Golden-vector proof for <see cref="HandleRules"/> (PHASE_2A_SUBSTRATE.md §3, SLICE_S3_CONTRACT.md §2).
/// Reads the ACTUAL committed vector file at <c>contracts/deterministic/handle-rules.v1.json</c>.
/// </summary>
public sealed class HandleRulesTests
{
    [Fact]
    public void Bounds_MatchTheCommittedVectorFile()
    {
        var vectorFile = LoadVectorFile();
        Assert.Equal(HandleRules.MinLength, vectorFile.MinLength);
        Assert.Equal(HandleRules.MaxLength, vectorFile.MaxLength);
    }

    [Fact]
    public void Canonicalizations_MatchTheCommittedVectorFile()
    {
        var vectorFile = LoadVectorFile();
        Assert.NotEmpty(vectorFile.Canonicalizations);

        foreach (var vector in vectorFile.Canonicalizations)
        {
            var canonical = HandleRules.Canonicalize(vector.Raw);
            Assert.True(vector.Canonical == canonical, $"vector \"{vector.Name}\": expected \"{vector.Canonical}\", got \"{canonical}\"");
        }
    }

    [Fact]
    public void ValidHandles_MatchTheCommittedVectorFile()
    {
        var vectorFile = LoadVectorFile();
        Assert.NotEmpty(vectorFile.Valid);

        foreach (var vector in vectorFile.Valid)
        {
            var result = HandleRules.Validate(vector.Raw);
            Assert.True(result.IsValid, $"vector \"{vector.Name}\": expected valid, got reason \"{result.ReasonKey}\"");
            Assert.Equal(vector.Canonical, result.Canonical);
        }
    }

    [Fact]
    public void InvalidHandles_MatchTheCommittedVectorFile()
    {
        var vectorFile = LoadVectorFile();
        Assert.NotEmpty(vectorFile.Invalid);

        foreach (var vector in vectorFile.Invalid)
        {
            var result = HandleRules.Validate(vector.Raw);
            Assert.False(result.IsValid, $"vector \"{vector.Name}\": expected invalid");
            Assert.Equal(vector.ReasonKey, result.ReasonKey);
        }
    }

    private static HandleRulesVectorFile LoadVectorFile()
    {
        var path = FindRepoFile("contracts/deterministic/handle-rules.v1.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<HandleRulesVectorFile>(json)
            ?? throw new InvalidOperationException($"could not deserialize {path}");
    }

    private static string FindRepoFile(string repoRelativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, repoRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"could not locate {repoRelativePath} walking up from {AppContext.BaseDirectory}");
    }

    private sealed class HandleRulesVectorFile
    {
        [JsonPropertyName("minLength")] public int MinLength { get; set; }
        [JsonPropertyName("maxLength")] public int MaxLength { get; set; }
        [JsonPropertyName("canonicalizations")] public List<CanonVector> Canonicalizations { get; set; } = new();
        [JsonPropertyName("valid")] public List<ValidVector> Valid { get; set; } = new();
        [JsonPropertyName("invalid")] public List<InvalidVector> Invalid { get; set; } = new();
    }

    private sealed class CanonVector
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("raw")] public string Raw { get; set; } = "";
        [JsonPropertyName("canonical")] public string Canonical { get; set; } = "";
    }

    private sealed class ValidVector
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("raw")] public string Raw { get; set; } = "";
        [JsonPropertyName("canonical")] public string Canonical { get; set; } = "";
    }

    private sealed class InvalidVector
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("raw")] public string Raw { get; set; } = "";
        [JsonPropertyName("reasonKey")] public string ReasonKey { get; set; } = "";
    }
}
