using Svac.DomainCore.Contracts.Payment;

namespace Svac.DomainCore.Payment;

/// <summary>
/// The PROD default (SLICE_S1_CONTRACT.md §1b, B12): prod resolution of an unconfigured IPaymentService
/// throws (L18 fail-closed), two slices before money exists. Never DI-registered behind a DevSeams
/// branch — this IS the branch that runs when DevSeams is off, by construction.
/// </summary>
public sealed class ThrowingPaymentService : IPaymentService
{
    public Task<PaymentResult> Charge(string userId, long amountMinorUnits, string currency, string idempotencyKey, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "IPaymentService has no real backend configured (B12, SLICE_S1_CONTRACT.md §1b) — payment " +
            "vendors (S23 IAP, S30 Stripe) are swap-safe seams not yet built. This throw is deliberate " +
            "fail-closed behavior, not a bug.");
}
