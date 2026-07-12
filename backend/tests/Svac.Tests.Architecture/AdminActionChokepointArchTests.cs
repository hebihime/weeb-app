using System.Text.RegularExpressions;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// SLICE_S5_CONTRACT.md §1c's arch rule (red-fixture): "no type outside AdminActionExecutor's namespace
/// invokes a mutating domain-contract member (IConfigRegistry.SetValue, ILedger.Reverse, staff-directory
/// writes, future desk verbs) from Svac.AdminHost*. UI code literally cannot skip the chokepoint."
///
/// A text scan over every real <c>.cs</c> file under <c>backend/admin-host</c> (mirrors
/// AdminHostBoundaryTests.cs/ModuleBoundaryTests.cs's own text-scan discipline for cross-cutting
/// invariants a pure assembly-reference or reflection check cannot express) for three known mutating-
/// member call SHAPES, keyed on this codebase's own consistent constructor-parameter naming convention
/// (verified against every real call site: <c>ConfigRegistry(CoreDbContext db, IEventStore eventStore)</c>,
/// every <c>AdminActionExecutor</c>/<c>StaffBootstrapper</c> constructor param named exactly
/// <c>configRegistry</c>/<c>eventStore</c>/<c>adminDb</c>):
///   - <c>configRegistry.SetValue(</c>          — the 9A config mutation verb
///   - <c>eventStore.Append/Reverse/Tombstone(</c> — the 3A event-store mutation verbs
///   - <c>adminDb.StaffAccounts.Add(</c> / <c>adminDb.StaffRoleGrants.Add(</c> — staff-directory writes
///
/// Allowlisted BOTH by directory (never scanned) and by call-shape (stripped before scanning):
///   - <c>**/Execution/**</c> — the chokepoint's own implementation; it is what the rule protects the
///     door of, not a caller subject to it.
///   - <c>**/Bootstrap/**</c> — <c>StaffBootstrapper</c> (SLICE_S5_CONTRACT.md §1b: "provision that
///     subject + SuperAdmin grant as a system-actor action" BEFORE any staff row can exist to re-read —
///     the ONE pre-existing, documented exception the executor's own step 1 structurally cannot serve,
///     landed and gated by Pass A, out of THIS pass's scope to relocate).
///   - <c>**/Auth/**</c> — Pass A's staff-auth transport subsystem (<c>DevSeamsStaffTransport</c>'s
///     idempotent self-provisioning of a dev fixture's OWN staff/grant rows at first sign-in — the same
///     "identity does not exist yet to re-read" structural exception as Bootstrap, one layer earlier;
///     <c>StaffSignInPipeline</c>'s <c>admin.signin.refused</c> audit event — a sign-in-pipeline concern,
///     never a staff-lifecycle MUTATION the executor's five actions govern). Landed and gated by Pass A,
///     out of THIS pass's scope to relocate.
///   - any call textually nested inside a <c>.Execute(</c> invocation's own argument list — the `work`
///     delegate callers (Svac.AdminHost.Staff.StaffRolesEndpointExtensions' handlers,
///     ConfigEditorBoundsTests.cs-style config-editor callers) legitimately perform exactly these
///     mutations, but ONLY because AdminActionExecutor.Execute invokes them AFTER Authorize/reason/
///     four-eyes all pass — the chokepoint's OWN control, not a bypass of it.
/// </summary>
public sealed class AdminActionChokepointArchTests
{
    private static readonly string[] ForbiddenPatterns =
    {
        @"configRegistry\.SetValue\s*\(",
        @"eventStore\.(Append|Reverse|Tombstone)\s*\(",
        @"adminDb\.StaffAccounts\.Add\s*\(",
        @"adminDb\.StaffRoleGrants\.Add\s*\(",
    };

    [Fact]
    public void NoTypeOutsideTheExecutorOrBootstrap_InvokesAMutatingMemberOutsideAWorkDelegate()
    {
        var adminHostDir = Path.Combine(FindRepoRoot(), "backend", "admin-host");
        Assert.True(Directory.Exists(adminHostDir), "backend/admin-host is expected to exist since S5.");

        var violations = ScanForViolations(adminHostDir);

        Assert.Empty(violations);
    }

    [Fact]
    public void RedFixture_AMutatingCallOutsideTheExecutorAndOutsideAWorkDelegate_IsDetected()
    {
        const string fixtureSource = """
            namespace Fixture;

            public sealed class RogueStaffLifecycleShortcut(AdminDbContext adminDb, IEventStore eventStore, IConfigRegistry configRegistry)
            {
                public async Task Deactivate(string staffId)
                {
                    // Bypasses AdminActionExecutor entirely -- exactly the class of bug this rule exists to catch.
                    adminDb.StaffAccounts.Add(new StaffAccountEntity());
                    await eventStore.Append(StreamType.Audit, staffId, "admin.action.executed", null, default!, default, default);
                    await configRegistry.SetValue("some.key", 1, "reason", default, default!);
                }
            }
            """;

        var violations = FindViolationsInSource(fixtureSource);

        Assert.Equal(3, violations.Count); // all three forbidden shapes present, none nested in a .Execute( call.
    }

    [Fact]
    public void RedFixture_TheSameCallsInsideAnExecuteWorkDelegate_AreNotFlagged()
    {
        const string fixtureSource = """
            namespace Fixture;

            public sealed class LegitimateEndpointHandler(IAdminActionExecutor executor, AdminDbContext adminDb, IConfigRegistry configRegistry)
            {
                public async Task Handle(string staffId, string reason)
                {
                    await executor.Execute(default!, "admin.staff.deactivate", default, reason, async ctx =>
                    {
                        adminDb.StaffAccounts.Add(new StaffAccountEntity());
                        await configRegistry.SetValue("some.key", 1, reason, ctx.Actor, ctx);
                    });
                }
            }
            """;

        var violations = FindViolationsInSource(fixtureSource);

        Assert.Empty(violations); // both calls are inside the .Execute( call's own argument list -- gated by the chokepoint, not a bypass.
    }

    private static List<string> ScanForViolations(string adminHostDir)
    {
        var violations = new List<string>();
        foreach (var path in EnumerateRealCsFiles(adminHostDir))
        {
            var normalized = path.Replace('\\', '/');
            if (normalized.Contains("/Execution/", StringComparison.Ordinal) ||
                normalized.Contains("/Bootstrap/", StringComparison.Ordinal) ||
                normalized.Contains("/Auth/", StringComparison.Ordinal))
            {
                continue; // the chokepoint itself, plus Pass A's two pre-existing, documented exceptions.
            }

            var content = File.ReadAllText(path);
            foreach (var finding in FindViolationsInSource(content))
            {
                violations.Add($"{path}: {finding}");
            }
        }
        return violations;
    }

    /// <summary>
    /// Blanks out every <c>.Execute(...)</c> call's own argument list (balanced-paren scan, string-
    /// literal-aware) before running the forbidden-pattern regexes over what remains — a call inside a
    /// `work` delegate is legitimate BECAUSE it is invoked from behind the chokepoint's own gating, never
    /// a bypass of it.
    /// </summary>
    private static List<string> FindViolationsInSource(string content)
    {
        var stripped = StripExecuteCallArguments(content);
        var violations = new List<string>();
        foreach (var pattern in ForbiddenPatterns)
        {
            foreach (Match m in Regex.Matches(stripped, pattern))
            {
                violations.Add($"forbidden call \"{m.Value.TrimEnd('(', ' ')}\" outside any .Execute( work delegate");
            }
        }
        return violations;
    }

    private static string StripExecuteCallArguments(string content)
    {
        var result = content;
        var searchFrom = 0;
        while (true)
        {
            var callSite = result.IndexOf(".Execute(", searchFrom, StringComparison.Ordinal);
            if (callSite < 0)
            {
                break;
            }

            var openParenIndex = callSite + ".Execute".Length;
            var closeParenIndex = FindMatchingCloseParen(result, openParenIndex);
            if (closeParenIndex < 0)
            {
                break; // unbalanced -- give up blanking further (never crash the scan itself on odd input).
            }

            var argStart = openParenIndex + 1;
            var argLength = closeParenIndex - argStart;
            var blanked = new string(' ', argLength);
            result = string.Concat(result.AsSpan(0, argStart), blanked, result.AsSpan(closeParenIndex));
            searchFrom = closeParenIndex + 1;
        }
        return result;
    }

    /// <summary>Balanced-paren scan from an opening '(' at <paramref name="openParenIndex"/>, skipping over string/char literals so a literal containing a stray paren never miscounts.</summary>
    private static int FindMatchingCloseParen(string text, int openParenIndex)
    {
        var depth = 0;
        for (var i = openParenIndex; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                i = SkipStringLiteral(text, i);
                continue;
            }
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }
        return -1;
    }

    private static int SkipStringLiteral(string text, int quoteIndex)
    {
        var i = quoteIndex + 1;
        while (i < text.Length)
        {
            if (text[i] == '\\')
            {
                i += 2;
                continue;
            }
            if (text[i] == '"')
            {
                return i;
            }
            i++;
        }
        return i;
    }

    // ------------------------------------------------------------------------------------------------
    // SECURITY_REVIEW_S5.md S5-06 (MEDIUM, Lens2, DEFERRED): StripExecuteCallArguments blanks the
    // argument list of ANY ".Execute(" call it finds, regardless of the receiver's TYPE — it keys purely
    // off the literal substring. A decoy type that also happens to expose a method named "Execute" (never
    // IAdminActionExecutor) shields a real bypass from this scan just by being named the same thing.
    // ------------------------------------------------------------------------------------------------
    [Fact(Skip = "deferred: SECURITY_REVIEW_S5.md S5-06 (StripExecuteCallArguments blanks ANY .Execute( call regardless of receiver type -- a decoy .Execute( method hides a real chokepoint bypass from the scan) -> assert the callee is IAdminActionExecutor")]
    public void RedFixture_DecoyExecuteReceiver_IsFlagged()
    {
        const string fixtureSource = """
            namespace Fixture;

            public sealed class DecoyExecutor
            {
                public void Execute(System.Action work) => work();
            }

            public sealed class RogueCaller(AdminDbContext adminDb, DecoyExecutor decoy)
            {
                public void Handle()
                {
                    decoy.Execute(() => adminDb.StaffAccounts.Add(new StaffAccountEntity()));
                }
            }
            """;

        var violations = FindViolationsInSource(fixtureSource);

        // Desired: a mutating call hidden inside a NON-IAdminActionExecutor's own ".Execute(" method must
        // still be flagged. Today it is NOT (StripExecuteCallArguments blanks the argument list of
        // `decoy.Execute(...)` exactly as if it were the real chokepoint), so this currently returns empty.
        Assert.NotEmpty(violations);
    }

    // ------------------------------------------------------------------------------------------------
    // SECURITY_REVIEW_S5.md S5-07 (MEDIUM, Lens2, DEFERRED): ScanForViolations' directory allowlist uses
    // Contains("/Auth/") (and "/Execution/", "/Bootstrap/") — matching ANY path with that segment
    // ANYWHERE, not just the real Pass-A auth subsystem's own top-level Auth/ directories. A decoy nested
    // "Auth" segment under an unrelated directory (e.g. Desks/Auth/Rogue.cs) is skipped by this rule even
    // though it is not one of the two documented pre-existing exceptions this allowlist exists for.
    // ------------------------------------------------------------------------------------------------
    [Fact(Skip = "deferred: SECURITY_REVIEW_S5.md S5-07 (dir allowlist matches ANY nested /Auth//Execution//Bootstrap/ segment -- a bare mutating call in Desks/Auth/Rogue.cs would pass unscanned) -> anchor the allowlist to the exact Pass-A paths")]
    public void RedFixture_NestedAuthDir_StillScanned()
    {
        // NOT a real Pass-A auth file -- a decoy nested "Auth" segment under an unrelated Desks/ directory.
        var decoyPath = string.Join(Path.DirectorySeparatorChar, DecoyPathSegments);

        // Desired: only the REAL Pass-A auth directories are allowlisted, anchored to their exact paths --
        // this decoy nested segment must still be considered SCANNED (not skipped) so a mutating call
        // hidden inside it would be caught. Today's rule (reproduced verbatim below) incorrectly skips it.
        Assert.False(MatchesCurrentUnanchoredAllowlist(decoyPath));
    }

    private static readonly string[] DecoyPathSegments = { "backend", "admin-host", "Svac.AdminHost", "Desks", "Auth", "Rogue.cs" };

    /// <summary>Verbatim reproduction of ScanForViolations' CURRENT (unfixed) directory-skip condition —
    /// kept as its own named method so the red-fixture proof above documents the EXACT rule being
    /// challenged, never a paraphrase that could silently drift from the real one.</summary>
    private static bool MatchesCurrentUnanchoredAllowlist(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/Execution/", StringComparison.Ordinal)
            || normalized.Contains("/Bootstrap/", StringComparison.Ordinal)
            || normalized.Contains("/Auth/", StringComparison.Ordinal);
    }

    private static IEnumerable<string> EnumerateRealCsFiles(string dir) =>
        Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !p.EndsWith(".g.cs", StringComparison.Ordinal)
                     && !p.EndsWith(".AssemblyInfo.cs", StringComparison.Ordinal));

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "docker-compose.yml")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException("could not locate repo root from " + AppContext.BaseDirectory);
    }
}
