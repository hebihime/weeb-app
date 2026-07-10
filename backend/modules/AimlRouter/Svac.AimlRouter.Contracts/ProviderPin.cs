namespace Svac.AimlRouter.Contracts;

/// <summary>
/// Explicit routing mode (SLICE_S2_CONTRACT.md §1b): honored VERBATIM, no policy override — but the
/// allowlist and privacy floor still bind (§1b: "the pin bypasses the routing POLICY, never the LAWS").
/// </summary>
public sealed record ProviderPin(string Provider, string Model);
