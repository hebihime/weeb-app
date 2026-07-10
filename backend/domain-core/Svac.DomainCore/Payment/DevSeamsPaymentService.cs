using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Payment;

namespace Svac.DomainCore.Payment;

/// <summary>DevSeams seed impl (SLICE_S1_CONTRACT.md §1b, B12): always succeeds, for local dev only. NEVER DI-registered in prod.</summary>
[DevSeamsOnly]
public sealed class DevSeamsPaymentService : IPaymentService
{
    public Task<PaymentResult> Charge(string userId, long amountMinorUnits, string currency, string idempotencyKey, CancellationToken ct = default) =>
        Task.FromResult(new PaymentResult(Succeeded: true, ProviderReference: $"dev-seams:{idempotencyKey}", FailureReasonKey: null));
}
