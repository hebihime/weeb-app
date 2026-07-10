namespace Svac.AimlRouter.Providers;

/// <summary>
/// A TRANSPORT selected by environment, never a provider selectable by policy (SLICE_S2_CONTRACT.md
/// §1b/§12.2 — the ruling-contradiction fix): local-Claude-Code vs Claude-API is a transport, not a
/// separate 9A allowlist entry. A desk edit can never route production traffic to a keyless local
/// process because <see cref="Api"/> and <see cref="LocalProcess"/> are never themselves 9A values.
/// </summary>
public enum ProviderTransport
{
    Api,
    LocalProcess,
}
