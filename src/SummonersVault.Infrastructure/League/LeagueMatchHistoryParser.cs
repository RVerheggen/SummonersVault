using System.Globalization;
using System.Text.Json;
using SummonersVault.Core.Models;

namespace SummonersVault.Infrastructure.League;

public static class LeagueMatchHistoryParser
{
    public static MatchSnapshotResult Parse(string json)
    {
        try { using var document = JsonDocument.Parse(json); return Parse(document.RootElement); }
        catch (JsonException) { return MatchSnapshotResult.Failed; }
    }

    public static MatchSnapshotResult Parse(JsonElement root)
    {
        if (root.TryGetProperty("games", out JsonElement wrapper) && wrapper.ValueKind == JsonValueKind.Object && wrapper.TryGetProperty("games", out JsonElement games))
        {
            root = games;
        }
        else if (root.TryGetProperty("games", out JsonElement directGames))
        {
            root = directGames;
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            return MatchSnapshotResult.Failed;
        }

        JsonElement game = root.EnumerateArray().FirstOrDefault();
        if (game.ValueKind == JsonValueKind.Undefined)
        {
            return MatchSnapshotResult.Empty;
        }

        long? matchId = GetInt64(game, "gameId");
        if (game.TryGetProperty("gameCreation", out JsonElement creation) && creation.ValueKind == JsonValueKind.Number && creation.TryGetInt64(out long milliseconds))
        {
            try { return MatchSnapshotResult.Known(DateTimeOffset.FromUnixTimeMilliseconds(milliseconds), matchId); }
            catch (ArgumentOutOfRangeException) { return MatchSnapshotResult.Failed; }
        }
        string? textual = game.TryGetProperty("gameCreationDate", out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return DateTimeOffset.TryParse(textual, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed)
            ? MatchSnapshotResult.Known(parsed, matchId)
            : MatchSnapshotResult.Failed;
    }

    private static long? GetInt64(JsonElement element, string property) => element.TryGetProperty(property, out JsonElement value) && value.TryGetInt64(out long result) ? result : null;
}
