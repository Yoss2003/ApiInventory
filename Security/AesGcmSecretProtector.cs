using System.Security.Cryptography;
using System.Text;

namespace InventoryAPI.Security;

public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const string Prefix = "enc:v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public AesGcmSecretProtector(string masterKey)
    {
        if (string.IsNullOrWhiteSpace(masterKey))
            throw new InvalidOperationException("Security:EncryptionKey no está configurada.");

        _key = SHA256.HashData(Encoding.UTF8.GetBytes(masterKey));
    }

    public bool IsProtected(string? storedValue) =>
        !string.IsNullOrEmpty(storedValue) && storedValue.StartsWith(Prefix, StringComparison.Ordinal);

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;
        if (IsProtected(plaintext)) return plaintext;

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var payload = new byte[NonceSize + TagSize + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(cipherBytes, 0, payload, NonceSize + TagSize, cipherBytes.Length);

        return Prefix + Convert.ToBase64String(payload);
    }

    public string Unprotect(string storedValue)
    {
        if (string.IsNullOrEmpty(storedValue)) return string.Empty;
        if (!IsProtected(storedValue)) return storedValue;

        var payload = Convert.FromBase64String(storedValue[Prefix.Length..]);
        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipherBytes = payload.AsSpan(NonceSize + TagSize);
        var plainBytes = new byte[cipherBytes.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }
}