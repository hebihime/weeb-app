using System.Security.Claims;
using Svac.AdminHost.Auth;
using Xunit;

namespace Svac.Tests.AdminHost;

/// <summary>
/// SECURITY_REVIEW_S5.md S5-02 (fixNow): before this fix, <see cref="EntraClaimTypes.HasMfaClaim"/>
/// treated ANY non-empty <c>acr</c> claim as MFA-satisfied — a plain single-factor <c>pwd</c> sign-in
/// that merely carries an (unrelated-to-MFA) authentication-context claim would have passed. The fix
/// requires the claim's VALUE to match one of the tenant's own configured Conditional Access values
/// (<see cref="StaffAuthEntraConfig.AcrValues"/>, SVAC_ENTRA_MFA_ACR_VALUES) — this is the RED->GREEN
/// regression test, plus the "unconfigured acr contributes nothing" fail-closed proof.
/// </summary>
public sealed class EntraClaimTypesTests
{
    private static readonly IReadOnlySet<string> NoConfiguredAcr = new HashSet<string>();
    private static readonly IReadOnlySet<string> OneConfiguredAcr = new HashSet<string> { "c1-staff-mfa" };

    [Fact]
    public void AmrContainsMfa_SatisfiesMfa_RegardlessOfAcr()
    {
        var claims = new[] { new Claim("amr", "[\"pwd\",\"mfa\"]") };
        Assert.True(EntraClaimTypes.HasMfaClaim(claims, NoConfiguredAcr));
        Assert.True(EntraClaimTypes.HasMfaClaim(claims, OneConfiguredAcr));
    }

    [Fact]
    public void RegressionS5_02_AnyNonEmptyAcr_WithNoConfiguredValues_NoLongerSatisfiesMfa()
    {
        // Before the fix: !IsNullOrWhiteSpace(c.Value) alone was enough -- this exact shape (a plain
        // single-factor sign-in's acr, unrelated to the tenant's real MFA Conditional Access context)
        // would have incorrectly satisfied MFA. No amr claim at all in this sign-in.
        var claims = new[] { new Claim("acr", "0") }; // Entra's own "no CA context satisfied" acr value
        Assert.False(EntraClaimTypes.HasMfaClaim(claims, NoConfiguredAcr));
    }

    [Fact]
    public void AcrPresent_ButDoesNotMatchAnyConfiguredValue_DoesNotSatisfyMfa()
    {
        var claims = new[] { new Claim("acr", "some-other-context") };
        Assert.False(EntraClaimTypes.HasMfaClaim(claims, OneConfiguredAcr));
    }

    [Fact]
    public void AcrPresent_AndMatchesAConfiguredValue_SatisfiesMfa()
    {
        var claims = new[] { new Claim("acr", "c1-staff-mfa") };
        Assert.True(EntraClaimTypes.HasMfaClaim(claims, OneConfiguredAcr));
    }

    [Fact]
    public void NoAmrNoAcr_NeverSatisfiesMfa()
    {
        var claims = new[] { new Claim("sub", "someone") };
        Assert.False(EntraClaimTypes.HasMfaClaim(claims, OneConfiguredAcr));
    }

    [Fact]
    public void AmrWithoutMfa_AndUnmatchedAcr_DoesNotSatisfyMfa()
    {
        var claims = new[] { new Claim("amr", "[\"pwd\"]"), new Claim("acr", "unrelated") };
        Assert.False(EntraClaimTypes.HasMfaClaim(claims, OneConfiguredAcr));
    }
}
