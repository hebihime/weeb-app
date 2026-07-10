namespace Svac.AimlRouter.Contracts;

/// <summary>Closed set of backend modules permitted to call the router (SLICE_S2_CONTRACT.md §1b). Extending this is a versioned contract change.</summary>
public enum CallerModule
{
    Integrity,
    Conversations,
    Characters,
    PartnerIntel,
    Media,
    System,
}
