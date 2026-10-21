param(
    [Parameter(Mandatory=$true)][string]$PackageDir,
    [Parameter(Mandatory=$true)][string]$Setup,
    [Parameter(Mandatory=$true)][string]$LogRoot,
    [Parameter(Mandatory=$true)][string]$Version
)
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PackageDir 'captura.exe'
$info = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
if ($info.FileVersion -ne "$Version.0") { throw "EXE 版本不符：$($info.FileVersion)" }
foreach ($required in @('captura.exe.config','lib\Captura.Loc.dll','Languages\zh-CN.json','Settings\Captura.json')) {
    if (!(Test-Path -LiteralPath (Join-Path $PackageDir $required))) { throw "缺少运行文件：$required" }
}
$installDir = Join-Path $LogRoot ('install-test-' + [guid]::NewGuid().ToString('N'))
$installLog = Join-Path $LogRoot 'install.log'
$arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/TESTMODE=1','/LANG=chinesesimp',('/DIR="'+$installDir+'"'),('/LOG="'+$installLog+'"'))
$process = Start-Process -FilePath $Setup -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "安装测试失败：$($process.ExitCode)" }
Get-ChildItem -LiteralPath $PackageDir -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring($PackageDir.Length + 1)
    $installedFile = Join-Path $installDir $relative
    if (!(Test-Path -LiteralPath $installedFile) -or (Get-FileHash -LiteralPath $_.FullName).Hash -ne (Get-FileHash -LiteralPath $installedFile).Hash) { throw "安装载荷哈希不符：$relative" }
}
$uninstaller = Join-Path $installDir 'unins000.exe'
$process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "卸载测试失败：$($process.ExitCode)" }
if (Test-Path -LiteralPath (Join-Path $installDir 'captura.exe')) { throw '卸载后 EXE 仍然存在。' }
Write-Host '安装包验证通过：中文静默安装、完整载荷 SHA-256、卸载、EXE 版本和依赖文件。'
