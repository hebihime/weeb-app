using Svac.AimlRouter.Policy;
using Svac.DomainCore.Policy;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// ADVERSARIAL — auth / IDOR lens on SLICE S2 (aiml-router). Same lens as <see cref="AuthIdorLensTests"/>
/// (4A refusal, topology-is-not-a-guard, encoded identity), aimed at S2's own deliverable.
///
/// S2's ratified auth deliverable (SLICE_S2_CONTRACT.md §13 Correction 2) is NOT the Auth-F3 redesign —
/// it is to CONFIRM three absences "red-fixture both directions": (a) aiml.invoke carries no target
/// resource, (b) no request DTO maps to it, (c) no consumer-actor reachability exists. The break these
/// tests demonstrate: the confirmation that shipped (PolicyEntryShapeTests.cs) asserts the shape of a
/// policy-row CONSTANT that no enforced code path reads, and the one real HTTP surface that reaches the
/// router (the diagnostic host) both maps a request DTO onto InvokeAsync AND is open to the whole network
/// as a forged System actor. The absences are asserted, not enforced.
///
/// RED on purpose (same convention as AuthIdorLensTests): each asserts the property S2 PROMISES and the
/// code currently VIOLATES. The build phase turns them green by wiring the guard, not by deleting the test.
/// </summary>
public sealed class AuthIdorS2LensTests
{
    // ---------------------------------------------------------------------------------------------
    // FINDING S2-A — 4A refusal is asserted against an UNUSED constant, not the enforced table.
    //
    // SLICE_S2_CONTRACT.md §3: "One row, internal chokepoint ... the row's existence makes an ungated
    // route unshippable forever." AimlRouterPolicyEntries.AimlInvoke (Policy/AimlRouterPolicyEntries.cs:29)
    // is that row — but it is spliced into NOTHING. PolicyTable.cs (the single source of truth
    // StartupPolicyCoverage's boot-refusal and the generated action×axis matrix both read) has 7 rows and
    // aiml.invoke is not one of them. The only reader of AimlRouterPolicyEntries anywhere in the backend is
    // PolicyEntryShapeTests.cs, i.e. the "confirmation" test asserts the shape of a value the running
    // system never consults. So §3's "unshippable forever" guarantee is not true today: nothing structural
    // would refuse a future ungated router route — the row that is supposed to force the refusal is not in
    // the table the refusal check reads.
    // ---------------------------------------------------------------------------------------------
    [Fact(Skip = "deferred: SECURITY_REVIEW finding S2-A — splicing aiml.invoke into the enforced " +
        "domain-core PolicyTable is deliberately deferred by module doc to the first-real-consumer slice " +
        "(S2 §0 does not authorize editing PolicyTable, no route exists to gate yet); must land together " +
        "with the S1 catch-all-Map boot-refusal gap before any consumer mounts the router.")]
    public void EnforcedPolicyTable_MustContainAimlInvokeRow_SoAnUngatedRouterRouteCannotShip()
    {
        var enforcedTable = new PolicyTable();

        var aimlInvoke = enforcedTable.Find(AimlRouterPolicyEntries.Invoke);

        // The break: the enforced table returns null — the aiml.invoke row lives only as an unreferenced
        // constant in the module, never registered where the 4A boot-refusal / matrix suite would enforce
        // it. "The row's existence makes an ungated route unshippable forever" (§3) is not yet true.
        Assert.NotNull(aimlInvoke);
    }

    // ---------------------------------------------------------------------------------------------
    // FINDING S2-B — topology-is-not-a-guard: the one real route to the model egress is bound to all
    // interfaces and forges a System actor for any caller.
    //
    // Correction 2 (c): "no consumer-actor reachability exists." The router has no prod caller (Svac.PublicApi
    // never references it), but backend/e2e/aiml-router-diagnostic-host/Program.cs IS a real HTTP surface
    // that maps InvokeDiagnosticRequest (Task/Caller/PayloadClass from the body) onto IAimlRouter.InvokeAsync
    // under a hardcoded ActorRef.System (Program.cs:80), with NO authentication. Its own only consumer,
    // backend/e2e/aiml-router.e2e.mjs:102, reaches it over http://localhost — yet Program.cs:32 binds
    // http://0.0.0.0:{port}. So on any shared dev/CI network, an unauthenticated peer can POST /invoke and
    // drive the developer's authenticated `claude` CLI session (model output capture, spend, data egress) as
    // the System actor with an arbitrary Caller, and PayloadClass=NonPersonal sails past the
    // refuse-all-special-category egress authorizer. "Test tooling, never shipped" is a deployment
    // convention (topology), not a structural guard; the socket is open regardless.
    //
    // Break (inputs -> wrong result): from another host on the LAN/CI network, with the diagnostic host up:
    //   curl -sXPOST http://<devbox-ip>:5299/invoke -H 'content-type: application/json' \
    //        -d '{"Task":"Generate","Caller":"Integrity","PayloadClass":"NonPersonal","UserText":"exfil"}'
    //   -> HTTP 200 with model output, executed as ActorRef.System / Caller=Integrity, no credential.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void DiagnosticHost_ModelEgressEndpoint_MustBindLoopbackOnly()
    {
        var programSource = ReadRepoFile("backend/e2e/aiml-router-diagnostic-host/Program.cs");

        // The break: the unauthenticated, System-actor-forging /invoke route listens on every interface,
        // when its sole consumer connects over loopback. Bind 127.0.0.1 (or [::1]) instead.
        Assert.DoesNotContain("0.0.0.0", programSource);
    }

    // --- helpers -------------------------------------------------------------------------------

    private static string ReadRepoFile(string repoRelativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, repoRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"could not locate {repoRelativePath} walking up from {AppContext.BaseDirectory}");
    }
}
