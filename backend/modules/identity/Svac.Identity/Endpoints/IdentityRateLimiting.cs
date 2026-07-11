using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Svac.Identity.Endpoints;

/// <summary>
/// Host-level per-IP fixed-window rate limiting for identity's anonymous mutation endpoints
/// (SLICE_S3_CONTRACT.md §1c: "Anonymous mutation endpoints additionally sit behind a host-level per-IP
/// fixed-window rate limiter (vanilla ASP.NET RateLimiter — transport abuse control, NOT 10A)"). Every
/// consumer-product quota (email-send flooding, etc.) still goes through 10A's IQuotaService — this is
/// purely the transport-layer abuse brake, stated so nobody retrofits it as a second product quota system.
/// </summary>
public static class IdentityRateLimiting
{
    public const string AnonymousMutationPolicy = "identity-anon";

    public static IServiceCollection AddIdentityRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(AnonymousMutationPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromMinutes(1),
                        // 100/min/IP across every anonymous signup/auth mutation combined: generous enough
                        // that a shared-NAT office or a CI E2E run's own traffic never self-trips it, tight
                        // enough that a real credential-stuffing/signup-flood burst still hits the ceiling
                        // fast. Per-mailbox flooding is separately capped by 10A's identity.email.send.daily
                        // quota (§5) — this is purely the transport-layer brake.
                        PermitLimit = 100,
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));
        });
}
