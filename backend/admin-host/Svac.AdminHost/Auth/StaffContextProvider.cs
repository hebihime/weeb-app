using Svac.DomainCore.Contracts;

namespace Svac.AdminHost.Auth;

/// <summary>
/// Solves the "Blazor lifetime trap" once (SLICE_S5_CONTRACT.md §1b): S1's <c>IRequestContextAccessor</c>
/// is request-scoped (an <c>AsyncLocal</c> set once by <c>RequestContextMiddleware</c>). Under this
/// slice's static-SSR render mode that IS one staff operation, so the trap does not bite today — but the
/// moment a future desk turns on per-component interactivity (§1a: "additive, never a rewrite, the moment
/// one does"), a single circuit's DI scope can back MANY staff operations over its lifetime, and reusing
/// the ambient accessor's ORIGINAL CorrelationId for every one of them would wrongly correlate unrelated
/// audit events. <see cref="StaffContextProvider"/> mints a FRESH <see cref="RequestContext"/> per staff
/// operation — same staff ActorRef + region (both already resolved onto the ambient context by <see
/// cref="StaffCookieBearerAuthenticator"/>), fresh CorrelationId — so every future desk inherits the fix
/// by calling this instead of reading <see cref="IRequestContextAccessor"/> directly. <c>Staff</c> stays
/// null here — the hat is per-ACTION (it depends on which policy row is being authorized), computed and
/// stamped on by <c>AdminActionExecutor</c> (§1c step 2), never by this generic seam.
/// </summary>
public interface IStaffContextProvider
{
    public RequestContext ForStaffOperation();
}

public sealed class StaffContextProvider(IRequestContextAccessor accessor) : IStaffContextProvider
{
    public RequestContext ForStaffOperation() =>
        accessor.Current with { CorrelationId = Guid.NewGuid().ToString("N") };
}
