namespace Svac.AdminHost.Domain.Search;

/// <summary>The three query classes the User Search desk supports (SLICE_S5_CONTRACT.md §9: "Search(query: HandlePrefix|EmailExact|DeviceExact, cursor)").</summary>
public enum UserSearchQueryClass
{
    HandlePrefix,
    EmailExact,
    DeviceExact,
}

/// <summary>The search request (SLICE_S5_CONTRACT.md §9's host-owned port).</summary>
public sealed record UserSearchQuery(UserSearchQueryClass QueryClass, string Term, string? Cursor);

/// <summary>
/// One rendered result row — opaque ids, no trust fields (SLICE_S5_CONTRACT.md §9's recorded obligation
/// for S3's future adapter: "no trust fields in the result DTO, opaque ids"). <see cref="AccountState"/>
/// is the one non-PII, operationally-necessary field a search result must carry (a triage operator needs
/// to see active/suspended/banned/deleted at a glance) — never a reputation score, verification tier, or
/// any other trust signal.
/// </summary>
public sealed record UserSearchResultRow(string UserRef, string? Handle, string? EmailMasked, string AccountState);

/// <summary>
/// One page of search results (SLICE_S5_CONTRACT.md §9). <see cref="SourceLive"/> is false only for
/// <see cref="EmptyUserSearchSource"/> — it distinguishes "the identity module is not live yet" (§8 seam
/// 16: "the honest 'identity module not yet live' state") from a REAL zero-result search once S3 lands a
/// live adapter, so the UI never conflates the two.
/// </summary>
public sealed record UserSearchPage(IReadOnlyList<UserSearchResultRow> Items, string? NextCursor, bool HasMore, bool SourceLive);

/// <summary>
/// The HOST-OWNED search port (SLICE_S5_CONTRACT.md §0/§8 seam 6/§9 — judge synthesis §12.4: "the
/// substrate has no business knowing about user search... the correct dependency direction is admin
/// host → identity module contract via one adapter when S3 lands"). <see cref="EmptyUserSearchSource"/>
/// is registered NOW (§9); S3's real adapter is a later ONE-DI-LINE swap
/// (<c>services.AddScoped&lt;IUserSearchSource, IdentityUserSearchSource&gt;()</c>), never a signature
/// change to this interface or to the audited execute path that calls it.
/// </summary>
public interface IUserSearchSource
{
    public Task<UserSearchPage> Search(UserSearchQuery query, CancellationToken ct = default);
}
