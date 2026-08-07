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
        if (root.TryGetProperty("games", out var wrapper) && wrapper.ValueKind == JsonValueKind.Object && wrapper.TryGetProperty("games", out var games)) root = games;
        else if (root.TryGetProperty("games", out var directGames)) root = directGames;
        if (root.ValueKind != JsonValueKind.Array) return MatchSnapshotResult.Failed;
        var game = root.EnumerateArray().FirstOrDefault();
        if (game.ValueKind == JsonValueKind.Undefined) return MatchSnapshotResult.Empty;
        var matchId = GetInt64(game, "gameId");
        if (game.TryGetProperty("gameCreation", out var creation) && creation.ValueKind == JsonValueKind.Number && creation.TryGetInt64(out var milliseconds))
        {
            try { return MatchSnapshotResult.Known(DateTimeOffset.FromUnixTimeMilliseconds(milliseconds), matchId); }
            catch (ArgumentOutOfRangeException) { return MatchSnapshotResult.Failed; }
        }
        var textual = game.TryGetProperty("gameCreationDate", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return DateTimeOffset.TryParse(textual, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? MatchSnapshotResult.Known(parsed, matchId)
            : MatchSnapshotResult.Failed;
    }

    private static long? GetInt64(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.TryGetInt64(out var result) ? result : null;
}
