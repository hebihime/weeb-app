using Svac.DomainCore.Contracts.Purge;

namespace Svac.DomainCore.Contracts.Export;

/// <summary>Closed union: what a contributor did for one subject's export (PHASE_2A_SUBSTRATE.md §2).</summary>
public abstract record ExportDisposition
{
    public sealed record ContributesDisposition : ExportDisposition;

    public sealed record NotExportableDisposition(string Reason) : ExportDisposition;

    public sealed record WithheldDisposition(string BasisRef) : ExportDisposition;

    public static readonly ExportDisposition Contributes = new ContributesDisposition();
    public static ExportDisposition NotExportable(string reason) => new NotExportableDisposition(reason);
    public static ExportDisposition Withheld(string basisRef) => new WithheldDisposition(basisRef);
}

/// <summary>
/// Registry machinery every PII-holding module registers into (PHASE_2A_SUBSTRATE.md §2, SLICE_S3_
/// CONTRACT.md §1b) — the export-side mirror of 13A's purge registry, deliberately living in domain-core
/// so a registrant module never references identity (or any other module) to participate. The
/// <c>export-registry.json</c> file + the export⋈purge CI cross-gate are S3-build artifacts; this surgery
/// ships the interface + disposition union only — zero registrants exist at S1/S2.
/// </summary>
public interface IExportContributor
{
    /// <summary>The store key this contributor answers for — matches the same key space as 13A's purge registry.</summary>
    public string StoreKey { get; }

    public Task<ExportDisposition> ContributeAsync(SubjectRef subject, IExportSink sink, CancellationToken ct = default);
}
