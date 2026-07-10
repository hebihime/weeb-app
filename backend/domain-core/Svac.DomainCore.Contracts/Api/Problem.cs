namespace Svac.DomainCore.Contracts.Api;

/// <summary>
/// RFC 9457 problem shape with a message_key + correlation id, never localized prose (token law 2,
/// SLICE_S1_CONTRACT.md §1c). The one generic error shape.
/// </summary>
public sealed record Problem(
    string Type,
    string Title,
    int Status,
    string MessageKey,
    string CorrelationId,
    string? Detail = null,
    string? Instance = null);

/// <summary>Canonical message keys S1 seeds into contracts/message-keys.json (SLICE_S1_CONTRACT.md §1c).</summary>
public static class MessageKeys
{
    public const string LimitReachedGeneric = "limit_reached.generic";
    public const string ErrorGeneric = "error.generic";
    public const string ErrorCouldNotSend = "error.could_not_send";
}
