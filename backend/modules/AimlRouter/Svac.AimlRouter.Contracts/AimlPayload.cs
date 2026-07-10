namespace Svac.AimlRouter.Contracts;

/// <summary>One turn of the neutral text envelope (SLICE_S2_CONTRACT.md §1b: "Messages[]").</summary>
public sealed record AimlMessage(AimlMessageRole Role, string Content);

/// <summary>Closed role set for <see cref="AimlMessage"/> — a neutral envelope, no vendor dialect.</summary>
public enum AimlMessageRole
{
    User,
    Assistant,
}

/// <summary>
/// The neutral text envelope (SLICE_S2_CONTRACT.md §1b): "System, Messages[], MaxTokens, Temperature,
/// StructuredOutputSchema? — no vendor dialect; in-memory only, NEVER persisted by the router." The same
/// shape doubles as the SUCCESS output payload (<see cref="AimlResult.Success"/>): <see cref="OutputText"/>
/// carries the completion text and is null on every REQUEST-direction payload; <see cref="Messages"/> and
/// the rest are null/empty on every OUTPUT-direction payload. Two directions, one type, so there is no
/// second envelope shape to design or persist.
/// </summary>
public sealed record AimlPayload(
    string? System = null,
    IReadOnlyList<AimlMessage>? Messages = null,
    int? MaxTokens = null,
    double? Temperature = null,
    string? StructuredOutputSchema = null,
    string? OutputText = null)
{
    /// <summary>Convenience factory for the common single-user-turn request shape.</summary>
    public static AimlPayload ForUserTurn(string userText, string? system = null, int? maxTokens = null, double? temperature = null, string? structuredOutputSchema = null) =>
        new(System: system, Messages: new[] { new AimlMessage(AimlMessageRole.User, userText) }, MaxTokens: maxTokens, Temperature: temperature, StructuredOutputSchema: structuredOutputSchema);

    /// <summary>Convenience factory for a SUCCESS output payload — the only place <see cref="OutputText"/> is populated.</summary>
    public static AimlPayload ForOutput(string outputText) => new(OutputText: outputText);
}
