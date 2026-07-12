namespace Svac.AdminHost.Auth;

/// <summary>A typed DI marker for "is DevSeams on for THIS boot" (never a 9A entry, §1b/§12.16) — lets
/// Razor pages (e.g. SignIn.razor) render the DevSeams fixture list without reading an environment
/// variable directly from markup code.</summary>
public sealed record DevSeamsEnabledFlag(bool Value);
