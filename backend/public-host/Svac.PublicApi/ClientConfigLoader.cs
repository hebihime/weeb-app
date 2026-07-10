using System.Text.Json;
using Svac.DomainCore.Contracts.Api;

namespace Svac.PublicApi;

/// <summary>
/// Loads GET /v1/client-config's response from i18n/locales.json at boot (SLICE_S1_CONTRACT.md §1c). No
/// 9A mirror of locales is created — one truth stays the file (§1c/§12.12).
/// </summary>
public static class ClientConfigLoader
{
    public static ClientConfigResponse Load(string localesPath)
    {
        var json = File.ReadAllText(localesPath);
        var doc = JsonSerializer.Deserialize<LocalesFile>(json)
            ?? throw new InvalidOperationException($"\"{localesPath}\" deserialized to null.");
        if (doc.Locales is null or { Length: 0 } || string.IsNullOrWhiteSpace(doc.Default))
        {
            throw new InvalidOperationException(
                $"\"{localesPath}\" deserialized with an empty locales list or default locale — check the file's " +
                "\"locales\"/\"default\" keys are present and spelled correctly, since a malformed shape " +
                "deserializes those fields to null/empty rather than throwing.");
        }
        return new ClientConfigResponse(ApiVersion: "v0", Locales: doc.Locales, DefaultLocale: doc.Default);
    }

    /// <summary>
    /// Dev/CI: repo-relative (backend/public-host/Svac.PublicApi -&gt; ../../../i18n/locales.json).
    /// Container: the Dockerfile COPYs i18n/ to alongside the published app.
    /// </summary>
    public static string ResolveDefaultLocalesPath(string contentRootPath)
    {
        var containerPath = Path.Combine(contentRootPath, "i18n", "locales.json");
        if (File.Exists(containerPath))
        {
            return containerPath;
        }
        return Path.Combine(contentRootPath, "..", "..", "..", "i18n", "locales.json");
    }
}
