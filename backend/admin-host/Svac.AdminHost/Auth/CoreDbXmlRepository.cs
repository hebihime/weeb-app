using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Persistence;

namespace Svac.AdminHost.Auth;

/// <summary>
/// Persists Data Protection keys to the existing schema-`core` <c>data_protection_keys</c> table
/// (SLICE_S5_CONTRACT.md Pass A deliverable: "PERSIST DataProtection keys to the existing core
/// DataProtectionKeys EF store ... so cookies/antiforgery survive restart + multiple instances — the
/// scaffold logged the not-persisted warning; fix it here").
///
/// A hand-rolled <see cref="IXmlRepository"/> rather than the <c>Microsoft.AspNetCore.DataProtection.
/// EntityFrameworkCore</c> package + its own <c>PersistKeysToDbContext&lt;T&gt;</c>: that package expects
/// its OWN <c>DataProtectionKey</c> entity mapped onto the context, but <c>core.data_protection_keys</c> is
/// deliberately hand-owned instead (<c>DataProtectionKeyEntity</c>'s own doc comment: "owned directly
/// rather than pulled in via the ... package so Svac.DomainCore.csproj's dependency surface stays exactly
/// what §1a lists") — this repository reads/writes that SAME table directly via <c>CoreDbContext</c>,
/// adding zero new package dependency to either project (<c>IXmlRepository</c> itself ships in the
/// ASP.NET Core shared framework Svac.AdminHost already references via <c>Sdk="Microsoft.NET.Sdk.Web"</c>).
///
/// Creates its OWN <see cref="CoreDbContext"/> per call (never captures a Scoped context across the
/// Data Protection key-ring's own singleton-lifetime cache) — mirrors the pattern every other
/// short-lived-DbContext-from-a-long-lived-singleton call site in this repo uses (e.g.
/// <c>AdminMigrationHostedService</c>).
/// </summary>
public sealed class CoreDbXmlRepository(string postgresConnectionString) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var db = NewDb();
        return db.DataProtectionKeys
            .Select(k => k.Xml)
            .ToList()
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .Select(xml => XElement.Parse(xml!))
            .ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var db = NewDb();
        db.DataProtectionKeys.Add(new Svac.DomainCore.Persistence.DataProtectionKeyEntity
        {
            FriendlyName = friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting),
        });
        db.SaveChanges();
    }

    private CoreDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(postgresConnectionString).Options);
}
