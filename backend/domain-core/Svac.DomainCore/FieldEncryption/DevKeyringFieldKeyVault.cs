using System.Security.Cryptography;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.FieldEncryption;

namespace Svac.DomainCore.FieldEncryption;

/// <summary>
/// DevSeams local dev keyring (SLICE_S1_CONTRACT.md §1b, §9): an obviously-fake in-memory master-key
/// store, never DI-registered in prod (arch-tested). Master keys are derived deterministically from a
/// fixed, clearly-labeled dev seed so restarts within the same process stay usable; DestroyKey removes
/// the master key so a subsequent UnwrapKey throws — the crypto-shred contract holds even in dev.
///
/// PII-8 (SECURITY_REVIEW_S3.md): because master keys are RE-DERIVABLE from the fixed dev seed (needed
/// so a restart within the same process stays usable without a persisted key store), <c>_destroyed</c>
/// being purely in-memory meant a process restart resurrected every "shredded" key — every deployed
/// instance of the crypto-shred contract (the only one that actually ships) silently stopped holding
/// once the process cycled. <paramref name="destroyedKeysStorePath"/> is an OPT-IN file-backed persistence
/// seam: when supplied (or the <see cref="DestroyedKeysPathEnvironmentVariable"/> env var is set), a
/// destroyed key name is appended to that file and reloaded by every new instance pointed at the SAME
/// path — "restart" (a fresh instance over the same store) can never resurrect a shredded key. Left
/// unset (the parameterless constructor every existing call site + test fixture uses), behavior is
/// BYTE-IDENTICAL to before this fix — purely in-memory, isolated per instance — so this stays a
/// zero-risk addition for the ~15 existing direct constructions across the test suite. The real DI
/// registration (<see cref="Svac.DomainCore.DependencyInjection.DomainCoreServiceCollectionExtensions.AddDomainCore"/>)
/// is the one caller that opts in, so an actual dev/compose host restart is protected for real.
/// </summary>
[DevSeamsOnly]
public sealed class DevKeyringFieldKeyVault : IFieldKeyVault
{
    /// <summary>Env var carrying the persisted-destroyed-keys file path (PII-8); unset means pure in-memory, matching pre-fix behavior.</summary>
    public const string DestroyedKeysPathEnvironmentVariable = "SVAC_DEVSEAMS_DESTROYED_KEYS_PATH";

    // NEVER a real secret: a fixed, obviously-labeled dev-only seed. Production resolution of
    // IFieldKeyVault never reaches this type (arch-tested); ProdFieldKeyVaultGuard throws first.
    private static readonly byte[] DevSeed = "svac-dev-keyring-NOT-FOR-PRODUCTION-USE"u8.ToArray();

    private readonly Dictionary<string, byte[]> _liveMasterKeys = new();
    private readonly HashSet<string> _destroyed = new();
    private readonly Lock _lock = new();
    private readonly string? _destroyedKeysStorePath;

    public DevKeyringFieldKeyVault() : this(destroyedKeysStorePath: null)
    {
    }

    /// <param name="destroyedKeysStorePath">
    /// Optional file path backing the destroyed-key-name set (PII-8). Null falls back to the
    /// <see cref="DestroyedKeysPathEnvironmentVariable"/> env var; if THAT is also unset, this instance
    /// is pure in-memory (byte-identical to the pre-fix type — no cross-instance persistence, no shared
    /// file, zero risk of concurrent-test file contention).
    /// </param>
    public DevKeyringFieldKeyVault(string? destroyedKeysStorePath)
    {
        _destroyedKeysStorePath = destroyedKeysStorePath ?? Environment.GetEnvironmentVariable(DestroyedKeysPathEnvironmentVariable);
        if (_destroyedKeysStorePath is not null && File.Exists(_destroyedKeysStorePath))
        {
            foreach (var line in File.ReadAllLines(_destroyedKeysStorePath))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    _destroyed.Add(line);
                }
            }
        }
    }

    public Task<byte[]> WrapKey(string keyName, byte[] rawKey, CancellationToken ct = default)
    {
        var masterKey = GetOrDeriveMasterKey(keyName);
        using var aes = new AesGcm(masterKey, AesGcm.TagByteSizes.MaxSize);
        var nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[rawKey.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        aes.Encrypt(nonce, rawKey, ciphertext, tag);

        var wrapped = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, wrapped, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, wrapped, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, wrapped, nonce.Length + tag.Length, ciphertext.Length);
        return Task.FromResult(wrapped);
    }

    public Task<byte[]> UnwrapKey(string keyName, byte[] wrappedKey, CancellationToken ct = default)
    {
        var masterKey = GetOrDeriveMasterKey(keyName);
        var nonceLen = AesGcm.NonceByteSizes.MaxSize;
        var tagLen = AesGcm.TagByteSizes.MaxSize;
        var nonce = wrappedKey[..nonceLen];
        var tag = wrappedKey[nonceLen..(nonceLen + tagLen)];
        var ciphertext = wrappedKey[(nonceLen + tagLen)..];

        using var aes = new AesGcm(masterKey, AesGcm.TagByteSizes.MaxSize);
        var plaintext = new byte[ciphertext.Length];
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Task.FromResult(plaintext);
    }

    public Task DestroyKey(string keyName, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _liveMasterKeys.Remove(keyName);
            _destroyed.Add(keyName);
            if (_destroyedKeysStorePath is not null)
            {
                var dir = Path.GetDirectoryName(_destroyedKeysStorePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                // Append-only, one key name per line: a durable record that survives THIS instance's
                // death — the next DevKeyringFieldKeyVault constructed over the same path reloads it
                // before a single UnwrapKey call can re-derive (and thus resurrect) the "destroyed" key.
                File.AppendAllLines(_destroyedKeysStorePath, new[] { keyName });
            }
        }
        return Task.CompletedTask;
    }

    public Task<byte[]> GetNamedSecret(string keyName, CancellationToken ct = default) =>
        Task.FromResult(GetOrDeriveMasterKey(keyName));

    private byte[] GetOrDeriveMasterKey(string keyName)
    {
        lock (_lock)
        {
            if (_destroyed.Contains(keyName))
            {
                throw new InvalidOperationException($"dev keyring key \"{keyName}\" was destroyed (crypto-shredded) — it can never be recovered.");
            }
            if (_liveMasterKeys.TryGetValue(keyName, out var existing))
            {
                return existing;
            }
            var derived = HKDF.DeriveKey(HashAlgorithmName.SHA256, DevSeed, 32, salt: System.Text.Encoding.UTF8.GetBytes(keyName));
            _liveMasterKeys[keyName] = derived;
            return derived;
        }
    }
}
