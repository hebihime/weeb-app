using System.Text.RegularExpressions;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// TODOS.md's 2026-07-12 founder ruling (staff-row deletion / least-privileged DB role): "the
/// accountability threat (audit chain must always resolve stf_/srg_ ids) is closed in S5 at the APP
/// LAYER: lifecycle is state-transition only (deactivated_at/revoked_at), no DELETE code path, arch test
/// asserts no Remove/ExecuteDelete on the two staff entities (Pass B)." SLICE_S5_CONTRACT.md §2 states
/// the same law directly: "revocation = revoked_at state transition, NEVER a DELETE" — a staff account is
/// deactivated (never removed), a role grant is revoked (never removed), so any audit event's actor_ref/
/// target_ref naming a stf_/srg_ id always resolves to a real row, forever.
///
/// A text scan over every real <c>.cs</c> file under <c>backend/admin-host</c> for the two ways EF Core
/// can delete a tracked/untracked row — <c>DbSet&lt;T&gt;.Remove</c>/<c>RemoveRange</c> (change-tracked
/// delete, staged for the next SaveChanges) and <c>IQueryable&lt;T&gt;.ExecuteDelete</c>/
/// <c>ExecuteDeleteAsync</c> (a direct SQL DELETE, no tracking) — scoped to <c>StaffAccounts</c>/
/// <c>StaffRoleGrants</c>, the DbContext's own two DbSet property names (AdminDbContext exposes no
/// others, so this scope is already exhaustive for this schema).
/// </summary>
public sealed class StaffLifecycleNeverDeletesArchTests
{
    private static readonly Regex RemoveCallPattern = new(
        @"\.(StaffAccounts|StaffRoleGrants)\s*\.\s*(Remove|RemoveRange)\s*\(", RegexOptions.Compiled);

    /// <summary>Line-based co-occurrence (this codebase's own LINQ style keeps a query + its terminal
    /// operator on one line, e.g. Svac.Identity.Purge.PurgeExecutors.cs's own ExecuteDeleteAsync call
    /// sites) — a real ExecuteDelete call naming either DbSet anywhere on the SAME line is a violation.</summary>
    private static readonly Regex ExecuteDeletePattern = new(@"\.ExecuteDelete(Async)?\s*\(", RegexOptions.Compiled);

    [Fact]
    public void NoTypeInTheAdminHost_EverCallsRemoveOrExecuteDelete_OnEitherStaffEntity()
    {
        var adminHostDir = Path.Combine(FindRepoRoot(), "backend", "admin-host");
        Assert.True(Directory.Exists(adminHostDir), "backend/admin-host is expected to exist since S5.");

        var violations = new List<string>();
        foreach (var path in EnumerateRealCsFiles(adminHostDir))
        {
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (RemoveCallPattern.IsMatch(line))
                {
                    violations.Add($"{path}:{i + 1}: {line.Trim()}");
                }
                if (ExecuteDeletePattern.IsMatch(line) &&
                    (line.Contains("StaffAccounts", StringComparison.Ordinal) || line.Contains("StaffRoleGrants", StringComparison.Ordinal)))
                {
                    violations.Add($"{path}:{i + 1}: {line.Trim()}");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void RedFixture_ARemoveCallOnEitherStaffDbSet_IsDetected()
    {
        bool MatchesRemove(string line) => RemoveCallPattern.IsMatch(line);

        Assert.True(MatchesRemove("adminDb.StaffAccounts.Remove(row);"));
        Assert.True(MatchesRemove("adminDb.StaffRoleGrants.RemoveRange(grants);"));
        Assert.False(MatchesRemove("var trimmed = someList.Remove(item);")); // unrelated .Remove( -- never a false positive on ordinary collection use.
    }

    [Fact]
    public void RedFixture_AnExecuteDeleteCallNamingEitherStaffEntityOnTheSameLine_IsDetected()
    {
        bool MatchesExecuteDelete(string line) => ExecuteDeletePattern.IsMatch(line);

        Assert.True(MatchesExecuteDelete("await adminDb.StaffAccounts.Where(s => s.Id == id).ExecuteDeleteAsync(ct);"));
        Assert.True(MatchesExecuteDelete("await adminDb.StaffRoleGrants.Where(g => g.StaffId == id).ExecuteDeleteAsync(ct);"));
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
