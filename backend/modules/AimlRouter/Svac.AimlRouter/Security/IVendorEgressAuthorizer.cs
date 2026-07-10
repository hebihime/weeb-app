using Svac.AimlRouter.Contracts;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;

namespace Svac.AimlRouter.Security;

/// <summary>Decision shape for a vendor-egress check (SLICE_S2_CONTRACT.md §1b).</summary>
public abstract record VendorEgressDecision
{
    public sealed record Authorized : VendorEgressDecision;
    public sealed record Refused : VendorEgressDecision;

    public static readonly VendorEgressDecision Allow = new Authorized();
    public static readonly VendorEgressDecision Deny = new Refused();

    public bool IsAuthorized => this is Authorized;
}

/// <summary>
/// Vendor-egress authorization seam (SLICE_S2_CONTRACT.md §1b; P3; S17 arms it): the router is the only
/// place user data leaves our trust boundary toward a model vendor, and this seam is the second,
/// independent lock (alongside the allowlist bounds rule, §4) on that egress.
/// </summary>
internal interface IVendorEgressAuthorizer
{
    public VendorEgressDecision Authorize(PayloadClass payloadClass, ActorRef? subject, RequestContext ctx);
}
