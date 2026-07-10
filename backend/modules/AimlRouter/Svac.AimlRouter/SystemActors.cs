using Svac.AimlRouter.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Deterministic;

namespace Svac.AimlRouter;

/// <summary>
/// Mints the STABLE `sys_&lt;caller_module&gt;` actor identity the 10A quota key is consumed against
/// (SLICE_S2_CONTRACT.md §5: "actor = sys_&lt;caller_module&gt;"). Deterministic on purpose — the same
/// <see cref="CallerModule"/> must resolve to the SAME actor_ref across process restarts, or the daily
/// quota window's counter row would fork every time the host recycles. Built over the same pure Ulid
/// codec + the existing "sys" IdPrefixes.System prefix (already a member of domain-core's closed set),
/// with a fixed timestamp and a caller-seeded (never wall-clock, never process Random) 80-bit body —
/// Deterministic discipline: same input, same output, forever.
/// </summary>
internal static class SystemActors
{
    private static readonly DateTimeOffset FixedTimestamp = DateTimeOffset.UnixEpoch;

    public static ActorRef ForCallerModule(CallerModule caller)
    {
        var seed = (int)caller;
        var bytes = new byte[10];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)((seed + i * 31) & 0xFF);
        }
        var body = Ulid.Encode(FixedTimestamp.ToUnixTimeMilliseconds(), bytes);
        var id = OpaqueId.Parse(Ulid.WithPrefix(IdPrefixes.System, body));
        return new ActorRef(id, ActorKind.System);
    }
}
