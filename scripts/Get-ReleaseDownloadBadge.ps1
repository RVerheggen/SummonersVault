[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string] $Repository,

    [Parameter(Mandatory)]
    [string] $OutputPath,

    [string] $GitHubToken
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$headers = @{
    Accept = 'application/vnd.github+json'
    'User-Agent' = 'SummonersVault-release-download-counter'
    'X-GitHub-Api-Version' = '2022-11-28'
}

if (-not [string]::IsNullOrWhiteSpace($GitHubToken)) {
    $headers.Authorization = "Bearer $GitHubToken"
}

$installerAssetName = 'SummonersVault.Desktop-win-Setup.exe'
$portableAssetName = 'SummonersVault.Desktop-win-Portable.zip'
$stableTagPattern = '^v?(?<Version>[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?)$'
$pageSize = 100
$page = 1
$installerDownloads = [long]0
$portableDownloads = [long]0
$fullPackageDownloads = [long]0
$deltaPackageDownloads = [long]0

do {
    $uri = "https://api.github.com/repos/$Repository/releases?per_page=$pageSize&page=$page"
    $response = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
    $releases = @($response)

    foreach ($release in $releases) {
        if ($release.draft -or $release.prerelease) {
            continue
        }

        if ([string]$release.tag_name -notmatch $stableTagPattern) {
            continue
        }

        $version = $Matches.Version
        $fullPackageAssetName = "SummonersVault.Desktop-$version-full.nupkg"
        $deltaPackageAssetName = "SummonersVault.Desktop-$version-delta.nupkg"

        foreach ($asset in @($release.assets)) {
            $assetName = [string]$asset.name
            $downloadCount = [long]$asset.download_count

            if ($assetName.Equals($installerAssetName, [StringComparison]::Ordinal)) {
                $installerDownloads += $downloadCount
            } elseif ($assetName.Equals($portableAssetName, [StringComparison]::Ordinal)) {
                $portableDownloads += $downloadCount
            } elseif ($assetName.Equals($fullPackageAssetName, [StringComparison]::Ordinal)) {
                $fullPackageDownloads += $downloadCount
            } elseif ($assetName.Equals($deltaPackageAssetName, [StringComparison]::Ordinal)) {
                $deltaPackageDownloads += $downloadCount
            }
        }
    }

    $page++
} while ($releases.Count -eq $pageSize)

$totalDownloads = $installerDownloads + $portableDownloads + $fullPackageDownloads + $deltaPackageDownloads
$badge = [ordered]@{
    schemaVersion = 1
    label = 'release downloads'
    message = $totalDownloads.ToString('N0', [System.Globalization.CultureInfo]::InvariantCulture)
    color = 'd0a54f'
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    $null = New-Item -ItemType Directory -Path $outputDirectory -Force
}

$badge | ConvertTo-Json | Set-Content -Path $OutputPath -Encoding utf8NoBOM

Write-Host "Release downloads: $totalDownloads"
Write-Host "  Installer: $installerDownloads"
Write-Host "  Portable: $portableDownloads"
Write-Host "  Velopack full packages: $fullPackageDownloads"
Write-Host "  Velopack delta packages: $deltaPackageDownloads"
