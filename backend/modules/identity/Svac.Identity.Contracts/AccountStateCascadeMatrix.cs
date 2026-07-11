namespace Svac.Identity.Contracts;

/// <summary>
/// One cell of the cascade matrix (SLICE_S3_CONTRACT.md §1a/§1c): what a given <see cref="AccountState"/>
/// means for one surface, and which slice owns implementing it as a projection against
/// <see cref="AccountStateChanged"/>.
/// </summary>
public sealed record AccountStateCascadeCell(AccountState State, string Surface, string Behavior, string OwningSlice);

/// <summary>
/// The §1c cascade table transcribed as checked-in code (SLICE_S3_CONTRACT.md §1a: "one cell =
/// {state, surface, behavior, owningSlice}; later modules' projections reference the cell they
/// implement"). Identity's OWN rows — the only ones whose stores exist at S3 — are enumerated here in
/// full, transcribed verbatim from §1b's cascade prose. Later modules' own cells (matches hide at S14,
/// DMs pause at S13, the conversations module's presence surface, ...) are a PHASE-2 NOTE ONLY: S3 does
/// not own their implementation, only the event (<see cref="AccountStateChanged"/>) they key off, so this
/// table intentionally does not enumerate cells identity does not own.
/// </summary>
public static class AccountStateCascadeMatrix
{
    public static readonly IReadOnlyList<AccountStateCascadeCell> Cells = new[]
    {
        new AccountStateCascadeCell(
            AccountState.Suspended,
            "identity.sessions",
            "Sessions REMAIN valid; feature surfaces deny later via the accountState policy axis; " +
            "export/deletion/logout stay reachable (GDPR rights survive suspension).",
            "S3"),
        new AccountStateCascadeCell(
            AccountState.Banned,
            "identity.sessions",
            "All sessions + refresh families revoked in-tx.",
            "S3"),
        new AccountStateCascadeCell(
            AccountState.Banned,
            "identity.devices",
            "Device push tokens cleared.",
            "S3"),
        new AccountStateCascadeCell(
            AccountState.Banned,
            "identity.auth.email_code",
            "Email-code issuance silently refused (wire-uniform 202) — a banned probe is " +
            "indistinguishable from a nonexistent email.",
            "S3"),
        new AccountStateCascadeCell(
            AccountState.Deleted,
            "identity.account",
            "The §2 two-phase, rights-preserving deletion pipeline: logical delete + restricted rights " +
            "set (me/export/deletion/sessions/logout) at request, physical purge at effective_at.",
            "S3"),

        // --- PHASE-2 NOTE (not identity's cells, listed nowhere else): every other module's own §1c row
        // (e.g. matches hide at S14, DMs pause at S13, the conversations presence surface, ...) is added
        // by THAT slice when it builds its projection against AccountStateChanged (§1b) — never here.
    };
}
