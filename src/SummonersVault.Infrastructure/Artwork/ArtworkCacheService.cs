using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using SummonersVault.Application.Abstractions;
using SummonersVault.Infrastructure.Storage;

namespace SummonersVault.Infrastructure.Artwork;

public sealed class ArtworkCacheService : IArtworkService, IDisposable
{
    internal const long MaximumCacheBytes = 256L * 1024 * 1024;
    internal const long MaximumImageBytes = 8L * 1024 * 1024;
    private const string AssetPrefix = "/lol-game-data/assets/";
    private static readonly Uri CommunityDragonRoot = new("https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/");
    private readonly VaultPaths _paths;
    private readonly ILeagueClientGateway _league;
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposeState;

    public ArtworkCacheService(VaultPaths paths, ILeagueClientGateway league, HttpClient? httpClient = null)
    {
        _paths = paths;
        _league = league;
        _ownsClient = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<string?> ResolveAsync(string? assetPath, bool allowCommunityDragon, CancellationToken cancellationToken = default)
    {
        string? canonical = Canonicalize(assetPath);
        if (canonical is null)
        {
            return null;
        }

        _paths.EnsureArtworkCacheCreated();
        string? cached = FindCached(canonical);
        if (cached is not null) { File.SetLastAccessTimeUtc(cached, DateTime.UtcNow); return cached; }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = FindCached(canonical);
            if (cached is not null)
            {
                return cached;
            }

            byte[]? bytes = null;
            string? mediaType = null;
            if (allowCommunityDragon && TryMapCommunityDragon(canonical, out Uri? uri))
            {
                (bytes, mediaType) = await DownloadAsync(uri, cancellationToken).ConfigureAwait(false);
            }

            bytes ??= await _league.FetchAssetAsync(canonical, cancellationToken).ConfigureAwait(false);
            if (bytes is null || bytes.LongLength > MaximumImageBytes || !TryDetectImage(bytes, mediaType, out string? extension))
            {
                return null;
            }

            string destination = Path.Combine(_paths.ArtworkCacheDirectory, Hash(canonical) + extension);
            string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
                File.Move(temporary, destination, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            EnforceLimit(destination);
            return destination;
        }
        finally { _gate.Release(); }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_paths.ArtworkCacheDirectory))
        {
            return Task.CompletedTask;
        }

        foreach (string file in Directory.EnumerateFiles(_paths.ArtworkCacheDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { File.Delete(file); } catch (IOException) { }
        }
        return Task.CompletedTask;
    }

    public long GetCacheSizeBytes() => Directory.Exists(_paths.ArtworkCacheDirectory)
        ? Directory.EnumerateFiles(_paths.ArtworkCacheDirectory).Sum(path => new FileInfo(path).Length) : 0;

    internal static bool TryMapCommunityDragon(string assetPath, out Uri uri)
    {
        uri = CommunityDragonRoot;
        string? canonical = Canonicalize(assetPath);
        if (canonical is null || !canonical.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string relative = canonical[AssetPrefix.Length..].ToLowerInvariant();
        if (relative.Length == 0 || relative.Split('/').Any(part => part is "" or "." or ".."))
        {
            return false;
        }

        uri = new Uri(CommunityDragonRoot, string.Join('/', relative.Split('/').Select(Uri.EscapeDataString)));
        return uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("raw.communitydragon.org", StringComparison.OrdinalIgnoreCase);
    }

    internal static string? Canonicalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string value = path.Trim().Replace('\\', '/');
        if (!value.StartsWith('/') || value.Contains("../", StringComparison.Ordinal) || value.Contains("/./", StringComparison.Ordinal)
            || value.Contains('?') || value.Contains('#') || Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            return null;
        }

        return value;
    }

    private async Task<(byte[]? Bytes, string? MediaType)> DownloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaximumImageBytes)
            {
                return (null, null);
            }

            string? mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return (null, null);
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaximumImageBytes)
                {
                    return (null, null);
                }

                buffer.Write(chunk, 0, read);
            }
            return (buffer.ToArray(), mediaType);
        }
        catch (HttpRequestException) { return (null, null); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return (null, null); }
    }

    private string? FindCached(string canonical)
    {
        if (!Directory.Exists(_paths.ArtworkCacheDirectory))
        {
            return null;
        }

        return Directory.EnumerateFiles(_paths.ArtworkCacheDirectory, Hash(canonical) + ".*").FirstOrDefault();
    }

    private void EnforceLimit(string keep)
    {
        var files = Directory.EnumerateFiles(_paths.ArtworkCacheDirectory).Select(path => new FileInfo(path)).OrderBy(x => x.LastAccessTimeUtc).ToList();
        long total = files.Sum(x => x.Length);
        foreach (FileInfo? file in files)
        {
            if (total <= MaximumCacheBytes)
            {
                break;
            }

            if (file.FullName.Equals(keep, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try { total -= file.Length; file.Delete(); } catch (IOException) { }
        }
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool TryDetectImage(byte[] bytes, string? mediaType, out string extension)
    {
        extension = string.Empty;
        if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            extension = ".png";
        }
        else if (bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255)
        {
            extension = ".jpg";
        }
        else if (bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP")
        {
            extension = ".webp";
        }

        return extension.Length > 0 && (mediaType is null || mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _gate.Dispose();
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}
