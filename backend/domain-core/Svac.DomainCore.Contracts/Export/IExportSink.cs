namespace Svac.DomainCore.Contracts.Export;

/// <summary>
/// The write side of the statutory data-export registry (PHASE_2A_SUBSTRATE.md §2, SLICE_S3_CONTRACT.md
/// §1b). A contributor writes its own store's rows as schema-versioned JSON under a store key — never a
/// raw DB dump, never a cross-module join. The concrete artifact target (Postgres bytea today, blob
/// storage later) is a module-internal implementation detail of whoever implements this interface; this
/// surgery ships the seam only, no implementation (no consumer exists at S1/S2).
/// </summary>
public interface IExportSink
{
    public Task WriteAsync(string storeKey, int schemaVersion, string jsonPayload, CancellationToken ct = default);
}
