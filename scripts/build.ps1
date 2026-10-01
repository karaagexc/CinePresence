param([ValidateSet('Debug','Release')][string]$Configuration = 'Release', [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$sdkPath = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
if (-not (Test-Path -LiteralPath $sdkPath)) {
    New-Item -ItemType Directory -Force (Join-Path $projectRoot '.tools') | Out-Null
    $installer = Join-Path $projectRoot '.tools\dotnet-install.ps1'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & $installer -Channel 10.0 -InstallDir (Join-Path $projectRoot '.tools\dotnet') -NoPath
    if (-not (Test-Path -LiteralPath $sdkPath)) { throw 'The .NET 10 SDK could not be installed.' }
}
Push-Location $projectRoot
try {
    & npm.cmd ci --prefix 'browser-companion' --ignore-scripts --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw 'Companion dependency restore failed. Node.js is required for development.' }
    & npm.cmd run build --prefix 'browser-companion'
    if ($LASTEXITCODE -ne 0) { throw 'TypeScript compilation failed.' }
    & $sdkPath restore 'CinePresence.sln'
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    & $sdkPath build 'CinePresence.sln' -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    if (-not $SkipTests) {
        & $sdkPath test 'CinePresence.sln' -c $Configuration --no-build --no-restore --logger 'console;verbosity=minimal'
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
        & node --test 'tests/browser-companion.test.mts'
        if ($LASTEXITCODE -ne 0) { throw 'Browser companion tests failed. Node.js is required for development tests only.' }
    }
} finally { Pop-Location }
