using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;

namespace Svac.Identity.Contracts;

/// <summary>
/// The account state machine (SLICE_S3_CONTRACT.md §1b) — the verbs S12 (suspend/ban/reinstate as
/// operator surfaces) and S18 (verification-driven ban) call. CLOSED transition table, enforced by the
/// Phase-2 implementation, never by this interface's shape: active↔suspended · active|suspended→banned ·
/// banned→active (reinstate) · active|suspended→deleted ONLY via <see cref="RequestDeletion"/> — a direct
/// transition to <see cref="AccountState.Deleted"/> has no method here, deliberately; erasure has exactly
/// one door · deleted→active ONLY via <see cref="CancelDeletion"/> inside the grace window · post-purge
/// deleted is terminal. Every real transition is 4A-gated (§3) and appends <see cref="AccountStateChanged"/>
/// to the audit stream in the SAME transaction — the single publication point every later module's §1c
/// cascade projection keys on.
///
/// Phase 1 (SLICE_PLAYBOOK.md scaffold gate) ships this interface plus a DI-resolvable stub
/// implementation that throws <see cref="NotImplementedException"/> for every verb; the real state
/// machine (identity.accounts, the §1c cascade engine, the two-phase deletion pipeline) lands in the S3
/// BUILD phase.
/// </summary>
public interface IAccountLifecycle
{
    /// <summary>active|suspended → suspended. Sessions remain valid (§1b/§1c cascade table); feature surfaces deny later via the accountState policy axis.</summary>
    public Task Suspend(OpaqueId accountId, string reasonKey, RequestContext ctx, CancellationToken ct = default);

    /// <summary>suspended → active.</summary>
    public Task Reinstate(OpaqueId accountId, RequestContext ctx, CancellationToken ct = default);

    /// <summary>active|suspended → banned. All sessions/refresh families revoked in-tx, device push tokens cleared, email-code issuance silently refused (§1c cascade table).</summary>
    public Task Ban(OpaqueId accountId, string reasonKey, RequestContext ctx, CancellationToken ct = default);

    /// <summary>active|suspended → deleted (logical, Phase L of §2's two-phase, rights-preserving pipeline). The ONLY door into the deleted state.</summary>
    public Task RequestDeletion(OpaqueId accountId, RequestContext ctx, CancellationToken ct = default);

    /// <summary>deleted → active, grace-window only (§2 Phase L). Not reachable once Phase P (the physical purge) has run — post-purge deleted is terminal.</summary>
    public Task CancelDeletion(OpaqueId accountId, RequestContext ctx, CancellationToken ct = default);
}
