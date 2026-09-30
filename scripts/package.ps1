param([string]$DiscordApplicationId = '', [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration Release -SkipTests:$SkipTests
$sdkPath = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$releaseConfig = Get-Content (Join-Path $projectRoot 'src\CinePresence.App\release.json') -Raw | ConvertFrom-Json
if (-not $DiscordApplicationId) { $DiscordApplicationId = $releaseConfig.discordApplicationId }
if ($DiscordApplicationId -notmatch '^\d{15,22}$') { throw 'A real public Discord application ID is required for a release.' }
$appVersion = ([xml](Get-Content (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$outputPath = Join-Path $projectRoot ('artifacts\CinePresence-' + $appVersion + '-win-x64')
Push-Location $projectRoot
try {
    & $sdkPath publish 'src\CinePresence.App\CinePresence.App.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -o $outputPath
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    & $sdkPath publish 'src\CinePresence.BrowserHost\CinePresence.BrowserHost.csproj' -c Release -r win-x64 --self-contained true -p:DebugType=None -o $outputPath
    if ($LASTEXITCODE -ne 0) { throw 'Browser host publish failed.' }
    $extensionPath = Join-Path $outputPath 'browser-companion'
    New-Item -ItemType Directory -Force $extensionPath | Out-Null
    foreach ($file in @('manifest.json', 'popup.html', 'popup.css', 'icons', 'dist')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot ('browser-companion\' + $file)) -Destination $extensionPath -Recurse -Force
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'browser-setup.html') -Destination $outputPath
    @{ discordApplicationId = $DiscordApplicationId } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'release.json') -Encoding utf8
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $outputPath
    Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $outputPath
    New-Item -ItemType Directory -Force (Join-Path $outputPath 'licenses') | Out-Null
    Copy-Item -Path (Join-Path $projectRoot 'licenses\*') -Destination (Join-Path $outputPath 'licenses')
    foreach ($runtime in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
        $runtimePath = Join-Path $projectRoot ('.tools\packages\' + $runtime)
        if (Test-Path -LiteralPath $runtimePath) {
            $versionPath = Get-ChildItem -LiteralPath $runtimePath -Directory | Sort-Object Name -Descending | Select-Object -First 1
            foreach ($notice in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
                $noticePath = Join-Path $versionPath.FullName $notice
                if (Test-Path -LiteralPath $noticePath) { Copy-Item -LiteralPath $noticePath -Destination (Join-Path $outputPath ('licenses\' + $runtime + '-' + $notice)) }
            }
        }
    }
    New-Item -ItemType Directory -Force (Join-Path $outputPath 'docs') | Out-Null
    Copy-Item -Path (Join-Path $projectRoot 'docs\*.md') -Destination (Join-Path $outputPath 'docs')
    $zipPath = Join-Path $projectRoot ('artifacts\CinePresence-' + $appVersion + '-win-x64.zip')
    Compress-Archive -Path $outputPath -DestinationPath $zipPath -Force
    & (Join-Path $PSScriptRoot 'build-installer.ps1') -Version $appVersion
    $setupPath = Join-Path $projectRoot ('artifacts\CinePresence-' + $appVersion + '-Setup-win-x64.exe')
    @($zipPath, $setupPath) | ForEach-Object { Get-FileHash -LiteralPath $_ -Algorithm SHA256 } | ForEach-Object { $_.Hash + '  ' + (Split-Path $_.Path -Leaf) } | Set-Content (Join-Path $projectRoot 'artifacts\SHA256SUMS.txt')
    Write-Output $zipPath
} finally { Pop-Location }
