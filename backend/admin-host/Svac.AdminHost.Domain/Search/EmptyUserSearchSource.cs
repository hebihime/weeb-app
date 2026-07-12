namespace Svac.AdminHost.Domain.Search;

/// <summary>
/// The day-one <see cref="IUserSearchSource"/> registration (SLICE_S5_CONTRACT.md §0/§9: "EmptyUserSearchSource
/// registered now"). ALWAYS returns zero rows with <see cref="UserSearchPage.SourceLive"/> = false — the
/// honest "identity module not yet live" state (§8 seam 16), never a fabricated row, regardless of what
/// term/queryClass is asked for. The audited-execute path around this port (quota consumption, the
/// <c>admin.user_search.executed</c> event) runs identically whether this stub or S3's real adapter is
/// behind the port — only the RESULT differs, never the wiring.
/// </summary>
public sealed class EmptyUserSearchSource : IUserSearchSource
{
    public Task<UserSearchPage> Search(UserSearchQuery query, CancellationToken ct = default) =>
        Task.FromResult(new UserSearchPage(Array.Empty<UserSearchResultRow>(), NextCursor: null, HasMore: false, SourceLive: false));
}
