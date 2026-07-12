using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Svac.Identity.Endpoints;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// OPS-1 (SECURITY_REVIEW_S3.md, HIGH): behind Azure Container Apps' ingress, every anonymous request's
/// transport-level source address is the ingress proxy, so <see cref="IdentityRateLimiting"/>'s per-IP
/// partition key (<c>httpContext.Connection.RemoteIpAddress</c>) collapsed the whole anonymous funnel into
/// ONE bucket. The fix is <c>app.UseForwardedHeaders(...)</c> configured via
/// <see cref="ForwardedHeadersConfiguration.Build"/> — trusting ONLY a configured CIDR set, never every
/// peer (which would let a client spoof X-Forwarded-For to pick its own partition).
///
/// No DB/Testcontainer needed — a minimal real Kestrel host on loopback with one probe endpoint that
/// echoes the resolved <c>Connection.RemoteIpAddress</c>, which is EXACTLY what the rate limiter partitions
/// on (IdentityRateLimiting.cs). Proving the resolved address differs per distinct X-Forwarded-For value
/// under a trusted hop — and stays put under an untrusted/unconfigured one — is equivalent to proving the
/// limiter partitions correctly, without needing to exhaust the real 100/min bucket.
/// </summary>
public sealed class ForwardedHeadersTrustTests
{
    private static async Task<(WebApplication App, HttpClient Client)> StartProbeHost(string? trustedProxyCidrs)
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();

        app.UseForwardedHeaders(ForwardedHeadersConfiguration.Build(trustedProxyCidrs));
        app.MapGet("/probe", (HttpContext ctx) => Results.Text(ctx.Connection.RemoteIpAddress?.ToString() ?? "null"));

        await app.StartAsync();
        var addressFeature = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("no server address feature available.");
        var client = new HttpClient { BaseAddress = new Uri(addressFeature.Addresses.First()) };
        return (app, client);
    }

    [Fact]
    public async Task TrustedHop_TwoDifferentForwardedForClients_ResolveToTwoDifferentAddresses()
    {
        // The test client itself connects from loopback — trust exactly that hop.
        var (app, client) = await StartProbeHost("127.0.0.1/32");
        try
        {
            var first = await SendWithForwardedFor(client, "203.0.113.7");
            var second = await SendWithForwardedFor(client, "203.0.113.99");

            Assert.Equal("203.0.113.7", first);
            Assert.Equal("203.0.113.99", second);
            Assert.NotEqual(first, second); // two different rate-limiter partitions, per real client.
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task UntrustedHop_NoCidrsConfigured_ForwardedForIsIgnored_AddressStaysTheRawPeer()
    {
        // Nothing configured (SVAC_ACA_INGRESS_CIDRS unset) — the safe fail-closed default: no hop is
        // trusted, so a spoofed X-Forwarded-For must NOT repartition the limiter.
        var (app, client) = await StartProbeHost(trustedProxyCidrs: null);
        try
        {
            var resolved = await SendWithForwardedFor(client, "203.0.113.7");
            Assert.Equal("127.0.0.1", resolved); // XFF ignored; the literal transport-connection address wins.
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task UntrustedHop_DifferentCidrConfigured_ForwardedForIsIgnored_CannotSpoofIntoTrust()
    {
        // A CIDR IS configured, but it names a network the test client's loopback peer address is NOT
        // a member of — proves the trust check is a real membership test, not "any CIDR configured = trust
        // everyone."
        var (app, client) = await StartProbeHost("10.0.0.0/16");
        try
        {
            var resolved = await SendWithForwardedFor(client, "203.0.113.7");
            Assert.Equal("127.0.0.1", resolved);
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
        }
    }

    private static async Task<string> SendWithForwardedFor(HttpClient client, string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/probe");
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        var response = await client.SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }
}
