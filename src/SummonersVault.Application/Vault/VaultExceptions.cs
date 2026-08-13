namespace SummonersVault.Application.Vault;

public sealed class UnsupportedVaultException(string message) : Exception(message);

public sealed class VaultUpgradeException : Exception
{
    public VaultUpgradeException(string message)
        : base(message)
    {
    }

    public VaultUpgradeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
