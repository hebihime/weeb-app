using Svac.DomainCore.Contracts.Ids;

namespace Svac.Identity.Contracts;

/// <summary>
/// The read-composition seam (SLICE_S3_CONTRACT.md §1a): "S10's privacy matrix consumes this, never
/// identity's tables." Every later module reads identity state through THIS interface — zero cross-module
/// joins exist (§2's enumeration). A null result means the account id is unknown, tombstoned, or otherwise
/// unresolvable — callers treat null as absence, the same posture <c>IResourceOwnershipResolver</c>'s null
/// owner takes, never as an error.
///
/// Phase 1 (SLICE_PLAYBOOK.md scaffold gate) ships this interface plus a DI-resolvable stub
/// implementation that throws <see cref="NotImplementedException"/> for every read; the real reads
/// (against <c>identity.accounts</c>, via the module-owned <c>IdentityDbContext</c>) land in the S3 BUILD
/// phase.
/// </summary>
public interface IAccountDirectory
{
    /// <summary>The account's current lifecycle state, or null if the id is unknown.</summary>
    public Task<AccountState?> GetState(OpaqueId accountId, CancellationToken ct = default);

    /// <summary>The account's canonical (NFKC-folded lowercase, HandleRules) handle, or null if the id is unknown/tombstoned.</summary>
    public Task<string?> GetHandle(OpaqueId accountId, CancellationToken ct = default);

    /// <summary>The account's current age in whole years, DERIVED from the encrypted birthdate via AgeMath on every call — never the raw birthdate (§1c: "birthdate NEVER in any response, arch-tested"). Null if the id is unknown.</summary>
    public Task<int?> GetAgeYears(OpaqueId accountId, CancellationToken ct = default);
}
