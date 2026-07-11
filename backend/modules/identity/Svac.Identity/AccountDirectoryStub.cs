using Svac.DomainCore.Contracts.Ids;
using Svac.Identity.Contracts;

namespace Svac.Identity;

/// <summary>
/// Phase-1 scaffold stub — see <see cref="AccountLifecycleStub"/>'s doc comment. DI-resolvable now; the
/// real reads (against <c>identity.accounts</c> via the module-owned <c>IdentityDbContext</c>) land in
/// the S3 BUILD phase.
/// </summary>
internal sealed class AccountDirectoryStub : IAccountDirectory
{
    public Task<AccountState?> GetState(OpaqueId accountId, CancellationToken ct = default) =>
        throw new NotImplementedException("S3 Phase 2");

    public Task<string?> GetHandle(OpaqueId accountId, CancellationToken ct = default) =>
        throw new NotImplementedException("S3 Phase 2");

    public Task<int?> GetAgeYears(OpaqueId accountId, CancellationToken ct = default) =>
        throw new NotImplementedException("S3 Phase 2");
}
