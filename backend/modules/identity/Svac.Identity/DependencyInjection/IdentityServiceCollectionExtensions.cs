using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.DomainCore.Contracts.Consent;
using Svac.DomainCore.Contracts.Email;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Hosting;
using Svac.Identity.Auth;
using Svac.Identity.Consent;
using Svac.Identity.Contracts;
using Svac.Identity.Email;
using Svac.Identity.Endpoints;
using Svac.Identity.Persistence;
using Svac.Identity.Policy;

namespace Svac.Identity.DependencyInjection;

/// <summary>
/// Wires the identity module into DI (SLICE_S3_CONTRACT.md §0/§1a/§3/§1b) — BUILD phase (Pass 1). Beyond
/// the Phase-1 scaffold's stub-only registration: IdentityDbContext (schema `identity`) + its migration
/// hosted service, the identity <see cref="IPolicyTableSource"/> + its two <see
/// cref="IResourceOwnershipResolver"/> registrations, the session-backed <see cref="IBearerAuthenticator"/>
/// (overriding <c>AddSvacHosting</c>'s anonymous default — call this AFTER <c>AddSvacHosting</c>/<c>
/// AddDomainCore</c> in the host's Program.cs, exactly like today), the real <see cref="IEmailSender"/>,
/// and <see cref="IConsentLedgerWriter"/> + its two rebuildable projections.
/// </summary>
public static class IdentityServiceCollectionExtensions
{
    /// <param name="smtpOptions">
    /// Null means "no real SMTP relay configured" — <see cref="IEmailSender"/> resolves to a fail-closed
    /// throw (SLICE_S3_CONTRACT.md §1b, L18: "Prod boot with no configured SMTP throws at startup",
    /// modeled as an unresolvable typed dependency per the IPaymentService/S2 TRUST-BREAK-3 precedent —
    /// never a factory that silently no-ops at send time). Dev/compose passes
    /// <see cref="SmtpTransportOptions.MailpitDefault"/>. Transport selection is the CALLER's
    /// environment/config decision, never a 9A entry (§1b/§9).
    /// </param>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, string postgresConnectionString, SmtpTransportOptions? smtpOptions = null)
    {
        services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(postgresConnectionString));
        services.AddHostedService<IdentityMigrationHostedService>();

        // Phase-2a union (PHASE_2A_SUBSTRATE.md §1): identity's OWN IPolicyTableSource, unioned with
        // CorePolicyTableSource (and AimlRouter's, if registered) at boot — never edits another source's
        // rows. The two ownership resolvers are the OwnedResource axis's real registrants (S1/S2 had zero).
        services.AddSingleton<IPolicyTableSource, IdentityPolicyTableSource>();
        services.AddScoped<IResourceOwnershipResolver, SessionOwnershipResolver>();
        services.AddScoped<IResourceOwnershipResolver, DeviceOwnershipResolver>();

        // Overrides AddSvacHosting's AnonymousBearerAuthenticator default — last registration wins for
        // GetRequiredService<T>() (non-enumerable) resolution; call AddIdentityModule AFTER AddSvacHosting.
        services.AddScoped<IBearerAuthenticator, SessionBearerAuthenticator>();

        services.AddSingleton<IEmailTemplateRenderer, IdentityEmailTemplateRenderer>();
        if (smtpOptions is not null)
        {
            services.AddSingleton(smtpOptions);
            services.AddScoped<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddScoped<IEmailSender>(_ => throw new InvalidOperationException(
                "IEmailSender has no real SMTP transport configured — resolving this without SmtpTransportOptions is fail-closed by design (SLICE_S3_CONTRACT.md §1b, L18)."));
        }

        services.AddScoped<ConsentCurrentProjection>();
        services.AddScoped<PushCategoryConsentProjection>();
        services.AddScoped<IConsentLedgerWriter, IdentityConsentLedgerWriter>();

        services.AddScoped<IAccountLifecycle, AccountLifecycleStub>();
        services.AddScoped<IAccountDirectory, AccountDirectory>();

        // SLICE_S3_CONTRACT.md §1c BUILD (signup/* + auth/* + minimal GET /v1/me): the endpoint-facing
        // services + the host-level per-IP transport rate limiter (never a 10A entry, §1c).
        services.AddScoped<EmailChallengeMachine>();
        services.AddScoped<SignupCompletionService>();
        services.AddScoped<RefreshRotationService>();
        // SLICE_S3_CONTRACT.md §1c Pass 2 (this build): the /v1/me/* account-management endpoint services.
        services.AddScoped<HandleChangeService>();
        services.AddIdentityRateLimiting();

        return services;
    }
}
