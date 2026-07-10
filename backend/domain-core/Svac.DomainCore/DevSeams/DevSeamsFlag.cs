namespace Svac.DomainCore.DevSeams;

/// <summary>
/// DevSeams is an environment/deployment flag, NEVER a 9A entry (SLICE_S1_CONTRACT.md §1b, §12.16) — a
/// runtime-tunable that swaps fake payment/crypto/region/con backends in from the ops desk must be
/// structurally impossible. Read once at startup from the environment, never from the config registry.
/// Fails toward OFF: anything other than an explicit "true" leaves DevSeams disabled, including an
/// unset variable — a missing setting must never accidentally enable fake production backends.
/// </summary>
public static class DevSeamsFlag
{
    public const string EnvironmentVariableName = "SVAC_DEVSEAMS_ENABLED";

    public static bool IsEnabled(IReadOnlyDictionary<string, string?> environment) =>
        environment.TryGetValue(EnvironmentVariableName, out var value) && bool.TryParse(value, out var parsed) && parsed;

    public static bool IsEnabledFromProcessEnvironment() =>
        bool.TryParse(Environment.GetEnvironmentVariable(EnvironmentVariableName), out var parsed) && parsed;
}
