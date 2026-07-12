# SECURITY_REVIEW_S5.md — slice S5 (admin/staff trust boundary) remediation record

**Scope:** `backend/admin-host/**`, `backend/tests/Svac.Tests.AdminHost/**`, `backend/tests/Svac.Tests.Architecture/**`
(the two new admin-host-specific arch tests), `backend/Directory.Packages.props`,
`tools/dead-tunable-lint/**`. Five adversarial lenses (fable) on the staff/admin trust boundary → triage
(orchestrator) → this remediation pass (Sonnet, one now-green regression test per fixed finding, one
Skip-annotated proof test per deferred finding). **Disposition rule (SLICE_PLAYBOOK):** CRITICAL/HIGH
fixed in-slice; the seeded last-SuperAdmin lockout guard (CRITICAL→lockout) is a fixNow item by explicit
instruction regardless of its triage severity label. Remaining MEDIUM/LOW deferred with a
`[Fact(Skip="deferred: SECURITY_REVIEW_S5.md <id>")]` (or node `{ skip: "..." }`) proof test, carried to
the slice named in its own finding text or, absent one, "the next admin-host desk slice that touches the
same file."

## Mandated verdicts

None. This pass required no founder escalation and ratified no open question — every finding resolved
inside the fixNow/defer/documented-residual disposition without needing a ruling above the review itself.

## FIX NOW — 5 findings, each with a now-green regression test

| id | sev | finding | fix | test (file) |
|---|---|---|---|---|
| S5-01 | HIGH | Ungated `GET /config` leaked the FULL 9A registry to anonymous/any-staff requests — no `ActorKind.Staff` check, no policy row, no `_canView` guard | new `admin.config.read` policy row (`AdminPolicyTableSource.cs`, StaffRoles = {SuperAdmin, EconomyOps} — the union of every role that can commit at least one `core.config.set.*` action); `ConfigRegistry.razor.cs`'s `OnInitializedAsync` now gates via a direct `IPolicyEngine.Authorize` call (mirrors Dashboard.razor.cs's own pattern) before ever calling `ListEntries()`; `ConfigRegistry.razor` wraps the whole page body in `@if (_canView)`/`else` (access-denied card); `ConfigRegistryDeskModule.VisibleTo` narrowed from all six roles to {SuperAdmin, EconomyOps} so the nav link itself obeys the same allowlist | `ConfigRegistryReadGateHttpTests.cs` (4 live-HTTP tests: SafetyAgent and Anonymous see zero row/value testids and the raw value never appears in the response body; SuperAdmin and EconomyOps are unaffected) |
| S5-02 | MEDIUM→trust | `EntraClaimTypes.HasMfaClaim` treated ANY non-empty `acr` claim as MFA-satisfied (`!IsNullOrWhiteSpace`) — a plain `pwd`/`acr:1` single-factor sign-in with an unrelated authentication-context claim would have passed | `HasMfaClaim` now takes the tenant's configured `acr` value set (`StaffAuthEntraConfig.AcrValues`, wired from `SVAC_ENTRA_MFA_ACR_VALUES`, comma-separated) and only counts an `acr` claim that MATCHES one of those values; an unconfigured (empty) set means `acr` contributes nothing — `amr`-contains-`mfa` alone still works, so this is strictly narrower than before, never a fail-open widening | `EntraClaimTypesTests.cs` (6 tests: the exact pre-fix "any acr" shape no longer satisfies MFA with no configured values; a matching configured value still does; amr-contains-mfa unaffected) |
| S5-03 | CRITICAL→lockout | No last-active-SuperAdmin guard: a self-deactivate or self-revoke of `super_admin` could drop the active-SuperAdmin count to zero with no in-app recovery | new executor step 5b in `AdminActionExecutor.Execute`, between four-eyes and `work()`: `WouldZeroActiveSuperAdmins` runs a race-safe `SELECT ... FOR UPDATE` (no target-specific WHERE clause — always locks the FULL currently-active `super_admin` grant+account set) over `admin.staff_role_grants ⋈ admin.staff_accounts`, inside the SAME shared transaction; fires on `admin.staff.deactivate` (any target currently holding the grant) and `admin.staff.role_revoke` when the NEW `affectedRoleCode` parameter (threaded through `IAdminActionExecutor.Execute`/`StaffRolesEndpointExtensions.HandleRoleRevoke`, since the generic chokepoint otherwise never sees which role a revoke targets) is `super_admin`; returns `Denied("policy.denied.last_superadmin")`, audited exactly like every other `admin.action.refused` | `LastSuperAdminLockoutGuardTests.cs` (8 tests: self-deactivate/self-revoke of the lone SuperAdmin denied and untouched; a second active SuperAdmin makes it succeed; a target with no `super_admin` grant, or a revoke of a DIFFERENT role held by the lone SuperAdmin, is never blocked; two REAL concurrent Postgres transactions doing concurrent self-revoke / self-deactivate of the last two SuperAdmins — exactly one commits, one is denied BY THIS GUARD specifically, final count is always 1, never 0 or 2, neither call throws unhandled) |
| S5-04 | HIGH/CRITICAL-in-prod | Staff DataProtection key ring persisted to `core.data_protection_keys` in PLAINTEXT unconditionally — a DB dump alone yields the raw cookie/antiforgery signing key material, enabling founder-cookie forgery | `AddStaffAuth` now branches on `devSeamsEnabled`: DevSeams (guaranteed Development-only by `ProdFieldKeyVaultGuard`) keeps the plaintext `CoreDbXmlRepository` path; every other boot chains `.ProtectKeysWithAzureKeyVault(keyIdentifier, new DefaultAzureCredential())` (new `Azure.Extensions.AspNetCore.DataProtection.Keys`/`Azure.Identity` packages) against a key identifier derived from the ALREADY-required `SVAC_KEYVAULT_ENDPOINT` (Program.cs); fails CLOSED (throws) if that identifier is null on a non-DevSeams boot — defensive, since `ProdFieldKeyVaultGuard.Enforce` already refuses boot first in that exact situation; new arch guard `DataProtectionKeyProtectionArchTests` asserts every file calling `.AddDataProtection(` also calls `.ProtectKeysWithAzureKeyVault(` in the same file, red/green-fixture proven, so a future refactor can never silently strip the chaining back out | `DataProtectionKeyVaultGuardTests.cs` (3 DI-composition tests: non-DevSeams + no key identifier throws with "S5-04" in the message; non-DevSeams + a key identifier does not throw; DevSeams unaffected) + `DataProtectionKeyProtectionArchTests.cs` (4 tests: the real repo scan is clean; red/green fixtures both directions) |
| S5-05 | LOW→lockout | `StaffAccountsPurgeStoreExecutor` pseudonymized on `RetentionExpiry` with no status guard — an age-cutoff sweep could pseudonymize (destroying the `external_subject` Entra `oid` lookup key) and lock out an ACTIVE operator/founder, including the last SuperAdmin | no-op (`return 0`, row untouched) when `purgeClass == RetentionExpiry && row.Status != "deactivated"` — `StatutoryErasure` (a real DSR) is completely unaffected by this guard and still pseudonymizes an active row exactly as before, per its own registration | 3 new tests in `StaffPurgeTests.cs`: an active row on `RetentionExpiry` no-ops (byte-identical, `external_subject` survives); a deactivated row on `RetentionExpiry` still pseudonymizes (the guard never blocks its own intended case); an active row on `StatutoryErasure` still pseudonymizes (the guard is scoped to `RetentionExpiry` only, never a way to dodge a real erasure obligation) |

## DEFER — 9 findings, Skip-annotated proof test, carried

| id | sev | finding | carried to |
|---|---|---|---|
| S5-06 | MEDIUM, Lens2 | `AdminActionChokepointArchTests.StripExecuteCallArguments` blanks any `.Execute(` call's argument list regardless of the RECEIVER's type — a decoy type exposing its own `.Execute(` method shields a real chokepoint bypass hidden inside it from the scan | next touch of `AdminActionChokepointArchTests.cs` (S5-07's own fix is the natural place to land this too — assert the callee resolves to `IAdminActionExecutor`) |
| S5-07 | MEDIUM, Lens2 | The SAME test's directory allowlist (`Contains("/Auth/")`/`/Execution/`/`/Bootstrap/`) matches ANY nested segment with that name anywhere in the path — a decoy `Desks/Auth/Rogue.cs` is skipped even though it is not one of the two documented Pass-A exceptions | next touch of `AdminActionChokepointArchTests.cs` — anchor the allowlist to the exact Pass-A paths |
| S5-08 | MEDIUM, Lens2 | The four-eyes exemption (`AdminActionExecutor` step 5) keys off the COMPUTED least-privileged hat (`HatFor.SelectLeastPrivileged`), not "does this actor hold SuperAdmin at all" — a dual-role SuperAdmin+EconomyOps actor on `core.config.set.ops` computes hat=EconomyOps and is fail-closed over-refused when four-eyes is armed | the desk slice that next revisits `AdminActionExecutor`'s four-eyes step (test on `rolesHeld.Contains(SuperAdmin)`, not the computed hat) |
| S5-09 | LOW-latent, Lens2 | `PolicyEngine`'s null-`StaffRoles` Role-axis skip composes with `AdminActionExecutor`'s null-hat four-eyes skip: a hypothetical FUTURE `RequiresReason` row with `StaffRoles=null` would let a zero-grant Staff actor reach `work()` completely ungated even with four-eyes armed. No shipped row hits it (every real `admin.*` row explicitly types `StaffRoles`; the one null-`StaffRoles` row, `admin.host.transport`, has `RequiresReason=false`) | the first future policy row that legitimately wants `StaffRoles=null` AND `RequiresReason=true` — guard the composition before that row ships |
| S5-10 | LOW, Lens2 | `AdminActionExecutor.IsFourEyesArmed` swallows `KeyNotFoundException` → `false` — safe reasoning for an unseeded unit-test key, but a key DROPPED in prod (manifest edit, botched migration, manual delete) silently DISARMS the control instead of failing closed, unlike every other guard in this codebase | the ops-desk slice that next touches `admin.four_eyes_required` (fail closed on a missing key) |
| S5-11 | LOW, Lens5 F1 | Every config/staff mutation endpoint reads its antiforgery token off `Request.Form` manually (never `[FromForm]`-bound) and never calls `IAntiforgery.ValidateRequestAsync` — `app.UseAntiforgery()` alone does not retroactively validate a hand-read form; mitigated ONLY by `SameSite=Lax` | the desk slice that next adds a mutation endpoint (call `ValidateRequestAsync` on every mutation POST, then backfill the existing five/two) |
| S5-12 | LOW, Lens5 F2 | `ConfigRegistry.SetValue` (domain-core, the ONE place either write path touches the config table) has no scope check at all; `HandleConfirm` never rechecks scope either — the set-scope write-refusal rests entirely on a single `HandlePropose` line | the domain-core slice that next touches `ConfigRegistry.SetValue` (assert `entry.Scope != "set"` inside `SetValue` itself — out of THIS pass's scope per "never edit domain-core contracts") |
| S5-13 | LOW, Lens5 F3 | `PendingConsumerSliceLint.Validate` (labeled the "authoritative SPEC") has no `doneSlices` parameter at all, so it cannot catch a key pending on an ALREADY-DONE slice the way `dead-tunable-lint.mjs`'s node mirror does; that node mirror's own `parseDoneSlices` `\bDONE\b` match additionally has no negation handling ("NOT DONE yet" is misread as done) | the next config-manifest-touching slice (add the `doneSlices`/DONE check to the C# lint; harden the node token match) |
| S5-14 | LOW, Lens6 | `UserSearch.razor.cs`'s `OnInitializedAsync` returns BEFORE calling `UserSearchExecutionService.Execute` when `Query` is empty/whitespace or `QueryClassRaw` fails to parse — that request is neither audited (`admin.user_search.executed`) nor quota-consumed, deviating from §0's "EVERY query (even empty) is audited ... and quota-consumed." Not an enumeration vector — a detection-completeness gap | the User Search desk's next revisit (move the empty-term decision into the service as a typed outcome) |

Each id above has a `[Fact(Skip=...)]` (or node `test(..., { skip: ... })`) proof test already in the tree,
described in its own doc comment, red the moment it is un-skipped:
`AdminActionChokepointArchTests.cs` (S5-06, S5-07), `DeferredFindingsProofTests.cs` (S5-08, S5-09, S5-10,
S5-12), `V0BatchManifestTests.cs` (S5-13, C# half), `dead-tunable-lint.test.mjs` (S5-13, node half),
`DeferredFindingsHttpProofTests.cs` (S5-11, S5-14).

## DOCUMENTED — accepted residuals (no fix, honest note)

- **S5-04 depends on a Julien-executed action.** `ProtectKeysWithAzureKeyVault` is real, wired code — but it
  is inert until `SVAC_KEYVAULT_ENDPOINT` names a real Azure Key Vault and the deploying identity has
  `wrapKey`/`unwrapKey`/`get` on the key `svac-admin-dataprotection` (OQ-3, the same pending-subscription
  item `ProdFieldKeyVaultGuard` already gates the field-encryption seam on). Until Julien provisions that,
  any non-Development boot throws at startup by design — fail-closed, not silently plaintext. Not a gap;
  the correct posture pending real infrastructure.
- **S5-02 depends on a Julien-executed action.** `SVAC_ENTRA_MFA_ACR_VALUES` must be set to the real Entra
  tenant's Conditional Access `acr` value(s) once the staff tenant/CA policy exists (the SAME OQ-3-adjacent
  "Entra servers cannot be exercised without the tenant" honesty note S5's own contract already records).
  Until then, `acr` contributes nothing to the MFA decision — `amr`-contains-`mfa` is the only live signal,
  which is a narrowing, not a weakening, of pre-fix behavior.
- **S5-11's residual mitigation (SameSite=Lax) is real, not decorative.** A genuine cross-site POST never
  carries the `.Svac.AdminAuth` cookie at all under `SameSite=Lax`, so the deferred gap is scoped to a
  same-site vector (XSS elsewhere on the same origin, an open redirect, a misconfigured subdomain) — a real
  but narrower residual than "no CSRF protection," which is why S5-11 is LOW, not HIGH.

## Verified sound (attacked, held)

- The audited-action chokepoint's 7-step invariant (one shared transaction spanning `AdminDbContext` +
  `CoreDbContext`, exactly one audit event per action, `work()` never invoked before every gate passes) is
  UNCHANGED by the new step 5b — it composes as an eighth gate in the same shared transaction, proven by
  the full `AdminActionExecutorTests`/`ConfigEditorBoundsTests` suites staying green untouched.
- The "no DELETE" staff-lifecycle law (deactivate/revoke are state transitions, never row surgery) holds
  through every S5-03/S5-05 change — neither new guard adds a code path that removes a row.
- The 13A purge-registry completeness invariant (`PurgeCompletenessAdversaryTests`/
  `PurgeCompletenessIdentityTests`) is unaffected by S5-05's guard — the registered verb per store/class
  didn't change, only a runtime precondition on when `Pseudonymize` actually executes.
- `admin.config.read`'s new row does not need a slot in `AdminActionKeys.All`/`RequireAdminActionsCovered`
  — it is a direct `IPolicyEngine.Authorize` VIEW gate (mirrors `admin.dashboard.read`, which is ALSO absent
  from that list), never routed through `IAdminActionExecutor`. Confirmed by the full boot-refusal suite
  (`BootHttpTests`, `BootRefusalTests`, `DependencyInjectionTests`) staying green.
- `IAdminActionExecutor.Execute`'s new trailing optional `affectedRoleCode` parameter (placed BEFORE `ct`,
  not after — CA1068 requires `CancellationToken` last) required fixing seven existing positional
  `ct)`-ending call sites to name the argument (`ct: ct`); verified none of the seven's OWN behavior
  changed (all pass unchanged CancellationToken values), only the fixed IAdminActionExecutor.Execute
  overload resolution.
- Azure.Identity bumped 1.17.2 → 1.21.0 (Directory.Packages.props) to resolve a genuine upstream Azure SDK
  CS0433 (Azure.Core ≥1.53 now exports `DefaultAzureCredential` itself; Azure.Identity <1.21.0 still ships
  its own now-duplicate copy) — confirmed via `Azure/azure-sdk-for-net#58822` and
  `AzureAD/microsoft-identity-web#3791`; not a downgrade, not a workaround, the documented upstream fix.

## Gate result (actual — run by me, this session)

```
$ dotnet build backend/Svac.sln --no-restore
Build succeeded. 0 Warning(s). 0 Error(s).

$ dotnet test backend/tests/Svac.Tests.Architecture --no-build
Passed! - Failed: 0, Passed: 187, Skipped: 5, Total: 192, Duration: 2m 29s
  (skips: 3 pre-existing S1/S2/S3 defers + AdminActionChokepointArchTests.RedFixture_DecoyExecuteReceiver_IsFlagged (S5-06)
   + AdminActionChokepointArchTests.RedFixture_NestedAuthDir_StillScanned (S5-07))

$ dotnet test backend/tests/Svac.Tests.AdminHost --no-build
Passed! - Failed: 0, Passed: 97, Skipped: 7, Total: 104, Duration: 1m 28s
  (skips: the 7 S5 DEFER proof tests: S5-08, S5-09, S5-10, S5-11, S5-12, S5-13, S5-14)

$ dotnet test backend/tests/Svac.Tests.DomainCore --no-build
Passed! - Failed: 0, Passed: 51, Skipped: 0, Total: 51

$ dotnet test backend/tests/Svac.Tests.AimlRouter --no-build
Passed! - Failed: 0, Passed: 51, Skipped: 2, Total: 53 (2 pre-existing S2 defers, unrelated to S5)

$ dotnet test backend/tests/Svac.Tests.Identity --no-build
Passed! - Failed: 0, Passed: 87, Skipped: 5, Total: 92 (5 pre-existing S3 defers, unrelated to S5)

$ node --test tools/dead-tunable-lint/*.test.mjs
tests 15, pass 14, fail 0, skipped 1 (S5-13's node half)

$ node --test tools/i18n-lint/*.test.mjs
tests 24, pass 24, fail 0, skipped 0 — includes the razor hardcoded-literal scan over the edited
ConfigRegistry.razor, clean

$ node tools/contract-lint/contract-lint.mjs
contract-lint OK: openapi.v0.json passes all v0 rules

$ node --test tools/contract-lint/*.test.mjs
tests 30, pass 28, fail 0, skipped 2 (pre-existing, unrelated)
```

**Zero fails across every deterministic suite this repo has.** Total across all five .NET test projects:
473 passed, 0 failed, 19 skipped, 492 total — 9 of the 19 skips are this pass's own S5 defer proofs
(2 in Architecture: S5-06, S5-07; 7 in AdminHost: S5-08, S5-09, S5-10, S5-11, S5-12, S5-13, S5-14), the
other 10 are pre-existing S1/S2/S3 defers unrelated to S5.

## Notable design decisions worth flagging explicitly

- **`IAdminActionExecutor.Execute` gained a trailing optional `string? affectedRoleCode` parameter,
  placed BEFORE `CancellationToken ct`** (not after — Roslyn's CA1068 requires `ct` stay the LAST
  parameter). Every existing caller that positionally passed `ct` as its sixth argument (`UserSearchExecutionService`,
  `AuditViewExecutionService`, all five `StaffRolesEndpointExtensions` handlers, `ConfigRegistryEndpointExtensions`'s
  two call sites) was updated to `ct: ct` (named) — a real, necessary, minimal signature change (never a
  domain-core contract; `IAdminActionExecutor` is admin-host's own interface), not a workaround.
- **The last-SuperAdmin guard is deliberately scoped to two actions, keyed by a passed-in role code for
  revoke** — a naive "block any mutation on the account of the last SuperAdmin" would have incorrectly
  blocked revoking an unrelated role (e.g. `economy_ops`) from a dual-role lone SuperAdmin; `RoleRevoke_ANonSuperAdminRole_OnTheLastActiveSuperAdmin_IsNeverBlockedByThisGuard`
  proves the guard's precision holds.
- **The two concurrency proofs for S5-03 use SELF-action (self-revoke/self-deactivate), not MUTUAL
  action.** A first draft used "A revokes B / B revokes A concurrently" and it is a genuine trap: whichever
  transaction commits first strips the LOSER's own acting authority, so the loser is denied by ORDINARY
  `PolicyEngine.Authorize` (no longer holding the role) rather than by this guard specifically — the
  end-to-end safety invariant (never zero, exactly one survives) still holds either way, but the test would
  no longer be proving THIS guard's own FOR-UPDATE race-safety. Self-action isolates it: neither actor's
  own grant/status is touched by the other's transaction, so only the shared guard can determine the
  outcome. Caught by actually running the test (it failed) before writing this report — the record CLAUDE.md
  asks for is the real one, not the one I expected to pass.
