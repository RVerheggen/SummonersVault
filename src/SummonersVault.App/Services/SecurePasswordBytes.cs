using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace SummonersVault.App.Services;

public static class SecurePasswordBytes
{
    public static byte[] From(SecureString secureString)
    {
        if (secureString.Length == 0) return [];
        var pointer = Marshal.SecureStringToGlobalAllocUnicode(secureString);
        var chars = new char[secureString.Length];
        try
        {
            for (var i = 0; i < chars.Length; i++) chars[i] = (char)Marshal.ReadInt16(pointer, i * 2);
            return Encoding.UTF8.GetBytes(chars);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(chars.AsSpan()));
            Marshal.ZeroFreeGlobalAllocUnicode(pointer);
        }
    }
}

