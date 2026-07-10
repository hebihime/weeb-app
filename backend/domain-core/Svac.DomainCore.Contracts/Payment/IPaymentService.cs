namespace Svac.DomainCore.Contracts.Payment;

/// <summary>Outcome of a payment attempt — stub shape only (SLICE_S1_CONTRACT.md §1b, B12).</summary>
public sealed record PaymentResult(bool Succeeded, string? ProviderReference, string? FailureReasonKey);

/// <summary>
/// Stub contract only (B12, SLICE_S1_CONTRACT.md §1b). DevSeams seed impl exists for local dev; NEVER
/// DI-registered in prod. Prod resolution of an unconfigured IPaymentService throws at startup (L18
/// fail-closed, two slices before money exists).
/// </summary>
public interface IPaymentService
{
    public Task<PaymentResult> Charge(string userId, long amountMinorUnits, string currency, string idempotencyKey, CancellationToken ct = default);
}
