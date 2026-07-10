using Svac.AimlRouter.Contracts;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;

namespace Svac.AimlRouter.Security;

/// <summary>
/// S2's registered <see cref="IVendorEgressAuthorizer"/> (SLICE_S2_CONTRACT.md §1b/§4): "SpecialCategory
/// ⇒ Refused, always — no code path can override it before S17's consent-ledger-backed impl exists."
/// This is the WHOLE implementation, not a stub — the correct S2 behavior is exactly "always refuse
/// special-category egress," never a TODO. S17 swaps this impl for a consent-ledger-backed one; because
/// this seam is DI-resolved (never inlined at the call site), upgrading authorization never rewrites
/// recorded provenance on past invocations.
/// </summary>
internal sealed class RefuseAllSpecialCategoryAuthorizer : IVendorEgressAuthorizer
{
    public VendorEgressDecision Authorize(PayloadClass payloadClass, ActorRef? subject, RequestContext ctx) =>
        payloadClass == PayloadClass.SpecialCategory ? VendorEgressDecision.Deny : VendorEgressDecision.Allow;
}
