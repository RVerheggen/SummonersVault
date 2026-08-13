namespace SummonersVault.App;

internal static class RankIconCatalog
{
    public static Uri? GetUri(string? tier)
    {
        string? fileName = tier?.Trim().ToUpperInvariant() switch
        {
            "IRON" => "Iron.png",
            "BRONZE" => "Bronze.png",
            "SILVER" => "Silver.png",
            "GOLD" => "Gold.png",
            "PLATINUM" => "Platinum.png",
            "EMERALD" => "Emerald.png",
            "DIAMOND" => "Diamond.png",
            "MASTER" => "Master.png",
            "GRANDMASTER" => "Grandmaster.png",
            "CHALLENGER" => "Challenger.png",
            _ => null
        };

        return fileName is null
            ? null
            : new Uri($"/SummonersVault.App;component/Assets/RankIcons/{fileName}", UriKind.Relative);
    }
}
