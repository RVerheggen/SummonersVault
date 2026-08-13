using System.Security.Cryptography;

namespace SummonersVault.Application.Security;

public sealed class SensitiveBuffer : IDisposable
{
    private byte[]? _bytes;

    public SensitiveBuffer(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        _bytes = bytes;
    }

    public ReadOnlyMemory<byte> Memory => _bytes ?? throw new ObjectDisposedException(nameof(SensitiveBuffer));

    public byte[] Copy()
    {
        ObjectDisposedException.ThrowIf(_bytes is null, this);
        return [.. _bytes];
    }

    public void Dispose()
    {
        if (_bytes is null)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_bytes);
        _bytes = null;
    }
}
