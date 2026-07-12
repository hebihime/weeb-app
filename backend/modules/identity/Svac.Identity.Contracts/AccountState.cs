namespace Svac.Identity.Contracts;

/// <summary>
/// The account_state axis (SLICE_S3_CONTRACT.md §2 DDL: <c>CHECK (account_state IN ('active','suspended',
/// 'banned','deleted'))</c>). Closed set, matches the DB CHECK verbatim — extend only via a versioned
/// contract change, never silently.
/// </summary>
public enum AccountState
{
    Active,
    Suspended,
    Banned,
    Deleted,
}
