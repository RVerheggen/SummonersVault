using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Design;

namespace SummonersVault.Infrastructure.Persistence;

internal sealed class DesignTimeVaultDbContextFactory : IDesignTimeDbContextFactory<VaultDbContext>
{
    public VaultDbContext CreateDbContext(string[] args)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        return new VaultDbContextFactory(connection).Create();
    }
}
