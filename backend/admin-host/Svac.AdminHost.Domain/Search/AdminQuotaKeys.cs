namespace Svac.AdminHost.Domain.Search;

/// <summary>
/// The admin host's 10A quota keys (SLICE_S5_CONTRACT.md §5). Mirrors
/// <c>Svac.AimlRouter.Quota.AimlRouterQuotaKeys</c>'s own shape: the runtime key is pinned HERE, in code,
/// beside its consumer — the 9A CAP key <see cref="Svac.DomainCore.Quota.QuotaService.Consume"/> actually
/// reads is the DERIVED <c>$"quota.{key}.cap"</c> (QuotaService.cs's own convention), never this literal.
/// See <c>backend/admin-host/Svac.AdminHost/config/admin-host.config.json</c>'s own comment on
/// <c>quota.admin.user_search.daily.cap</c> for why that manifest key is NOT the contract §4 table's
/// literal spelling <c>admin.user_search_daily_cap</c> — the same class of dead-tunable bug
/// <c>backend/tests/Svac.Tests.AimlRouter/BudgetCapConfigKeyWiringTests.cs</c> caught and fixed for
/// <c>aiml.call.daily</c>, caught here during THIS pass's own quota wiring rather than shipped dead.
/// </summary>
public static class AdminQuotaKeys
{
    /// <summary>SLICE_S5_CONTRACT.md §5: "admin.user_search.daily — actor = the staff stf_ ref, window daily, cap from 9A admin.user_search_daily_cap (v0 500)."</summary>
    public const string UserSearchDaily = "admin.user_search.daily";
}
