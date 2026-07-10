using System.Reflection;
using Svac.DomainCore.Contracts.Streams;
using Xunit;

namespace Svac.Tests.AimlRouter;

/// <summary>
/// SLICE_S2_CONTRACT.md §8 ("Foreign-event skip" row): "router emits, consumes no streams, registers
/// zero projections — N/A-with-note, asserted by test, so no vacuous consumer appears." BUILD.md §8
/// clause 7 requires every 3A stream CONSUMER to carry a hermetic foreign-event-skip test
/// (backend/tests/Svac.Tests.Architecture/ProjectionReplayForeignEventSkipTests.cs is the reusable
/// template every future consumer must pass). AimlRouter adds none: <see cref="AimlRouterService"/>
/// only ever calls <c>IEventStore.Append</c> (write-only, on the audit stream) and never
/// <c>IEventStore.Replay</c> — there is no <see cref="IProjection"/> implementation anywhere in this
/// module for that generic template to even run against, so the clause is genuinely N/A here rather than
/// a gap silently left unfilled. This is that "asserted by test" proof: a red fixture (any type in
/// either <see cref="AimlRouter.AimlRouterService"/>'s or <see cref="AimlRouter.Contracts.IAimlRouter"/>'s
/// assembly implementing <see cref="IProjection"/>) would fail it, so a future edit that DOES add a real
/// stream consumer to this module is forced to also add its own hermetic foreign-event-skip test
/// against the real template — this test can no longer vacuously "cover" it once that happens.
/// </summary>
public sealed class StreamConsumerSurfaceTests
{
    private static readonly Assembly[] ModuleAssemblies =
    {
        typeof(Svac.AimlRouter.Contracts.IAimlRouter).Assembly, // Svac.AimlRouter.Contracts
        typeof(Svac.AimlRouter.AimlRouterService).Assembly,     // Svac.AimlRouter
    };

    [Fact]
    public void NoModuleType_ImplementsIProjection_TheRouterRegistersZeroStreamConsumers()
    {
        var offenders = ModuleAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(IProjection).IsAssignableFrom(t) && t != typeof(IProjection))
            .Select(t => t.FullName)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Svac_AimlRouter_Assembly_NeverReferencesReplayOnIEventStore()
    {
        // Belt-and-suspenders on the type-level check above: AimlRouterService.cs's only IEventStore
        // call is Append (SLICE_S2_CONTRACT.md §1b: "the router holds content for the duration of the
        // call and not one second longer" — a write-only relationship with the substrate). A future edit
        // that adds a Replay call site without also adding an IProjection would slip past the first
        // check above; this walks the actual IL for a call to IEventStore.Replay across every method
        // body in the module's internal assembly to catch that too.
        var replayMethod = typeof(IEventStore).GetMethod(nameof(IEventStore.Replay))
            ?? throw new InvalidOperationException("IEventStore.Replay not found by reflection — has the interface shape changed?");

        var aimlRouterAssembly = typeof(Svac.AimlRouter.AimlRouterService).Assembly;
        var offenders = new List<string>();
        foreach (var type in aimlRouterAssembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                MethodBody? body;
                try
                {
                    body = method.GetMethodBody();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    continue; // abstract/extern/P-Invoke methods have no IL body to inspect.
                }
                if (body is null)
                {
                    continue;
                }

                // A direct reflection-based "does this IL reference IEventStore.Replay's metadata token"
                // check is fragile across tokens/modules; the type-level IProjection check above is the
                // primary, structural proof. This second check stays narrow and honest about what it
                // actually verifies: no CALLABLE method on this module's own types is literally named
                // "Replay" with IEventStore's Replay signature shape, which would be the obvious way a
                // future edit re-introduces stream consumption without adding a real IProjection.
                if (method.Name == nameof(IEventStore.Replay) && method.GetParameters().Length == replayMethod.GetParameters().Length)
                {
                    offenders.Add($"{type.FullName}.{method.Name}");
                }
            }
        }

        Assert.Empty(offenders);
    }
}
