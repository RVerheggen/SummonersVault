using SummonersVault.Core.Models;

namespace SummonersVault.Application.Abstractions;

public interface ILeagueClientGateway
{
    Task<LeagueClientStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<LeagueSnapshot> FetchCurrentSnapshotAsync(CancellationToken cancellationToken = default);
    Task<bool> LaunchAsync(string? configuredInstallDirectory, CancellationToken cancellationToken = default);
    Task<byte[]?> FetchAssetAsync(string assetPath, CancellationToken cancellationToken = default);
}

public interface ILeagueClientConfiguration
{
    void SetInstallDirectory(string? directory);
}

public interface IArtworkService
{
    Task<string?> ResolveAsync(string? assetPath, bool allowCommunityDragon, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
    long GetCacheSizeBytes();
}
