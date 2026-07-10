using System.Security.Cryptography;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.FieldEncryption;

namespace Svac.DomainCore.FieldEncryption;

/// <summary>
/// DevSeams local dev keyring (SLICE_S1_CONTRACT.md §1b, §9): an obviously-fake in-memory master-key
/// store, never DI-registered in prod (arch-tested). Master keys are derived deterministically from a
/// fixed, clearly-labeled dev seed so restarts within the same process stay usable; DestroyKey removes
/// the master key so a subsequent UnwrapKey throws — the crypto-shred contract holds even in dev.
/// </summary>
[DevSeamsOnly]
public sealed class DevKeyringFieldKeyVault : IFieldKeyVault
{
    // NEVER a real secret: a fixed, obviously-labeled dev-only seed. Production resolution of
    // IFieldKeyVault never reaches this type (arch-tested); ProdFieldKeyVaultGuard throws first.
    private static readonly byte[] DevSeed = "svac-dev-keyring-NOT-FOR-PRODUCTION-USE"u8.ToArray();

    private readonly Dictionary<string, byte[]> _liveMasterKeys = new();
    private readonly HashSet<string> _destroyed = new();
    private readonly Lock _lock = new();

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
