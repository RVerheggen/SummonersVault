using SummonersVault.Application.Abstractions;
using SummonersVault.Application.Accounts;
using SummonersVault.Core.Models;

namespace SummonersVault.Infrastructure.Backup;

internal static class BackupConflictResolver
{
    public static IReadOnlyList<BackupConflict> FindConflicts(
        IReadOnlyList<AccountImportItem> importedAccounts,
        IReadOnlyList<VaultAccount> currentAccounts)
    {
        var conflicts = new List<BackupConflict>();
        foreach (AccountImportItem importedItem in importedAccounts)
        {
            VaultAccount? current = FindConflict(importedItem.Account, currentAccounts);
            if (current is not null)
            {
                conflicts.Add(new(importedItem.Account.Id, current.Id, importedItem.Account.DisplayName));
            }
        }

        return conflicts;
    }

    public static IReadOnlyList<AccountImportItem> SelectAccountsToMerge(
        BackupImportPreview preview,
        IReadOnlyDictionary<Guid, BackupConflictChoice> choices,
        IEnumerable<Guid> currentAccountIds)
    {
        var conflicts = preview.Conflicts.ToDictionary(conflict => conflict.ImportedId);
        var existingIds = currentAccountIds.ToHashSet();
        var result = new List<AccountImportItem>();

        foreach (AccountImportItem importedItem in preview.NewAccounts)
        {
            VaultAccount imported = importedItem.Account;
            if (conflicts.TryGetValue(imported.Id, out BackupConflict? conflict))
            {
                if (choices.GetValueOrDefault(imported.Id, BackupConflictChoice.KeepCurrent) == BackupConflictChoice.KeepCurrent)
                {
                    continue;
                }

                imported.Id = conflict.CurrentId;
            }
            else if (existingIds.Contains(imported.Id))
            {
                imported.Id = Guid.NewGuid();
            }

            result.Add(importedItem);
            existingIds.Add(imported.Id);
        }

        return result;
    }

    private static VaultAccount? FindConflict(VaultAccount imported, IReadOnlyList<VaultAccount> currentAccounts)
    {
        if (!string.IsNullOrWhiteSpace(imported.Puuid))
        {
            VaultAccount? puuidMatch = currentAccounts.FirstOrDefault(current =>
                string.Equals(current.Puuid, imported.Puuid, StringComparison.OrdinalIgnoreCase));
            if (puuidMatch is not null)
            {
                return puuidMatch;
            }
        }

        string importedRegion = LeagueRegion.Normalize(imported.Region);
        return currentAccounts.FirstOrDefault(current =>
            string.Equals(current.Username.Trim(), imported.Username.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(LeagueRegion.Normalize(current.Region), importedRegion, StringComparison.OrdinalIgnoreCase));
    }
}
