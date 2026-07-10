namespace Svac.AimlRouter.Providers;

/// <summary>
/// Internal SPI (buy-vs-build seam; SLICE_S2_CONTRACT.md §1b) — NEVER crosses the module boundary.
/// Adding a future vendor (PhotoDNA/Content Safety at S11, an MT vendor at S13, ...) is one adapter +
/// one founder allowlist row + one DI line; S2 pre-builds zero vendor code beyond this seam + the
/// `anthropic` provider's two transports + the test-only SeedProvider.
/// </summary>
internal interface IModelProvider
{
    public ProviderDescriptor Descriptor { get; }

    public Task<ProviderExecutionResult> ExecuteAsync(ProviderInvocation invocation, CancellationToken ct);
}
