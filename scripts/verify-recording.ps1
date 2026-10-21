param([string]$Root, [Parameter(Mandatory=$true)][string]$LogRoot)
$ErrorActionPreference = 'Stop'
if (!$Root) { $Root = Split-Path $PSScriptRoot -Parent }
$cli = Join-Path $Root 'src\Captura.Console\bin\Release\captura-cli.exe'
$runtimeRoot = Join-Path $LogRoot 'recording'
New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
$settingsRoot = Join-Path $runtimeRoot 'settings'
New-Item -ItemType Directory -Force -Path $settingsRoot | Out-Null
$settingsFile = Join-Path $settingsRoot 'Captura.json'
[IO.File]::WriteAllText($settingsFile, '{"WindowsSettings":{"UseGdi":true},"UI":{"Language":"zh-CN"}}')
$shot = Join-Path $runtimeRoot 'shot.png'
& $cli shot --source '10,10,320,240' -f $shot
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $shot)) { throw '实际截图测试失败。' }
Add-Type -AssemblyName System.Drawing
$bitmap = [Drawing.Image]::FromFile($shot)
try { if ($bitmap.Width -ne 320 -or $bitmap.Height -ne 240) { throw '截图尺寸不符。' } } finally { $bitmap.Dispose() }
$recordings = @(@{Name='recording.avi';Encoder='sharpavi:0';Codec='mjpeg'})
if (Get-Command ffmpeg.exe -ErrorAction SilentlyContinue) {
    $recordings += @{Name='recording.mp4';Encoder='ffmpeg:0';Codec='h264'}
} else { Write-Host '未找到系统 FFmpeg：跳过 MP4 运行测试；AVI 和截图仍执行。' }
foreach ($recording in $recordings) {
    $target = Join-Path $runtimeRoot $recording.Name
    & $cli start --source '10,10,320,240' --encoder $recording.Encoder -r 5 -t 2 --settings $settingsRoot -f $target
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $target) -or (Get-Item -LiteralPath $target).Length -lt 1024) { throw "实际录制测试失败：$target" }
    if (Get-Command ffprobe.exe -ErrorAction SilentlyContinue) {
        $probeJson = & ffprobe.exe -v error -show_entries 'stream=codec_name,width,height' -show_entries 'format=duration' -of json $target
        if ($LASTEXITCODE -ne 0) { throw "视频解析失败：$target" }
        $probe = $probeJson | ConvertFrom-Json
        $stream = @($probe.streams | Where-Object codec_name -EQ $recording.Codec)
        if (!$stream.Count -or $stream[0].width -ne 320 -or $stream[0].height -ne 240 -or [double]::Parse($probe.format.duration, [Globalization.CultureInfo]::InvariantCulture) -lt 1.5) { throw "视频内容或时长不符：$target" }
        $probeJson | Set-Content -LiteralPath ($target + '.json') -Encoding UTF8
    } else { Write-Host '未找到 ffprobe：已检查录制文件生成与大小，未执行流解析。' }
}
Write-Host "实际运行检查通过：320×240 截图及 $($recordings.Count) 种两秒视频录制。证据：$runtimeRoot"
