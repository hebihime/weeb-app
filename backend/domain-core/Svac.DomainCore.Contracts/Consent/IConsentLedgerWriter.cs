using Svac.DomainCore.Contracts.Purge;

namespace Svac.DomainCore.Contracts.Consent;

/// <summary>A consent grant or revocation (PHASE_2A_SUBSTRATE.md §2).</summary>
public enum ConsentDecision
{
    Granted,
    Revoked,
}

/// <summary>
/// The typed write door onto the consent event stream (PHASE_2A_SUBSTRATE.md §2, SLICE_S3_CONTRACT.md
/// §1b). Appends ONE <c>consent.recorded</c> event to <c>events_consent</c> in the caller's own
/// transaction; region + lawful-basis stamping happens at the substrate, never the caller. Zero real
/// writers exist at S1/S2 — this ships the seam only.
/// </summary>
public interface IConsentLedgerWriter
{
    public Task Record(SubjectRef subject, ConsentKind kind, string version, string surface, ConsentDecision decision, RequestContext ctx, CancellationToken ct = default);
}
