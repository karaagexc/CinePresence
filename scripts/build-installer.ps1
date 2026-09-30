param([string]$Version)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $Version) { $Version = ([xml](Get-Content (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version }
$compiler = Join-Path $projectRoot '.tools\inno\ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $download = Join-Path $projectRoot '.tools\innosetup-7.1.0-x64.exe'
    Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe' -OutFile $download
    $signature = Get-AuthenticodeSignature -LiteralPath $download
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys') { throw 'Inno Setup signature verification failed.' }
    $destination = Join-Path $projectRoot '.tools\inno'
    $process = Start-Process -FilePath $download -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER','/SP-','/NOICONS',('/DIR="'+$destination+'"')) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw 'Inno Setup compiler installation failed.' }
}
$source = Join-Path $projectRoot ('artifacts\CinePresence-' + $Version + '-win-x64')
& $compiler ('/DAppVersion=' + $Version) ('/DSourceDir=' + $source) (Join-Path $projectRoot 'installer\CinePresence.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
