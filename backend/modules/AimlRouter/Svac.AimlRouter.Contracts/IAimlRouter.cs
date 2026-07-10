using Svac.DomainCore.Contracts;

namespace Svac.AimlRouter.Contracts;

/// <summary>
/// The ONE egress every module reaches a model through (SLICE_S2_CONTRACT.md, 15A ruling; module
/// <c>Svac.AimlRouter</c>, contract <c>IAimlRouter</c>, impl <c>AimlRouterService</c> — verbatim names).
/// Zero HTTP endpoint maps to this: consumers are backend modules calling in-process through
/// <c>Svac.AimlRouter.Contracts</c>, never a client (§0).
/// </summary>
public interface IAimlRouter
{
    public Task<AimlResult> InvokeAsync(AimlRequest req, RequestContext ctx, CancellationToken ct = default);
}
