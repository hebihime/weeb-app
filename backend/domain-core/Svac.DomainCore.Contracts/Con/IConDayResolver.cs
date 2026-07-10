namespace Svac.DomainCore.Contracts.Con;

/// <summary>
/// Seam-now con-timezone/cutoff resolver (SLICE_S1_CONTRACT.md §1b, §5, §9). Con-local resolution takes
/// (tz, cutoff) as parameters; S8 supplies real con values from the con registry. No fake con data ever
/// — DevSeams supplies an explicit, clearly-labeled placeholder, not a plausible-looking fake.
/// </summary>
public interface IConDayResolver
{
    public Task<(TimeZoneInfo TimeZone, TimeOnly Cutoff)> ResolveForCon(string conId, CancellationToken ct = default);
}
