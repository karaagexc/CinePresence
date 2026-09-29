param([string]$VlcPath = 'C:\Program Files\VideoLAN\VLC\vlc.exe')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$output = Join-Path $projectRoot 'artifacts\vlc-integration'
New-Item -ItemType Directory -Force $output | Out-Null
$video = Join-Path $output 'CinePresence.Sample.S02E04.mp4'
$ffmpeg = (Get-Command ffmpeg -ErrorAction Stop).Source
& $ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'color=c=0x242033:s=320x180:r=10' -t 120 -c:v libx264 -pix_fmt yuv420p -metadata 'title=CinePresence Sample S02E04' $video
if ($LASTEXITCODE -ne 0) { throw 'Could not generate local test video.' }
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
$password = [Guid]::NewGuid().ToString('N')
$previousPassword = $env:CINEPRESENCE_TEST_VLC_PASSWORD
$env:CINEPRESENCE_TEST_VLC_PASSWORD = $password
$headers = @{ Authorization = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(':' + $password)) }
$endpoint = 'http://127.0.0.1:' + $port + '/requests/status.json'
$vlc = $null
try {
    $vlc = Start-Process -FilePath $VlcPath -ArgumentList @('--ignore-config','--no-one-instance','--intf=qt','--qt-start-minimized','--no-qt-privacy-ask','--no-qt-error-dialogs','--extraintf=http,win10smtc','--vout=dummy','--avcodec-hw=none','--no-audio','--loop','--no-video-title-show','--http-host=127.0.0.1',('--http-port=' + $port),('--http-password=' + $password),('"' + $video + '"')) -WindowStyle Hidden -PassThru
    $ready = $false
    for ($i = 0; $i -lt 30; $i++) {
        try { $state = Invoke-RestMethod -Uri $endpoint -Headers $headers -TimeoutSec 2; if ($state.state -eq 'playing') { $ready = $true; break } } catch { }
        Start-Sleep -Milliseconds 200
    }
    if (-not $ready) { throw 'The isolated VLC test instance did not start its local interface.' }
    $sdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
    $dll = Join-Path $projectRoot 'src\CinePresence.App\bin\Debug\net10.0-windows10.0.22621.0\CinePresence.dll'
    function Probe([string]$name) {
        $report = Join-Path $output ($name + '.json')
        $probe = Start-Process -FilePath $sdk -ArgumentList @('exec',('"' + $dll + '"'),'--probe-vlc',('"' + $report + '"'),$port) -WindowStyle Hidden -PassThru
        if (-not $probe.WaitForExit(12000)) { Stop-Process -Id $probe.Id; throw 'VLC probe timed out.' }
        if ($probe.ExitCode -ne 0) { throw 'VLC probe failed.' }
        Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    }
    $playing = Probe 'playing'
    if ($playing.sources[0].state -ne 'Playing' -or $playing.sources[0].duration -lt 119) { throw 'Playing state or duration was not detected.' }
    $null = Invoke-RestMethod -Uri ($endpoint + '?command=pl_pause') -Headers $headers
    $paused = Probe 'paused'
    if ($paused.sources[0].state -ne 'Paused') { throw 'Pause was not detected.' }
    $null = Invoke-RestMethod -Uri ($endpoint + '?command=pl_pause') -Headers $headers
    $null = Invoke-RestMethod -Uri ($endpoint + '?command=seek&val=45') -Headers $headers
    for ($i = 0; $i -lt 20; $i++) {
        $seekState = Invoke-RestMethod -Uri $endpoint -Headers $headers
        if ($seekState.time -ge 44) { break }
        Start-Sleep -Milliseconds 100
    }
    $null = Invoke-RestMethod -Uri ($endpoint + '?command=pl_pause') -Headers $headers
    $seek = Probe 'seek'
    if ([Math]::Abs($seek.sources[0].position - 45) -gt 2) { throw 'Seek position was not detected.' }
    $null = Invoke-RestMethod -Uri ($endpoint + '?command=pl_pause') -Headers $headers
    $null = Invoke-RestMethod -Uri ($endpoint + '?command=rate&val=2') -Headers $headers
    $speed = Probe 'speed'
    if ($speed.sources[0].rate -ne 2 -or $speed.sources[0].state -ne 'Playing') { throw 'Resume or playback rate was not detected.' }
    $windowsReport = Join-Path $output 'windows-and-discord.json'
    $diagnostic = Start-Process -FilePath $sdk -ArgumentList @('exec',('"' + $dll + '"'),'--diagnose',('"' + $windowsReport + '"')) -WindowStyle Hidden -PassThru
    if (-not $diagnostic.WaitForExit(15000)) { Stop-Process -Id $diagnostic.Id; throw 'Diagnostic timed out.' }
    $null = Invoke-RestMethod -Uri ($endpoint + '?command=pl_stop') -Headers $headers
    $stopped = Probe 'stopped'
    if (@($stopped.sources).Count -ne 0) { throw 'Stop did not clear the source.' }
    'PASS: live VLC playback, pause, seek, resume, speed, and stop. No Discord activity was published.' | Tee-Object -FilePath (Join-Path $output 'result.txt')
} finally {
    if ($vlc -and -not $vlc.HasExited) { Stop-Process -Id $vlc.Id -ErrorAction SilentlyContinue }
    $env:CINEPRESENCE_TEST_VLC_PASSWORD = $previousPassword
}
