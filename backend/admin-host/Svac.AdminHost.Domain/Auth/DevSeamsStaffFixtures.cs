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

        var perRole = allRoles.Select(role => new DevSeamsStaffFixture(
            Key: role.ToString(),
            ExternalSubject: SubjectPrefix + role.ToString().ToLowerInvariant(),
            Email: $"{role.ToString().ToLowerInvariant()}@devseams.svac.internal",
            DisplayName: $"{role} (one role)",
            HasMfaClaim: true,
            ProvisionRow: true,
            Roles: new[] { role }));

        var noMfa = new DevSeamsStaffFixture(
            "no_mfa", SubjectPrefix + "no-mfa", "no-mfa@devseams.svac.internal", "No MFA claim (provisioned, SafetyAgent)",
            HasMfaClaim: false, ProvisionRow: true, Roles: new[] { StaffRole.SafetyAgent });

        var noStaffRow = new DevSeamsStaffFixture(
            "no_staff_row", SubjectPrefix + "no-staff-row", "no-staff-row@devseams.svac.internal", "No staff row (never provisioned)",
            HasMfaClaim: true, ProvisionRow: false, Roles: Array.Empty<StaffRole>());

        return new[] { founder }.Concat(perRole).Append(noMfa).Append(noStaffRow).ToArray();
    }
}
