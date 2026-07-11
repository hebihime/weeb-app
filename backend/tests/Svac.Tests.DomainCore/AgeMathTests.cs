using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Svac.DomainCore.Deterministic;
using Xunit;

namespace Svac.Tests.DomainCore;

/// <summary>
/// Golden-vector proof for <see cref="AgeMath"/> (PHASE_2A_SUBSTRATE.md §3, SLICE_S3_CONTRACT.md §1g).
/// Reads the ACTUAL committed vector file at <c>contracts/deterministic/age-math.v1.json</c> — the file
/// meant to be shared verbatim with the client test suites once one exists (the cross-file lint is
/// DEFERRED, per PHASE_2A_SUBSTRATE.md §3, not built here) — never a hand-typed copy that could drift.
/// </summary>
public sealed class AgeMathTests
{
    [Fact]
    public void AdultFloorYears_IsEighteen_ACodeConstant()
    {
        Assert.Equal(18, AgeMath.AdultFloorYears);
    }

    [Fact]
    public void CoppaFloorYears_IsThirteen_ACodeConstant()
    {
        Assert.Equal(13, AgeMath.CoppaFloorYears);
    }

    [Fact]
    public void GoldenVectors_MatchTheCommittedVectorFile()
    {
        var vectorFile = LoadVectorFile();
        Assert.Equal(AgeMath.AdultFloorYears, vectorFile.AdultFloorYears);
        Assert.Equal(AgeMath.CoppaFloorYears, vectorFile.CoppaFloorYears);
        Assert.NotEmpty(vectorFile.Vectors);

        foreach (var vector in vectorFile.Vectors)
        {
            var birthdate = DateOnly.ParseExact(vector.Birthdate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var asOf = DateOnly.ParseExact(vector.AsOf, "yyyy-MM-dd", CultureInfo.InvariantCulture);

            var age = AgeMath.AgeYears(birthdate, asOf);
            Assert.True(vector.ExpectedAgeYears == age, $"vector \"{vector.Name}\": expected age {vector.ExpectedAgeYears}, got {age}");

            if (vector.IsAdult is { } expectedIsAdult)
            {
                Assert.True(expectedIsAdult == AgeMath.IsAtLeast(birthdate, AgeMath.AdultFloorYears, asOf), $"vector \"{vector.Name}\": IsAtLeast(18) mismatch");
            }

            if (vector.IsCoppa is { } expectedIsCoppa)
            {
                Assert.True(expectedIsCoppa == AgeMath.IsAtLeast(birthdate, AgeMath.CoppaFloorYears, asOf), $"vector \"{vector.Name}\": IsAtLeast(13) mismatch");
            }
        }
    }

    [Fact]
    public void GoldenVectors_InvalidInputs_ThrowRatherThanProduceAVerdict()
    {
        var vectorFile = LoadVectorFile();
        Assert.NotEmpty(vectorFile.InvalidInputs);

        foreach (var vector in vectorFile.InvalidInputs)
        {
            var birthdate = DateOnly.ParseExact(vector.Birthdate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var asOf = DateOnly.ParseExact(vector.AsOf, "yyyy-MM-dd", CultureInfo.InvariantCulture);

            Assert.Throws<ArgumentException>(() => AgeMath.AgeYears(birthdate, asOf));
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static AgeMathVectorFile LoadVectorFile()
    {
        var path = FindRepoFile("contracts/deterministic/age-math.v1.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AgeMathVectorFile>(json, SerializerOptions)
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

    private sealed class AgeMathVectorFile
    {
        [JsonPropertyName("adultFloorYears")] public int AdultFloorYears { get; set; }
        [JsonPropertyName("coppaFloorYears")] public int CoppaFloorYears { get; set; }
        [JsonPropertyName("vectors")] public List<AgeVector> Vectors { get; set; } = new();
        [JsonPropertyName("invalidInputs")] public List<InvalidVector> InvalidInputs { get; set; } = new();
    }

    private sealed class AgeVector
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("birthdate")] public string Birthdate { get; set; } = "";
        [JsonPropertyName("asOf")] public string AsOf { get; set; } = "";
        [JsonPropertyName("expectedAgeYears")] public int ExpectedAgeYears { get; set; }
        [JsonPropertyName("isAdult")] public bool? IsAdult { get; set; }
        [JsonPropertyName("isCoppa")] public bool? IsCoppa { get; set; }
    }

    private sealed class InvalidVector
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("birthdate")] public string Birthdate { get; set; } = "";
        [JsonPropertyName("asOf")] public string AsOf { get; set; } = "";
    }
}
