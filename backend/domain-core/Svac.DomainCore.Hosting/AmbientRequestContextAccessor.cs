using Svac.DomainCore.Contracts;

namespace Svac.DomainCore.Hosting;

/// <summary>
/// Default IRequestContextAccessor: set once per request by RequestContextMiddleware, read by modules
/// (never HttpContext directly — arch-tested). AsyncLocal so it flows across await boundaries within one
/// request without needing HttpContext to be threaded through every call.
/// </summary>
public sealed class AmbientRequestContextAccessor : IRequestContextAccessor
{
    private static readonly AsyncLocal<RequestContext?> Ambient = new();

    public RequestContext Current =>
        Ambient.Value ?? throw new InvalidOperationException(
            "No RequestContext is set for the current call. RequestContextMiddleware must run before any " +
            "module code that resolves IRequestContextAccessor — check middleware ordering in Program.cs.");

    /// <summary>Set by RequestContextMiddleware exactly once per request, before the next middleware runs.</summary>
    public void Set(RequestContext context) => Ambient.Value = context;
}
