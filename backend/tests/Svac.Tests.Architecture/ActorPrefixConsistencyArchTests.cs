using Microsoft.AspNetCore.Http;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Region;
using Svac.DomainCore.Hosting;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// The Kind-vs-prefix cross-check promised by SECURITY_REVIEW_S1.md Auth-F1: an ActorRef's Id.Prefix is
/// load-bearing (OpaqueId.cs, ActorRef.cs — "4A axis checks and ER-6 absence rules key off it"), so this
/// suite proves TWO things non-vacuously, against real production code, not a hand-typed fixture double:
///
///   1. IdPrefixes.ActorKindForPrefix is a complete, bijective map — every ActorKind that can be minted
///      from an OpaqueId has exactly one canonical prefix, and no prefix maps to two kinds.
///   2. Every REAL production actor-minting call site (RequestContextMiddleware's anonymous actor today;
///      extend this list as new sites land) produces an ActorRef whose Kind matches its Id.Prefix under
///      that same map — a red fixture (BadProducer) proves the check actually fires on a mismatch.
/// </summary>
public sealed class ActorPrefixConsistencyArchTests
{
    [Fact]
    public void ActorKindForPrefix_IsBijective_EveryMintableActorKindHasExactlyOnePrefix()
    {
        var mintableKinds = new[] { ActorKind.User, ActorKind.Staff, ActorKind.Partner, ActorKind.System, ActorKind.Anonymous };

        foreach (var kind in mintableKinds)
        {
            var prefixesForKind = IdPrefixes.ActorKindForPrefix.Where(kv => kv.Value == kind).Select(kv => kv.Key).ToList();
            Assert.True(prefixesForKind.Count == 1, $"ActorKind.{kind} must map to exactly one prefix; found [{string.Join(", ", prefixesForKind)}]");
        }

        // No prefix maps to two different kinds (the dictionary's key uniqueness already guarantees this
        // structurally, but assert it explicitly so the invariant is visible as a proof, not an accident
        // of the CLR's Dictionary<> implementation).
        Assert.Equal(IdPrefixes.ActorKindForPrefix.Keys.Distinct().Count(), IdPrefixes.ActorKindForPrefix.Count);
    }

    [Fact]
    public async Task RequestContextMiddleware_MintsAnonymousActor_WhosePrefixMatchesItsKind()
    {
        var accessor = new AmbientRequestContextAccessor();
        ActorRef actor = default;
        var middleware = new RequestContextMiddleware(
            next: _ => { actor = accessor.Current.Actor; return Task.CompletedTask; },
            accessor: accessor,
            regionResolver: new FixedUnknownRegionResolver());

        var httpContext = new DefaultHttpContext { TraceIdentifier = "trace-cross-check" };
        await middleware.InvokeAsync(httpContext);

        AssertConsistent(actor);
    }

    [Fact]
    public void RedFixture_KindPrefixMismatch_IsDetected()
    {
        // The exact shape of the shipped bug before the Auth-F1 fix: an Anonymous-kind actor minted under
        // the System prefix. Proves AssertConsistent actually fires rather than passing vacuously.
        var badActor = new ActorRef(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.Anonymous);

        var ex = Record.Exception(() => AssertConsistent(badActor));
        Assert.NotNull(ex);
    }

    /// <summary>Shared cross-check: throws (via Assert) unless actor.Id.Prefix is the canonical prefix for actor.Kind.</summary>
    private static void AssertConsistent(ActorRef actor)
    {
        Assert.True(
            IdPrefixes.ActorKindForPrefix.TryGetValue(actor.Id.Prefix, out var expectedKind),
            $"actor id \"{actor.Id}\" carries prefix \"{actor.Id.Prefix}\", which is not a registered actor prefix.");
        Assert.Equal(expectedKind, actor.Kind);
    }

    private sealed class FixedUnknownRegionResolver : IRegionResolver
    {
        public Task<(RegionCode Region, RegionSource Source)> Resolve(ActorRef actor, CancellationToken ct = default) =>
            Task.FromResult((RegionCode.Unknown, RegionSource.EdgeInferred));
    }
}
