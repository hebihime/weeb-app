using Svac.DomainCore.Deterministic;

namespace Svac.AimlRouter.Contracts;

/// <summary>
/// The router's own opaque invocation id — "aiv_ ULID" (SLICE_S2_CONTRACT.md §1b). Deliberately NOT a
/// member of domain-core's closed <c>IdPrefixes</c> set: this id is internal telemetry that "never
/// serializes into a user-bound DTO" (§1b) and never crosses the module boundary as a resource
/// reference, so it does not need to round-trip through <c>OpaqueId.Parse</c>'s cross-module validation
/// — minting it locally, over the same pure Ulid codec every other typed id uses, keeps S2 entirely
/// inside <c>backend/modules/AimlRouter/**</c> (§0 scope ruling) with zero edits to domain-core's closed
/// prefix registry.
/// </summary>
public readonly record struct AimlInvocationId(string Value)
{
    public const string Prefix = "aiv";

    /// <summary>Mints a fresh invocation id. Caller supplies the clock and the randomness source (Deterministic discipline: no wall-clock/ambient-random read inside this type).</summary>
    public static AimlInvocationId New(DateTimeOffset now, Random random)
    {
        var bytes = new byte[10];
        random.NextBytes(bytes);
        var body = Ulid.Encode(now.ToUnixTimeMilliseconds(), bytes);
        return new AimlInvocationId(Ulid.WithPrefix(Prefix, body));
    }

    public override string ToString() => Value;
}
