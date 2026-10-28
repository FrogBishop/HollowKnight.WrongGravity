[CmdletBinding()]
param(
    [string]$ReferencesPath = (Join-Path $PSScriptRoot '../../References')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$headers = @{
    Accept = 'application/vnd.github+json'
    'User-Agent' = 'WrongGravity-reference-restore'
    'X-GitHub-Api-Version' = '2022-11-28'
}
if ($env:GH_TOKEN) {
    $headers.Authorization = "Bearer $env:GH_TOKEN"
}

# Resolve latest independently on every run; only copy assemblies used by the project.
$dependencies = @(
    @{
        Repository = 'hk-modding/api'
        AssetPattern = 'moddingapi.*.windows.zip'
        Assemblies = @('Assembly-CSharp.dll', 'MMHOOK_Assembly-CSharp.dll')
    },
    @{
        Repository = 'fifty-six/HollowKnight.Vasi'
        AssetPattern = 'Vasi.zip'
        Assemblies = @('Vasi.dll')
    }
)

$temporaryDirectory = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$downloadDirectory = Join-Path $temporaryDirectory ("WrongGravity-references-" + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $downloadDirectory

try {
    $stagingDirectory = Join-Path $downloadDirectory 'references'
    $null = New-Item -ItemType Directory -Path $stagingDirectory

    foreach ($dependency in $dependencies) {
        $repository = $dependency.Repository
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repository/releases/latest" `
            -Headers $headers -MaximumRetryCount 3 -RetryIntervalSec 2
        $assets = @($release.assets | Where-Object { $_.name -like $dependency.AssetPattern })
        if ($assets.Count -ne 1) {
            throw "Expected one '$($dependency.AssetPattern)' asset in $repository release '$($release.tag_name)'; found $($assets.Count)."
        }

        $asset = $assets[0]
        Write-Host "Downloading $repository $($release.tag_name): $($asset.name)"
        $packageDirectory = Join-Path $downloadDirectory ($repository -replace '/', '-')
        $null = New-Item -ItemType Directory -Path $packageDirectory
        $archivePath = Join-Path $packageDirectory 'release.zip'
        $extractedDirectory = Join-Path $packageDirectory 'extracted'

        # Release assets are public. Keep the API token out of the download request.
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $archivePath `
            -MaximumRetryCount 3 -RetryIntervalSec 2
        Expand-Archive -LiteralPath $archivePath -DestinationPath $extractedDirectory

        foreach ($assemblyName in $dependency.Assemblies) {
            $assemblyFiles = @(Get-ChildItem -LiteralPath $extractedDirectory -Recurse -File -Filter $assemblyName)
            if ($assemblyFiles.Count -ne 1) {
                throw "Expected one $assemblyName in $repository release '$($release.tag_name)'; found $($assemblyFiles.Count)."
            }
            Copy-Item -LiteralPath $assemblyFiles[0].FullName -Destination (Join-Path $stagingDirectory $assemblyName)
        }
    }

    # Replace local references only after both releases have downloaded successfully.
    $null = New-Item -ItemType Directory -Path $ReferencesPath -Force
    foreach ($assembly in Get-ChildItem -LiteralPath $stagingDirectory -File) {
        Copy-Item -LiteralPath $assembly.FullName -Destination (Join-Path $ReferencesPath $assembly.Name) -Force
        Write-Host "Restored $($assembly.Name)"
    }
}
finally {
    $resolvedDownloadDirectory = (Resolve-Path -LiteralPath $downloadDirectory).ProviderPath
    if ([IO.Path]::GetDirectoryName($resolvedDownloadDirectory) -ne $temporaryDirectory) {
        throw "Refusing to remove a download directory outside $temporaryDirectory."
    }
    Remove-Item -LiteralPath $resolvedDownloadDirectory -Recurse -Force
}
