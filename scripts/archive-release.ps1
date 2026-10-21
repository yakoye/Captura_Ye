param(
    [Parameter(Mandatory=$true)][string]$Source,
    [Parameter(Mandatory=$true)][string]$Version,
    [string[]]$ArchiveRoots = @('C:\Users\color\Downloads\yakoye-release', '\\192.168.7.100\Downloads\yakoye-release')
)
$ErrorActionPreference = 'Stop'
$sourceRoot = (Resolve-Path -LiteralPath $Source).Path
$files = @(Get-ChildItem -LiteralPath $sourceRoot -File -Recurse)
if (!$files.Count) { throw '发布目录为空，拒绝归档。' }
$manifest = @($files | ForEach-Object {
    [pscustomobject]@{ RelativePath=$_.FullName.Substring($sourceRoot.Length + 1); Length=$_.Length; SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$completed = @()
foreach ($archiveRoot in $ArchiveRoots) {
    $destination = Join-Path $archiveRoot "Captura_Ye\v$Version"
    try {
        New-Item -ItemType Directory -Force -Path $destination | Out-Null
        # 先检查全部冲突，再开始复制。
        foreach ($entry in $manifest) {
            $target = Join-Path $destination $entry.RelativePath
            if (Test-Path -LiteralPath $target) {
                if ((Get-Item -LiteralPath $target).Length -ne $entry.Length -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.SHA256) {
                    throw "归档同名文件冲突，未覆盖：$target"
                }
            }
        }
        foreach ($entry in $manifest) {
            $target = Join-Path $destination $entry.RelativePath
            if (!(Test-Path -LiteralPath $target)) {
                New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
                Copy-Item -LiteralPath (Join-Path $sourceRoot $entry.RelativePath) -Destination $target
            }
            if ((Get-Item -LiteralPath $target).Length -ne $entry.Length -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.SHA256) {
                throw "归档校验失败：$target"
            }
        }
        $completed += $destination
        Write-Host "归档完成并通过 SHA-256 校验：$destination（$($files.Count) 个文件）"
    } catch {
        throw "归档未全部完成。已完成：$($completed -join '；')。失败目标：$destination。原因：$_"
    }
}
