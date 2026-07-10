using Svac.DomainCore.Deterministic;
using Xunit;

namespace Svac.Tests.DomainCore;

public sealed class UlidTests
{
    [Fact]
    public void Encode_ProducesA26CharacterCrockfordBase32String()
    {
        var body = Ulid.Encode(1_700_000_000_000L, new byte[10]);
        Assert.Equal(26, body.Length);
    }

    [Fact]
    public void Encode_SameInputs_AlwaysProducesTheSameOutput()
    {
        var randomness = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var a = Ulid.Encode(1_700_000_000_000L, randomness);
        var b = Ulid.Encode(1_700_000_000_000L, randomness);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Encode_LaterTimestamp_SortsLexicographicallyAfterEarlier()
    {
        var earlier = Ulid.Encode(1_700_000_000_000L, new byte[10]);
        var later = Ulid.Encode(1_700_000_000_001L, new byte[10]);
        Assert.True(string.CompareOrdinal(earlier, later) < 0);
    }

    [Fact]
    public void Encode_RejectsNegativeTimestamp()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Ulid.Encode(-1, new byte[10]));
    }

    [Fact]
    public void Encode_RejectsWrongRandomnessLength()
    {
        Assert.Throws<ArgumentException>(() => Ulid.Encode(0, new byte[5]));
    }

    [Fact]
    public void WithPrefix_And_SplitPrefixed_RoundTrip()
    {
        var body = Ulid.Encode(1_700_000_000_000L, new byte[10]);
        var prefixed = Ulid.WithPrefix("usr", body);

        var (prefix, roundTrippedBody) = Ulid.SplitPrefixed(prefixed);

        Assert.Equal("usr", prefix);
        Assert.Equal(body, roundTrippedBody);
    }

    [Theory]
    [InlineData("no_underscore_but_too_short")]
    [InlineData("usr_tooshort")]
    [InlineData("nounderscoreatall00000000000000000000")]
    public void SplitPrefixed_RejectsMalformedIds(string malformed)
    {
        Assert.ThrowsAny<FormatException>(() => Ulid.SplitPrefixed(malformed));
    }
}
