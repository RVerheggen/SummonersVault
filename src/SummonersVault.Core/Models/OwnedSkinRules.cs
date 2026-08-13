namespace SummonersVault.Core.Models;

public static class OwnedSkinRules
{
    private const int AlternateChampionIdOffset = 60_000;
    private const int AlternateSkinIdOffset = 60_000_000;

    public static OwnedSkin Canonicalize(OwnedSkin skin)
    {
        if (skin.ChampionId is >= AlternateChampionIdOffset and < 70_000
            && skin.SkinId >= AlternateSkinIdOffset)
        {
            int championId = skin.ChampionId - AlternateChampionIdOffset;
            int skinId = skin.SkinId - AlternateSkinIdOffset;
            if (championId > 0 && skinId > 0 && skinId / 1000 == championId)
            {
                return skin with { SkinId = skinId, ChampionId = championId };
            }
        }

        return skin;
    }

    public static IReadOnlyList<OwnedSkin> Normalize(IEnumerable<OwnedSkin> skins) => [.. skins
        .Select(Canonicalize)
        .Where(IsCountedCanonical)
        .GroupBy(skin => skin.SkinId)
        .Select(group => group.First())];

    public static bool IsCounted(OwnedSkin skin)
    {
        return IsCountedCanonical(Canonicalize(skin));
    }

    private static bool IsCountedCanonical(OwnedSkin skin)
    {
        if (skin.SkinId <= 0 || skin.ChampionId <= 0)
        {
            return false;
        }

        if (skin.SkinId == skin.ChampionId * 1000)
        {
            return false;
        }

        string name = skin.Name.Trim();
        return !IsBaseName(name, "Classic")
            && !IsBaseName(name, "Original")
            && !IsBaseName(name, "Default");
    }

    private static bool IsBaseName(string name, string marker) =>
        name.Equals(marker, StringComparison.OrdinalIgnoreCase)
        || name.StartsWith($"{marker} ", StringComparison.OrdinalIgnoreCase);
}
