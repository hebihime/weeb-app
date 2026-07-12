using Svac.DomainCore.Deterministic;
using Xunit;

namespace Svac.Tests.DomainCore;

/// <summary>Golden-vector proof for <see cref="CursorMath"/> (PHASE_2A_SUBSTRATE.md §3).</summary>
public sealed class CursorMathTests
{
    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(50L)]
    [InlineData(12345L)]
    [InlineData(long.MaxValue)]
    public void EncodeThenDecode_RoundTrips(long offset)
    {
        var cursor = CursorMath.Encode(offset);
        var decoded = CursorMath.Decode(cursor);
        Assert.Equal(offset, decoded);
    }

    [Fact]
    public void Encode_IsDeterministic_SameInputSameCursor()
    {
        Assert.Equal(CursorMath.Encode(42), CursorMath.Encode(42));
    }

    [Fact]
    public void Encode_IsUrlSafe_NoPlusSlashOrPadding()
    {
        var cursor = CursorMath.Encode(999999999999L);
        Assert.DoesNotContain('+', cursor);
        Assert.DoesNotContain('/', cursor);
        Assert.DoesNotContain('=', cursor);
    }

    [Fact]
    public void Encode_NegativeOffset_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CursorMath.Encode(-1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-valid-base64url!!!")]
    [InlineData("!!!")]
    public void Decode_MalformedCursor_ThrowsFormatException(string malformed)
    {
        Assert.Throws<FormatException>(() => CursorMath.Decode(malformed));
    }

    [Fact]
    public void Decode_WellFormedButNonNumericPayload_ThrowsFormatException()
    {
        // Base64url-valid but decodes to "hello", not an integer.
        var cursor = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("hello")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Throws<FormatException>(() => CursorMath.Decode(cursor));
    }
}
