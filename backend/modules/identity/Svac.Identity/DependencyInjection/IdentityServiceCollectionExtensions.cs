using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.DomainCore.Contracts.Consent;
using Svac.DomainCore.Contracts.Email;
using Svac.DomainCore.Contracts.Export;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.Hosting;
using Svac.DomainCore.Purge;
using Svac.Identity.Auth;
using Svac.Identity.Consent;
using Svac.Identity.Contracts;
using Svac.Identity.Deletion;
using Svac.Identity.Email;
using Svac.Identity.Endpoints;
using Svac.Identity.Export;
using Svac.Identity.Persistence;
using Svac.Identity.Policy;
using Svac.Identity.Purge;

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
    /// Null means "no real SMTP relay configured" — <see cref="IEmailTransport"/> (the outbox dispatcher's
    /// downstream, SECURITY_REVIEW_S3.md MAIL-1/OPS-2) resolves to a fail-closed throw as a defense-in-depth
    /// backstop; the REAL fail-closed guarantee is <see cref="SmtpConfiguredGuard.Enforce"/>, called
    /// explicitly in Program.cs before the host serves traffic (never a lazy factory discovered at first
    /// send). Dev/compose passes <see cref="SmtpTransportOptions.MailpitDefault"/>. Transport selection is
    /// the CALLER's environment/config decision, never a 9A entry (§1b/§9). <see cref="IEmailSender"/>
    /// itself (the request-path-facing seam every endpoint/service actually injects) ALWAYS resolves —
    /// to <see cref="OutboxEmailSender"/>, which only enqueues — regardless of whether SMTP is configured;
    /// dispatch failure surfaces in <see cref="EmailOutboxDispatcher"/>'s log, off the request thread.
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
        services.AddScoped<IResourceOwnershipResolver, ExportOwnershipResolver>();

        // Overrides AddSvacHosting's AnonymousBearerAuthenticator default — last registration wins for
        // GetRequiredService<T>() (non-enumerable) resolution; call AddIdentityModule AFTER AddSvacHosting.
        services.AddScoped<IBearerAuthenticator, SessionBearerAuthenticator>();

        services.AddSingleton<IEmailTemplateRenderer, IdentityEmailTemplateRenderer>();

        // SECURITY_REVIEW_S3.md MAIL-1/OPS-2: the request path ALWAYS resolves IEmailSender to the
        // enqueue-only OutboxEmailSender — never the real transport directly, and never a fail-closed
        // throw, since a null-SMTP boot never reaches this line in a non-Development environment
        // (SmtpConfiguredGuard.Enforce, called explicitly in Program.cs, refuses to boot first). The real
        // transport (IEmailTransport) is what the outbox dispatcher drains into, off the request thread.
        services.AddSingleton<EmailOutbox>();
        services.AddScoped<IEmailSender, OutboxEmailSender>();
        services.AddHostedService<EmailOutboxDispatcher>();

        if (smtpOptions is not null)
        {
            services.AddSingleton(smtpOptions);
            services.AddScoped<IEmailTransport, SmtpEmailSender>();
        }
        else
        {
            // Defense-in-depth backstop only (SmtpConfiguredGuard already refused to boot in this shape
            // outside Development) — never the primary fail-closed mechanism.
            services.AddScoped<IEmailTransport>(_ => throw new InvalidOperationException(
                "IEmailTransport has no real SMTP transport configured — resolving this without SmtpTransportOptions is fail-closed by design (SECURITY_REVIEW_S3.md OPS-2, SLICE_S3_CONTRACT.md §1b, L18)."));
        }

        services.AddScoped<ConsentCurrentProjection>();
        services.AddScoped<PushCategoryConsentProjection>();
        services.AddScoped<IConsentLedgerWriter, IdentityConsentLedgerWriter>();

        services.AddScoped<IAccountLifecycle, AccountLifecycle>();
        services.AddScoped<IAccountDirectory, AccountDirectory>();

        // SLICE_S3_CONTRACT.md §2/§6a/§6c (deletion + purge build): the DELETION pipeline (Phase L via
        // AccountLifecycle above, Phase P via DeletionPhysicalPurgeWorker), identity's own additive slice
        // of the 13A registry, and the ten real IPurgeStoreExecutor registrants — every identity store
        // whose registry cell is ever something other than NotApplicable. identity.reserved_handles/
        // retired_handles/ban_evasion_refs need none (always NotApplicable; PurgePipeline never calls a
        // store's executor for that verb).
        services.AddSingleton<IPurgeRegistrySource, IdentityPurgeRegistrySource>();
        services.AddScoped<IPurgeStoreExecutor, EmailChallengesPurgeStoreExecutor>();
        services.AddScoped<IPurgeStoreExecutor, AccountsPurgeStoreExecutor>();
        services.AddScoped<IPurgeStoreExecutor, SessionsPurgeStoreExecutor>();
        services.AddScoped<IPurgeStoreExecutor, RefreshTokensPurgeStoreExecutor>();
        services.AddScoped<IPurgeStoreExecutor, DevicesPurgeStoreExecutor>();
        services.AddScoped<IPurgeStoreExecutor, PushCategoryConsentsPurgeStoreExecutor>();
        services.AddScoped<IPurgeStoreExecutor, ConsentCurrentPurgeStoreExecutor>();
        services.AddScoped<IPurgeStoreExecutor, HandleHistoryPurgeStoreExecutor>();
        services.AddScoped<IPurgeStoreExecutor, ExportJobsPurgeStoreExecutor>();
        services.AddScoped<IPurgeStoreExecutor, DeletionJobsPurgeStoreExecutor>();

        // The 13A custody-hold consult seam (ER-14, §2 Phase P step 1) — no real hold data exists before
        // S12; production always resolves the honest "no holds" registry. Svac.Tests.Identity's red
        // fixture registers its own ICustodyHoldRegistry directly against a fresh ServiceCollection to
        // prove the held/skip/release behavior without touching this composition.
        services.AddScoped<ICustodyHoldRegistry, NoCustodyHoldRegistry>();
        services.AddScoped<DeletionPhysicalPurgeWorker>();

        // SLICE_S3_CONTRACT.md §1c BUILD (signup/* + auth/* + minimal GET /v1/me): the endpoint-facing
        // services + the host-level per-IP transport rate limiter (never a 10A entry, §1c).
        services.AddScoped<EmailChallengeMachine>();
        services.AddScoped<SignupCompletionService>();
        services.AddScoped<RefreshRotationService>();
        // SLICE_S3_CONTRACT.md §1c Pass 2 (this build): the /v1/me/* account-management endpoint services.
        services.AddScoped<HandleChangeService>();
        services.AddIdentityRateLimiting();

        // SLICE_S3_CONTRACT.md §6b (export build): S3's own additive slice of the export registry —
        // unioned at boot with Svac.DomainCore.Export.CoreExportRegistrySource (registered by
        // AddDomainCore) into ONE IExportRegistry the export⋈purge cross-gate and ExportWorker both read.
        services.AddSingleton<IExportRegistrySource, IdentityExportRegistrySource>();

        // The 13 real IExportContributor registrants (SLICE_S3_CONTRACT.md §6b): every identity table
        // that holds the subject's data, PLUS the five S1 stores S3 is the first real consumer of.
        services.AddScoped<IExportContributor, AccountExportContributor>();
        services.AddScoped<IExportContributor, SessionsExportContributor>();
        services.AddScoped<IExportContributor, DevicesExportContributor>();
        services.AddScoped<IExportContributor, PushCategoryConsentsExportContributor>();
        services.AddScoped<IExportContributor, ConsentCurrentExportContributor>();
        services.AddScoped<IExportContributor, HandleHistoryExportContributor>();
        services.AddScoped<IExportContributor, ExportJobsExportContributor>();
        services.AddScoped<IExportContributor, DeletionJobsExportContributor>();
        services.AddScoped<IExportContributor, LedgerEntriesExportContributor>();
        services.AddScoped<IExportContributor, EventsLedgerExportContributor>();
        services.AddScoped<IExportContributor, EventsConsentExportContributor>();
        services.AddScoped<IExportContributor, EventsBehavioralExportContributor>();
        services.AddScoped<IExportContributor, EventsAuditExportContributor>();

        services.AddScoped<IExportArtifactStore, PostgresExportArtifactStore>();
        services.AddScoped<ExportWorker>();

        return services;
    }
}
