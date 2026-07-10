using System.Reflection;
using System.Text.RegularExpressions;
using Svac.DomainCore.Contracts.Api;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// L20 server-authoritative trust (SLICE_S1_CONTRACT.md §8): "an arch test scanning request-DTO type
/// graphs for verification*/reputation*/premium*/moderation_state/trust*/tier* (catches internal DTOs
/// OpenAPI lint cannot see)." tools/contract-lint/contract-lint.mjs already checks the OpenAPI document
/// wire shape; this test checks the C# request-DTO types themselves (the internal type graph a JSON
/// contract can hide fields from if a mapper drops one before serialization).
/// </summary>
public sealed class TrustDtoArchTest
{
    // MinorProt-F5 (SECURITY_REVIEW_S1.md): the original pattern was blind to the canonical
    // forgeable-18+ attest field names (age_verified, age_attested, is_adult, adult_verified,
    // birthdate_verified, minor_flag) — the exact client-forged-adulthood vector L20 and the minor stack
    // exist to kill. Each alternative's "_?" makes it match both snake_case wire-field spellings
    // ("age_verified") and PascalCase C# property spellings ("AgeVerified") under RegexOptions.IgnoreCase.
    //
    // TRUST-BREAK-4 (SECURITY_REVIEW_S2.md): extended with provider*/model*/payload_class — SLICE_S2_
    // CONTRACT.md §1b/§8/§10.2's own promised extension ("an arch scan ... proves neither the receipt
    // nor provider identity ever serializes into a user-bound DTO. A reported user can never probe
    // moderation-provider health"). Same "_?" convention: matches "Provider"/"provider_id"/"Model"/
    // "model_name"/"PayloadClass"/"payload_class" alike.
    private static readonly Regex TrustFieldPattern = new(
        "^(verification|reputation|premium|moderation_state|age_estimate|age_?verified|age_?attested|" +
        "is_?adult|adult_?verified|birthdate_?verified|minor_?flag|trust|tier|provider|model|payload_?class)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void NoContractApiRequestType_DeclaresATrustAuthoritativeProperty()
    {
        // L20 (contract-lint rule 1) is about REQUEST schemas specifically — a trust-shaped field on a
        // RESPONSE type (e.g. LimitReached.premium_extends, an explicit §1c render hint the SERVER
        // computes and sends) is not a forgeable-by-client concern and must not be flagged. S1 ships
        // zero consumer mutation endpoints (§0), so there are zero real request DTOs today; the scan
        // runs unconditionally over every "*Request"-named type in the namespace so it activates the
        // instant one appears, matching the naming convention every future request DTO here will follow.
        //
        // TRUST-BREAK-4: also scans "*Response"-named types. §1b's actual promise ("neither the
        // receipt nor provider identity ever serializes into a user-bound DTO") is a RESPONSE-shaped
        // concern (a server->client leak), not a request-forgery concern — provider*/model*/
        // payload_class fields on a would-be response DTO are exactly what this half of the scan exists
        // to catch, the moment one is added.
        var apiTypes = typeof(Problem).Assembly.GetExportedTypes()
            .Where(t => t.Namespace == typeof(Problem).Namespace
                && (t.Name.EndsWith("Request", StringComparison.Ordinal) || t.Name.EndsWith("Response", StringComparison.Ordinal)));

        var violations = new List<string>();
        foreach (var type in apiTypes)
        {
            CollectViolations(type, violations, new HashSet<Type>());
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void RedFixture_TrustAuthoritativePropertyOnARequestShapedType_IsDetected()
    {
        var violations = new List<string>();
        CollectViolations(typeof(FixtureBadRequest), violations, new HashSet<Type>());

        Assert.Single(violations);
    }

    [Fact]
    public void RedFixture_TrustAuthoritativePropertyOnANestedPayloadType_IsDetected()
    {
        // MinorProt-F5's second half: the scan was "top-level-properties-only ... a nested payload type
        // with a trust field is invisible to it" — not the "request-DTO type graphs" §8 promises. This
        // fixture's trust field lives two levels deep (FixtureRequestWithNestedPayload -> Nested ->
        // IsAdult) so CollectViolations must recurse through complex property types to catch it.
        var violations = new List<string>();
        CollectViolations(typeof(FixtureRequestWithNestedPayload), violations, new HashSet<Type>());

        Assert.Single(violations);
        Assert.Contains("IsAdult", violations[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// Walks a request-DTO's FULL type graph, not just its top-level properties (MinorProt-F5): recurses
    /// into any property whose type is a non-primitive, non-string reference/value type declared outside
    /// the BCL (a nested request payload), cycle-guarded by `visited` so a self-referencing or mutually
    /// recursive DTO graph can never loop forever.
    /// </summary>
    private static void CollectViolations(Type type, List<string> violations, HashSet<Type> visited)
    {
        if (!visited.Add(type))
        {
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (TrustFieldPattern.IsMatch(property.Name))
            {
                violations.Add($"{type.Name}.{property.Name}");
            }

            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var elementType = GetEnumerableElementType(propertyType) ?? propertyType;
            if (IsScannablePayloadType(elementType))
            {
                CollectViolations(elementType, violations, visited);
            }
        }
    }

    private static bool IsScannablePayloadType(Type type) =>
        !type.IsPrimitive && type != typeof(string) && type != typeof(decimal) && type != typeof(DateTime) &&
        type != typeof(DateTimeOffset) && type != typeof(Guid) && type != typeof(TimeOnly) && type != typeof(object) &&
        type.Namespace?.StartsWith("System", StringComparison.Ordinal) != true && (type.IsClass || (type.IsValueType && !type.IsEnum));

    private static Type? GetEnumerableElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }
        var enumerableInterface = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            ? type
            : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerableInterface?.GetGenericArguments().FirstOrDefault();
    }

    // Deliberately trust-shaped fixture type — never referenced outside this test.
    private sealed record FixtureBadRequest(string UserId, string PremiumTier);

    // Deliberately trust-shaped fixture types, the trust field two levels deep — never referenced
    // outside this test.
    private sealed record FixtureRequestWithNestedPayload(string UserId, FixtureNestedPayload Payload);
    private sealed record FixtureNestedPayload(string Note, bool IsAdult);
}
