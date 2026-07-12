using Svac.DomainCore.Contracts.Policy;

namespace Svac.AdminHost.Domain.Auth;

/// <summary>
/// The deterministic fixture set <c>DevSeamsStaffTransport</c> issues (SLICE_S5_CONTRACT.md §1b/§8 seam
/// 4: "founder-all-roles, one-per-role, one WITHOUT the MFA claim, one with NO staff row — so every
/// refusal path is E2E-testable forever"). Pure data: every claim shape here is IDENTICAL in kind to what
/// a real Entra sign-in would hand <see cref="StaffSignInPipeline"/> (an <see cref="ExternalSubject"/> +
/// an MFA boolean) — the dev transport differs from prod ONLY in where that shape comes from, never in
/// what the pipeline does with it (§1b: "the pipeline after [the transport] is IDENTICAL in dev and prod").
/// </summary>
public sealed record DevSeamsStaffFixture(
    string Key,
    string ExternalSubject,
    string Email,
    string DisplayName,
    bool HasMfaClaim,
    bool ProvisionRow,
    IReadOnlyList<StaffRole> Roles);

public static class DevSeamsStaffFixtures
{
    private const string SubjectPrefix = "devseams:";

    public static readonly IReadOnlyList<DevSeamsStaffFixture> All = BuildAll();

    public static DevSeamsStaffFixture? Find(string key) => All.FirstOrDefault(f => f.Key == key);

    private static DevSeamsStaffFixture[] BuildAll()
    {
        var allRoles = Enum.GetValues<StaffRole>();
        var founder = new DevSeamsStaffFixture(
            "founder", SubjectPrefix + "founder", "founder@devseams.svac.internal", "Founder (all 6 roles)",
            HasMfaClaim: true, ProvisionRow: true, Roles: allRoles);

        // Key = role.ToString().ToLowerInvariant() (e.g. "superadmin") -- the exact fixture key
        // backend/e2e/admin-host.e2e.mjs's wire contract drives (its "superadmin" fixture IS the
        // SuperAdmin per-role fixture, and its ExternalSubject "devseams:superadmin" is the ONE literal
        // string the e2e's SVAC_ADMIN_BOOTSTRAP_SUBJECT precondition also names, SLICE_S5_CONTRACT.md §1b).
        var perRole = allRoles.Select(role => new DevSeamsStaffFixture(
            Key: role.ToString().ToLowerInvariant(),
            ExternalSubject: SubjectPrefix + role.ToString().ToLowerInvariant(),
            Email: $"{role.ToString().ToLowerInvariant()}@devseams.svac.internal",
            DisplayName: $"{role} (one role)",
            HasMfaClaim: true,
            ProvisionRow: true,
            Roles: new[] { role }));

        // Key "no-mfa" (hyphen, not underscore) -- backend/e2e/admin-host.e2e.mjs's literal fixture key.
        var noMfa = new DevSeamsStaffFixture(
            "no-mfa", SubjectPrefix + "no-mfa", "no-mfa@devseams.svac.internal", "No MFA claim (provisioned, SafetyAgent)",
            HasMfaClaim: false, ProvisionRow: true, Roles: new[] { StaffRole.SafetyAgent });

        // Key "not-provisioned" (backend/e2e/admin-host.e2e.mjs's literal fixture key): never auto-
        // provisioned by DevSeamsStaffTransport (ProvisionRow: false) so the FIRST sign-in attempt
        // genuinely hits RefusedUnknownSubject; the e2e itself provisions "devseams:not-provisioned" mid-
        // run via the Staff & Roles desk, then signs in as this SAME fixture again to prove the new row
        // (and later, the granted hat) is live without a redeploy.
        var noStaffRow = new DevSeamsStaffFixture(
            "not-provisioned", SubjectPrefix + "not-provisioned", "not-provisioned@devseams.svac.internal", "Not provisioned (until granted mid-run)",
            HasMfaClaim: true, ProvisionRow: false, Roles: Array.Empty<StaffRole>());

        return new[] { founder }.Concat(perRole).Append(noMfa).Append(noStaffRow).ToArray();
    }
}
