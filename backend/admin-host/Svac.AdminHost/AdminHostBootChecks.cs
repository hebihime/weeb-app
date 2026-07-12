using Svac.AdminHost.Domain.Policy;
using Svac.DomainCore.Contracts.Policy;

namespace Svac.AdminHost;

/// <summary>
/// The admin host's own boot-refusal law (SLICE_S5_CONTRACT.md §1c): "every action key registered with
/// the executor resolves to a PolicyTable row at startup or the host refuses to boot" — the S1
/// boot-refusal law (Svac.DomainCore.Hosting.StartupPolicyCoverage.RequireMutationsPolicyMapped) applied
/// at the layer the admin host actually mutates through. REAL as of Pass B:
/// <see cref="Svac.AdminHost.Domain.Execution.AdminActionExecutor"/> (staff re-read, hat computation,
/// four-eyes, reason check, the one same-tx audit event) is the real business logic this data half backs
/// — <see cref="AdminActionKeys.All"/> is checked here against the REAL, boot-time-unioned PolicyTable,
/// and the executor's own <c>Execute</c> call defensively re-asserts <c>policyTable.Find(action)</c> is
/// non-null too (belt-and-suspenders: this boot check should make an unregistered action unreachable at
/// runtime, but the executor never trusts that silently). Every future desk slice adds its own verbs to
/// this SAME list (or a superset it asserts contains it), never a second, drifting enumeration.
/// </summary>
public static class AdminHostBootChecks
{
    /// <summary>Call once, after every endpoint is mapped and before <c>app.Run()</c> — same placement discipline as <c>RequireMutationsPolicyMapped</c>.</summary>
    public static WebApplication RequireAdminActionsCovered(this WebApplication app)
    {
        var policyTable = app.Services.GetRequiredService<IPolicyTable>();

        var unmapped = AdminActionKeys.All.Where(action => policyTable.Find(action) is null).ToList();
        if (unmapped.Count > 0)
        {
            throw new InvalidOperationException(
                "4A boot refusal (SLICE_S5_CONTRACT.md §1c): the following AdminActionExecutor action " +
                "key(s) have no PolicyTable row (a registered executor action with no policy backing it " +
                $"is a contract violation, not a gap):\n  - {string.Join("\n  - ", unmapped)}");
        }

        return app;
    }
}
