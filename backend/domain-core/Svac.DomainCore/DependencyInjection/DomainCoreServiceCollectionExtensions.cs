using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.DomainCore.Behavioral;
using Svac.DomainCore.Con;
using Svac.DomainCore.Config;
using Svac.DomainCore.Contracts.Behavioral;
using Svac.DomainCore.Contracts.Con;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.FieldEncryption;
using Svac.DomainCore.Contracts.Ledger;
using Svac.DomainCore.Contracts.Payment;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.Contracts.Quota;
using Svac.DomainCore.Contracts.Region;
using Svac.DomainCore.DevSeams;
using Svac.DomainCore.FieldEncryption;
using Svac.DomainCore.Ledger;
using Svac.DomainCore.Payment;
using Svac.DomainCore.Persistence;
using Svac.DomainCore.Policy;
using Svac.DomainCore.Purge;
using Svac.DomainCore.Quota;
using Svac.DomainCore.Region;

namespace Svac.DomainCore.DependencyInjection;

/// <summary>
/// Wires every S1 pillar into DI (SLICE_S1_CONTRACT.md §0/§1a). One call from each host's Program.cs;
/// modules never construct a pillar implementation directly, only resolve the Contracts interface.
/// </summary>
public static class DomainCoreServiceCollectionExtensions
{
    public static IServiceCollection AddDomainCore(this IServiceCollection services, string postgresConnectionString, bool devSeamsEnabled)
    {
        services.AddDbContext<CoreDbContext>(options => options.UseNpgsql(postgresConnectionString));

        // Phase-2a (PHASE_2A_SUBSTRATE.md §1): IPolicyTable becomes the boot-time UNION of every
        // registered IPolicyTableSource. CorePolicyTableSource is source #1 (domain-core's own 7 rows) —
        // with ONLY this source registered (true at S1/S2), the union is byte-identical to the old table.
        // Every feature module registers its OWN additional IPolicyTableSource; a duplicate action key
        // across sources is a boot refusal (PolicyTable's constructor throws).
        services.AddSingleton<IPolicyTableSource, CorePolicyTableSource>();
        services.AddSingleton<IPolicyTable>(sp => new PolicyTable(sp.GetServices<IPolicyTableSource>()));
        // IStaffRoleResolver default (PHASE_2A_SUBSTRATE.md §1, SLICE_S5_CONTRACT.md §1d): fail-closed —
        // a staff actor with no real resolver has no roles. The admin host (S5 build) overrides this with
        // its grant-table-backed resolver. IResourceOwnershipResolver has zero registrants at S1/S2 by
        // design (none registered here; IEnumerable<T> resolves to empty, which PolicyEngine treats as
        // "no resolver for any resource type" — the OwnedResource axis is a structural no-op until S3).
        services.AddSingleton<IStaffRoleResolver, DenyAllStaffRoleResolver>();
        services.AddScoped<IPolicyEngine, PolicyEngine>();

        services.AddScoped<Svac.DomainCore.Contracts.Streams.IEventStore, Svac.DomainCore.EventStore.PostgresEventStore>();
        services.AddScoped<IConfigRegistry, ConfigRegistry>();
        services.AddScoped<ConfigSeedLoader>();
        services.AddScoped<Svac.DomainCore.Contracts.Audit.IAuditReader, Svac.DomainCore.Audit.AuditReader>();
        services.AddScoped<IPurgeRunReader, Svac.DomainCore.Purge.PurgeRunReader>();

        services.AddScoped<ICapModifier, Svac.DomainCore.Quota.PremiumCapModifier>();
        services.AddScoped<ICapModifier, Svac.DomainCore.Quota.ReputationCapModifier>();
        services.AddScoped<ICapModifier, Svac.DomainCore.Quota.ModeCapModifier>();
        services.AddScoped<IQuotaService, QuotaService>();

        services.AddScoped<ILedger, LedgerService>();

        services.AddSingleton<IPurgeRegistry, PurgeRegistry>();
        services.AddScoped<IPurgePipeline, PurgePipeline>();

        services.AddScoped<IFieldEncryptor, AesFieldEncryptor>();
        services.AddScoped<IBehavioralStream, SubstrateBehavioralEmitter>();

        // Vendor seams (SLICE_S1_CONTRACT.md §1b/§9): DevSeams-gated fake impl OR the fail-closed prod
        // default. NEVER both registered — the arch test scans this exact branch for a violation.
        if (devSeamsEnabled)
        {
            services.AddSingleton<IPaymentService, DevSeamsPaymentService>();
            services.AddSingleton<IFieldKeyVault, DevKeyringFieldKeyVault>();
            services.AddSingleton<IRegionResolver, DevSeamsRegionResolver>();
            services.AddSingleton<IConDayResolver, DevSeamsConDayResolver>();
        }
        else
        {
            services.AddSingleton<IPaymentService, ThrowingPaymentService>();
            services.AddSingleton<IFieldKeyVault>(_ => throw new InvalidOperationException(
                "IFieldKeyVault has no real Key Vault backend wired yet (OQ-3 pending) and DevSeams is " +
                "disabled — resolving this service in a non-DevSeams environment is fail-closed by design."));
            services.AddSingleton<IRegionResolver, ThrowingRegionResolver>();
            services.AddSingleton<IConDayResolver, ThrowingConDayResolver>();
        }

        services.AddHostedService<MigrationHostedService>();

        return services;
    }
}
