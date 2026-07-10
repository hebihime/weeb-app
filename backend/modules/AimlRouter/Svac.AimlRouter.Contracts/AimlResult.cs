namespace Svac.AimlRouter.Contracts;

/// <summary>
/// CLOSED union (SLICE_S2_CONTRACT.md §1b, S1 PolicyDecision pattern): Success | Failure. There is no
/// code path that returns neither — every <see cref="IAimlRouter.InvokeAsync"/> call resolves to exactly
/// one of these two.
/// </summary>
public abstract record AimlResult
{
    public sealed record Success(AimlPayload Output, RoutingReceipt Receipt) : AimlResult;

    public sealed record Failure(AimlFailure Cause) : AimlResult;

    public static AimlResult Ok(AimlPayload output, RoutingReceipt receipt) => new Success(output, receipt);

    public static AimlResult Failed(AimlFailure cause) => new Failure(cause);
}
