using Svac.DomainCore.Contracts.Export;

namespace Svac.Identity.Export;

/// <summary>One (path, schema-versioned JSON) write a contributor made (SLICE_S3_CONTRACT.md §6b).</summary>
public sealed record ExportSinkEntry(string StoreKey, int SchemaVersion, string JsonPayload);

/// <summary>
/// The write side of one export run (SLICE_S3_CONTRACT.md §6b IExportSink) — collects every
/// contributor's write in memory (kilobytes of JSON, per the §12 item 5 sizing ruling) so <see
/// cref="Export.ExportWorker"/> can build the zip + manifest.json once every contributor has run.
/// </summary>
public sealed class InMemoryExportSink : IExportSink
{
    private readonly List<ExportSinkEntry> _entries = new();

    public IReadOnlyList<ExportSinkEntry> Entries => _entries;

    public Task WriteAsync(string storeKey, int schemaVersion, string jsonPayload, CancellationToken ct = default)
    {
        _entries.Add(new ExportSinkEntry(storeKey, schemaVersion, jsonPayload));
        return Task.CompletedTask;
    }
}
