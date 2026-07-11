using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.Identity.Contracts;

namespace Svac.Identity;

/// <summary>
/// Phase-1 scaffold stub (SLICE_PLAYBOOK.md Phase 1: "empty-but-running skeleton — compiling, wired,
/// booting — NOT the real feature"). DI-resolvable so <see cref="DependencyInjection.IdentityServiceCollectionExtensions.AddIdentityModule"/>'s
/// registration is provable now; every verb throws until the S3 BUILD phase implements the real state
/// machine (SLICE_S3_CONTRACT.md §1b/§2/§3).
/// </summary>
internal sealed class AccountLifecycleStub : IAccountLifecycle
{
    public Task Suspend(OpaqueId accountId, string reasonKey, RequestContext ctx, CancellationToken ct = default) =>
        throw new NotImplementedException("S3 Phase 2");

    public Task Reinstate(OpaqueId accountId, RequestContext ctx, CancellationToken ct = default) =>
        throw new NotImplementedException("S3 Phase 2");

    public Task Ban(OpaqueId accountId, string reasonKey, RequestContext ctx, CancellationToken ct = default) =>
        throw new NotImplementedException("S3 Phase 2");

    public Task RequestDeletion(OpaqueId accountId, RequestContext ctx, CancellationToken ct = default) =>
        throw new NotImplementedException("S3 Phase 2");

    public Task CancelDeletion(OpaqueId accountId, RequestContext ctx, CancellationToken ct = default) =>
        throw new NotImplementedException("S3 Phase 2");
}
