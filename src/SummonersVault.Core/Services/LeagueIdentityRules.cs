using SummonersVault.Core.Models;

namespace SummonersVault.Core.Services;

public static class LeagueIdentityRules
{
    public static bool MatchesLinkedAccount(VaultAccount account, string signedInPuuid) =>
        string.IsNullOrWhiteSpace(account.Puuid)
        || string.Equals(account.Puuid, signedInPuuid, StringComparison.Ordinal);
}
