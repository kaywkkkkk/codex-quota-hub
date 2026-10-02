param(
    [string]$Dotnet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseVersion = (Get-Content -LiteralPath (Join-Path $projectRoot 'VERSION') -Raw).Trim()
if ($releaseVersion -notmatch '^\d+\.\d+(\.\d+)?$') {
    throw 'VERSION must contain a numeric release version.'
}

$outputRoot = Join-Path $projectRoot 'release-assets'
$buildRoot = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $outputRoot,$buildRoot -Force | Out-Null

# Remove only generated output beneath this project's designated output roots.
function Reset-GeneratedDirectory([string]$Path, [string]$AllowedRoot) {
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $rootPrefix = [IO.Path]::GetFullPath($AllowedRoot).TrimEnd([char[]]'\/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear a path outside the output directory: $resolvedPath"
    }
    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $resolvedPath | Out-Null
}

$targets = @(
    @{Name='CodexQuotaHub'; Project='src/CodexQuotaHub.Desktop/CodexQuotaHub.Desktop.csproj'; Exe='CodexQuotaHub.exe'; Output='desktop'},
    @{Name='CodexQuotaHub-Cli'; Project='src/CodexQuotaHub.Cli/CodexQuotaHub.Cli.csproj'; Exe='CodexQuotaHub.Cli.exe'; Output='cli'}
)
$checksumLines = @()

Push-Location $projectRoot
try {
    foreach ($target in $targets) {
        $publishDirectory = Join-Path $buildRoot $target.Output
        $packageName = "$($target.Name)-$releaseVersion-win-x64"
        $packageDirectory = Join-Path $outputRoot $packageName
        Reset-GeneratedDirectory $publishDirectory $buildRoot
        Reset-GeneratedDirectory $packageDirectory $outputRoot

        & $Dotnet publish $target.Project -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $publishDirectory
        if ($LASTEXITCODE -ne 0) { throw "Publish failed for $($target.Name)." }

        Copy-Item -LiteralPath (Join-Path $publishDirectory $target.Exe) -Destination $packageDirectory
        foreach ($document in @('README.md','README.zh-TW.md','README.zh-CN.md','LICENSE','THIRD-PARTY-NOTICES.md')) {
            Copy-Item -LiteralPath (Join-Path $projectRoot $document) -Destination $packageDirectory
        }
        New-Item -ItemType Directory -Path (Join-Path $packageDirectory 'docs') | Out-Null
        Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/third-party') `
            -Destination (Join-Path $packageDirectory 'docs/third-party') -Recurse
        Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/previews') `
            -Destination (Join-Path $packageDirectory 'docs/previews') -Recurse
        Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/RELEASE.md') `
            -Destination (Join-Path $packageDirectory 'docs/RELEASE.md')
        Copy-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Destination $packageDirectory

        $zipFile = Join-Path $outputRoot "$packageName.zip"
        Compress-Archive -Path $packageDirectory -DestinationPath $zipFile -CompressionLevel Optimal -Force
        $digest = (Get-FileHash -LiteralPath $zipFile -Algorithm SHA256).Hash.ToLowerInvariant()
        $checksumLines += "$digest  $packageName.zip"
        Write-Host "Created $zipFile"
    }
    [IO.File]::WriteAllLines((Join-Path $outputRoot 'SHA256SUMS.txt'), [string[]]$checksumLines,
        (New-Object System.Text.UTF8Encoding($false)))
}
finally {
    Pop-Location
}
