using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;

namespace SummonersVault.Infrastructure.Security;

public static class VaultKeyEnvelope
{
    public const int MinimumMasterPasswordLength = 8;
    public const long MemoryBytes = 64L * 1024 * 1024;
    public const long Passes = 3;
    public const int Parallelism = 1;

    public static VaultMetadata Create(Guid vaultId, ReadOnlySpan<byte> masterPasswordUtf8, ReadOnlySpan<byte> databaseKey)
    {
        ValidateMasterPassword(masterPasswordUtf8);
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] wrapKey = Derive(masterPasswordUtf8, salt, MemoryBytes, Passes, Parallelism);
        byte[] ciphertext = new byte[databaseKey.Length];
        byte[] tag = new byte[16];
        try
        {
            using var aes = new AesGcm(wrapKey, tag.Length);
            aes.Encrypt(nonce, databaseKey, ciphertext, tag, AssociatedData(vaultId));
            return new VaultMetadata(
                1,
                vaultId,
                new ArgonMetadata("argon2id", MemoryBytes, Passes, Parallelism, Convert.ToBase64String(salt)),
                new KeyEnvelopeMetadata("aes-256-gcm", Convert.ToBase64String(nonce), Convert.ToBase64String(ciphertext), Convert.ToBase64String(tag)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrapKey);
            CryptographicOperations.ZeroMemory(salt);
        }
    }

    public static bool TryUnwrap(VaultMetadata metadata, ReadOnlySpan<byte> masterPasswordUtf8, out byte[] databaseKey)
    {
        databaseKey = [];
        if (metadata.FormatVersion != 1 || metadata.Kdf.Algorithm != "argon2id" || metadata.KeyEnvelope.Algorithm != "aes-256-gcm"
            || metadata.Kdf.MemoryBytes != MemoryBytes || metadata.Kdf.Passes != Passes || metadata.Kdf.Parallelism != Parallelism)
        {
            return false;
        }

        byte[] salt;
        byte[] nonce;
        byte[] ciphertext;
        byte[] tag;
        try
        {
            salt = Convert.FromBase64String(metadata.Kdf.SaltBase64);
            nonce = Convert.FromBase64String(metadata.KeyEnvelope.NonceBase64);
            ciphertext = Convert.FromBase64String(metadata.KeyEnvelope.CiphertextBase64);
            tag = Convert.FromBase64String(metadata.KeyEnvelope.TagBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] wrapKey = Derive(masterPasswordUtf8, salt, metadata.Kdf.MemoryBytes, metadata.Kdf.Passes, metadata.Kdf.Parallelism);
        byte[] candidate = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(wrapKey, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, candidate, AssociatedData(metadata.VaultId));
            databaseKey = candidate;
            return true;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(candidate);
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrapKey);
            CryptographicOperations.ZeroMemory(salt);
        }
    }

    public static void ValidateMasterPassword(ReadOnlySpan<byte> passwordUtf8)
    {
        if (Encoding.UTF8.GetCharCount(passwordUtf8) < MinimumMasterPasswordLength)
        {
            throw new ArgumentException($"Master password must contain at least {MinimumMasterPasswordLength} characters.", nameof(passwordUtf8));
        }
    }

    private static byte[] Derive(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, long memoryBytes, long passes, int parallelism)
    {
        var parameters = new Argon2Parameters
        {
            // NSec/libsodium expresses the Argon2 memory cost in KiB.
            MemorySize = memoryBytes / 1024,
            NumberOfPasses = passes,
            DegreeOfParallelism = parallelism
        };
        Argon2id algorithm = PasswordBasedKeyDerivationAlgorithm.Argon2id(in parameters);
        return algorithm.DeriveBytes(password, salt, 32);
    }

    private static byte[] AssociatedData(Guid vaultId) => Encoding.UTF8.GetBytes($"SummonersVault:v1:{vaultId:D}");
}
