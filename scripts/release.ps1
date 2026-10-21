[CmdletBinding()]
param(
    [switch]$Publish,
    [switch]$Archive,
    [switch]$AutoVersion,
    [switch]$ReusePackage,
    [string]$Version,
    [string]$IsccPath,
    [string]$Repository = 'yakoye/Captura_Ye'
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$logRoot = Join-Path $root ('temp\logs\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
Start-Transcript -Path (Join-Path $logRoot 'release.log') | Out-Null

function Run([string]$Exe, [string[]]$Arguments) {
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "命令失败（退出代码 $LASTEXITCODE）：$Exe $($Arguments -join ' ')" }
}
function Require-Command([string]$Name) {
    if (!(Get-Command $Name -ErrorAction SilentlyContinue)) { throw "未找到 $Name，请先按 README.md 安装构建工具。" }
}
function Download-Release([string]$Tag, [string]$Destination) {
    $originalDebug = $env:GODEBUG
    try {
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            $attemptDir = Join-Path $Destination "attempt-$attempt"
            New-Item -ItemType Directory -Force -Path $attemptDir | Out-Null
            if ($attempt -gt 1) {
                # 仅为当前进程及子进程启用 HTTP/1.1，规避部分网络的 HTTP/2 EOF。
                $debugParts = @($originalDebug -split ',' | Where-Object { $_ -and $_ -notmatch '^http2client=' })
                $env:GODEBUG = (@($debugParts) + 'http2client=0') -join ','
            }
            try {
                Run 'gh' @('release','download',$Tag,'--repo',$Repository,'--dir',$attemptDir) | Out-Host
                return $attemptDir
            } catch {
                if ($attempt -eq 3) { throw }
                Write-Host "回下载第 $attempt 次失败，保留该次文件和日志，改用 HTTP/1.1 重试。"
                Start-Sleep -Seconds 2
            }
        }
    } finally {
        [Environment]::SetEnvironmentVariable('GODEBUG', $originalDebug, 'Process')
    }
}
function Copy-Verified([string]$From, [string]$To) {
    if (Test-Path -LiteralPath $To) {
        if ((Get-FileHash -LiteralPath $From).Hash -ne (Get-FileHash -LiteralPath $To).Hash) { throw "同版本文件已存在且内容不同，未覆盖：$To。请使用新版本号。" }
    } else { Copy-Item -LiteralPath $From -Destination $To }
}
function Get-Compiler {
    if ($IsccPath) { return (Resolve-Path -LiteralPath $IsccPath).Path }
    $localCompiler = Join-Path $root 'temp\build-tools\Inno\ISCC.exe'
    if (Test-Path -LiteralPath $localCompiler) { return $localCompiler }
    $installed = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($installed) { return $installed.Source }
    $installerPath = Join-Path $root 'temp\build-tools\innosetup.exe'
    New-Item -ItemType Directory -Force -Path (Split-Path $installerPath -Parent) | Out-Null
    if (!(Test-Path -LiteralPath $installerPath)) {
        Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' -UseBasicParsing -OutFile $installerPath
    }
    $expected = '9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732'
    if ((Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash -ne $expected) { throw 'Inno Setup 下载文件哈希不符，已停止。' }
    $signature = Get-AuthenticodeSignature -LiteralPath $installerPath
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*Pyrsys B.V.*') { throw 'Inno Setup 发布者签名验证失败，已停止。' }
    $compilerFolder = Split-Path $localCompiler -Parent
    $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER','/NOICONS',('/DIR="' + $compilerFolder + '"'))
    $process = Start-Process -FilePath $installerPath -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $localCompiler)) { throw 'Inno Setup 编译器准备失败。' }
    return $localCompiler
}

try {
    Require-Command 'dotnet'
    Require-Command 'git'
    if ($Publish) { Require-Command 'gh'; Run 'gh' @('auth','status') }
    $assemblyFile = Join-Path $root 'src\Captura\Properties\AssemblyInfo.cs'
    $assembly = Get-Content -LiteralPath $assemblyFile -Raw -Encoding UTF8
    $currentVersion = [version]([regex]::Match($assembly, 'AssemblyVersion\("([0-9.]+)"\)').Groups[1].Value)
    $nextVersion = $currentVersion.ToString(3)
    if ($Version) {
        if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw '版本号必须使用 X.Y.Z 格式。' }
        $nextVersion = $Version
    } elseif ($AutoVersion) {
        $published = @(& git tag --list)
        if ($Publish) {
            $publishedJson = & gh release list --repo $Repository --limit 100 --json tagName
            if ($LASTEXITCODE -ne 0) { throw '无法读取远程发行版，未修改版本号。' }
            $publishedReleases = ConvertFrom-Json -InputObject ($publishedJson -join "`n")
            $published += @($publishedReleases | ForEach-Object tagName)
        }
        while ($published -contains "v$nextVersion") {
            $v = [version]$nextVersion
            $nextVersion = '{0}.{1}.{2}' -f $v.Major, $v.Minor, ($v.Build + 1)
        }
    }
    if ($nextVersion -ne $currentVersion.ToString(3)) {
        if ($ReusePackage) { throw '复用发布包时不能修改版本。' }
        $assembly = $assembly -replace 'AssemblyVersion\("[0-9.]+"\)', "AssemblyVersion(`"$nextVersion.0`")"
        $assembly = $assembly -replace 'AssemblyFileVersion\("[0-9.]+"\)', "AssemblyFileVersion(`"$nextVersion.0`")"
        $assembly = $assembly -replace 'AssemblyInformationalVersion\("[0-9.]+"\)', "AssemblyInformationalVersion(`"$nextVersion`")"
        [IO.File]::WriteAllText($assemblyFile, $assembly, (New-Object Text.UTF8Encoding($true)))
    }
    $Version = $nextVersion
    $cliAssemblyFile = Join-Path $root 'src\Captura.Console\Properties\AssemblyInfo.cs'
    $cliAssembly = Get-Content -LiteralPath $cliAssemblyFile -Raw -Encoding UTF8
    $newCliAssembly = $cliAssembly -replace 'AssemblyVersion\("[0-9.]+"\)', "AssemblyVersion(`"$Version.0`")"
    if ($newCliAssembly -ne $cliAssembly) {
        if ($ReusePackage) { throw '命令行版本与发布包不一致，请重新构建。' }
        [IO.File]::WriteAllText($cliAssemblyFile, $newCliAssembly, (New-Object Text.UTF8Encoding($true)))
    }
    $tag = "v$Version"
    $releaseDir = Join-Path $root "dist\$tag"
    Write-Host "准备 Captura Ye 中文版 $tag，日志：$logRoot"
    & (Join-Path $PSScriptRoot 'verify-localization.ps1') -Root $root

    if (!$ReusePackage) {
        $aboutFile = Join-Path $root 'src\Captura\Pages\AboutPage.xaml'
        $about = (Get-Content $aboutFile -Raw -Encoding UTF8) -replace '发行日期：\d{4}-\d{2}-\d{2}', ('发行日期：' + (Get-Date -Format 'yyyy-MM-dd'))
        [IO.File]::WriteAllText($aboutFile, $about, (New-Object Text.UTF8Encoding($true)))
        $compiler = Get-Compiler
        Run 'dotnet' @('build','src\Captura\Captura.csproj','-c','Release','-t:Rebuild','-v','minimal')
        Run 'dotnet' @('build','src\Captura.Console\Captura.Console.csproj','-c','Release','-v','minimal')
        $frameTests = '(FullyQualifiedName~FrameReviewTests|FullyQualifiedName~FrameEditingTests)'
        if (!(Get-Command ffmpeg.exe -ErrorAction SilentlyContinue)) {
            $frameTests = '((FullyQualifiedName~FrameReviewTests&FullyQualifiedName!~ExactFramePreviewMatchesBatchPngAndCompositions&FullyQualifiedName!~LargeSelectionCrossesBatchBoundaryWithoutLosingFrames&FullyQualifiedName!~RunningDecoderCanBeCancelled)|(FullyQualifiedName~FrameEditingTests&FullyQualifiedName!~FFmpeg))'
            Write-Host '系统 PATH 未找到 FFmpeg：跳过 19 项媒体集成测试，仍运行 19 项帧逻辑测试。'
        }
        $testFilter = 'FullyQualifiedName~PropertyStoreTests|FullyQualifiedName~RecorderTests|FullyQualifiedName~AudioFileWriterTests|FullyQualifiedName~ImageProviderTests|FullyQualifiedName~ReleaseBehaviorTests|' + $frameTests
        Run 'dotnet' @('test','src\Tests\Tests.csproj','-c','Release','--filter',$testFilter,'--logger',"trx;LogFileName=core.trx",'--results-directory',$logRoot,'-v','minimal')
        & (Join-Path $PSScriptRoot 'verify-recording.ps1') -Root $root -LogRoot $logRoot
        $stage = Join-Path $root ('temp\package-' + [guid]::NewGuid().ToString('N'))
        $portable = Join-Path $stage 'CapturaYe'
        New-Item -ItemType Directory -Force -Path $portable | Out-Null
        Copy-Item (Join-Path $root 'src\Captura\bin\Release\*') -Destination $portable -Recurse
        # 便携版使用自己的配置目录，不读取上游 Captura 的旧配置。
        New-Item -ItemType Directory -Force -Path (Join-Path $portable 'Settings') | Out-Null
        [IO.File]::WriteAllText((Join-Path $portable 'Settings\Captura.json'), '{"UI":{"Language":"zh-CN"}}', (New-Object Text.UTF8Encoding($false)))
        Copy-Item (Join-Path $root 'LICENSE.md') -Destination $portable
        Copy-Item (Join-Path $root 'src\Captura\Images\PlayerIcons') -Destination (Join-Path $portable 'PlayerIcons') -Recurse
        Copy-Item (Join-Path $root 'README.md') -Destination $portable
        $notes = @"
# Captura Ye 中文版 $tag

- 补齐简体中文界面、悬停说明、模式名称、错误提示和颜色选择器。
- 首次启动默认使用简体中文；更新关于页面、维护者和项目链接。
- 安装程序支持简体中文和英文选择；便携版保留完整运行依赖。
- 提供 build.cmd 和 release.cmd，自动编译、检查、打包、双归档和发布。

运行要求：Windows 10/11，.NET Framework 4.7.2 或更高版本。MP4 等格式需要 FFmpeg，可在程序内下载或指定目录。网络上传功能需要自行配置服务凭据。

验证：中文资源完整性检查、核心及回归测试、实际截图和 AVI 录制（系统有 FFmpeg 时也验证 MP4）、中文安装包静默安装/卸载及载荷哈希校验。详细日志保存在本地 temp/logs。
"@
        $versionNotes = Join-Path $root "docs\releases\$tag.md"
        if (Test-Path -LiteralPath $versionNotes) { $notes = Get-Content -LiteralPath $versionNotes -Raw -Encoding UTF8 }
        [IO.File]::WriteAllText((Join-Path $stage 'release-notes.md'), $notes, (New-Object Text.UTF8Encoding($true)))
        Copy-Item (Join-Path $stage 'release-notes.md') -Destination $portable
        $zip = Join-Path $stage "CapturaYe-$tag-Portable.zip"
        Compress-Archive -LiteralPath $portable -DestinationPath $zip -CompressionLevel Optimal
        Run $compiler @('/Qp',"/DMyAppVersion=$Version","/DPackageDir=$portable","/DReleaseDir=$stage",(Join-Path $root 'installer\CapturaYe.iss'))
        $setup = Join-Path $stage "CapturaYe-$tag-Setup.exe"
        & (Join-Path $PSScriptRoot 'verify-package.ps1') -PackageDir $portable -Setup $setup -LogRoot $logRoot -Version $Version
        $sourcePaths = @(& git ls-files --cached --others --exclude-standard | Where-Object { $_ -match '\.(cs|xaml|csproj|props|targets|json|ps1|cmd|iss|isl)$' })
        $sourceFiles = @($sourcePaths | Sort-Object -Unique | ForEach-Object { [pscustomobject]@{Path=$_; SHA256=(Get-FileHash -LiteralPath (Join-Path $root $_)).Hash} })
        $buildInfo = [pscustomobject]@{Version=$Version; SourceFiles=$sourceFiles}
        $buildInfo | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stage 'build-info.json') -Encoding UTF8
        $manifestLines = @(Get-ChildItem -LiteralPath $stage -File | Sort-Object Name | ForEach-Object { ((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name) })
        [IO.File]::WriteAllLines((Join-Path $stage 'SHA256SUMS.txt'), $manifestLines, (New-Object Text.UTF8Encoding($false)))
        if (Test-Path -LiteralPath $releaseDir) {
            if (& git tag --list $tag) { throw '该版本已经打过标签，不能重新覆盖构建，请增加版本号。' }
            $resolvedOutput = [IO.Path]::GetFullPath($releaseDir)
            $expectedParent = [IO.Path]::GetFullPath((Join-Path $root 'dist')) + [IO.Path]::DirectorySeparatorChar
            if (!$resolvedOutput.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase)) { throw '构建输出目录越界。' }
            $previousRoot = Join-Path $root 'temp\previous-packages'
            New-Item -ItemType Directory -Force -Path $previousRoot | Out-Null
            $previous = [IO.Path]::GetFullPath((Join-Path $previousRoot ($tag + '-' + [guid]::NewGuid().ToString('N'))))
            if (!$previous.StartsWith([IO.Path]::GetFullPath($previousRoot) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '备份目录越界。' }
            Move-Item -LiteralPath $resolvedOutput -Destination $previous
            Write-Host "已保留上一次未发布构建：$previous"
        }
        New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
        Get-ChildItem -LiteralPath $stage -File | ForEach-Object { Copy-Verified $_.FullName (Join-Path $releaseDir $_.Name) }
        $portableDestination = Join-Path $releaseDir 'CapturaYe'
        Copy-Item -LiteralPath $portable -Destination $portableDestination -Recurse
    }

    if (!(Test-Path (Join-Path $releaseDir 'SHA256SUMS.txt'))) { throw '未找到已验证的发布包。' }
    $buildInfo = Get-Content (Join-Path $releaseDir 'build-info.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $currentPaths = @(& git ls-files --cached --others --exclude-standard | Where-Object { $_ -match '\.(cs|xaml|csproj|props|targets|json|ps1|cmd|iss|isl)$' } | Sort-Object -Unique)
    $recordedPaths = @($buildInfo.SourceFiles | ForEach-Object Path | Sort-Object -Unique)
    if (Compare-Object -ReferenceObject $recordedPaths -DifferenceObject $currentPaths) { throw '源码文件列表在构建后改变，请重新构建。' }
    foreach ($source in $buildInfo.SourceFiles) {
        $sourcePath = Join-Path $root $source.Path
        if (!(Test-Path -LiteralPath $sourcePath) -or (Get-FileHash -LiteralPath $sourcePath).Hash -ne $source.SHA256) { throw "源码已在构建后改变：$($source.Path)。请重新构建。" }
    }
    Get-Content (Join-Path $releaseDir 'SHA256SUMS.txt') | ForEach-Object {
        if ($_ -notmatch '^([0-9a-f]{64})  (.+)$') { throw "校验清单格式错误：$_" }
        if ((Get-FileHash -LiteralPath (Join-Path $releaseDir $Matches[2])).Hash -ne $Matches[1]) { throw "发布包哈希不符：$($Matches[2])" }
    }
    if ($Publish) {
        $remoteUrl = "https://github.com/$Repository.git"
        $remotes = @(& git remote)
        if ($remotes -notcontains 'publish') { Run 'git' @('remote','add','publish',$remoteUrl) }
        elseif ((& git remote get-url publish) -ne $remoteUrl) { throw 'publish 远程地址不符，拒绝向其他仓库发布。' }
        $status = & git status --porcelain
        if ($status) {
            Write-Host '提交当前项目源码和发布脚本：'
            Run 'git' @('add','--all')
            Run 'git' @('commit','-m',"发布 Captura Ye 中文版 $tag")
        }
        $commit = (& git rev-parse HEAD).Trim()
        $localTag = & git tag --list $tag
        if ($localTag) {
            if ((& git rev-list -n 1 $tag).Trim() -ne $commit) { throw '该版本标签已指向其他提交，请更新版本号。' }
        } else { Run 'git' @('tag','-a',$tag,'-m',"Captura Ye 中文版 $tag") }
        # 本次推送单独使用已登录的 gh，不弹出凭据管理器登录窗口。
        Run 'git' @('-c','credential.helper=','-c','credential.helper=!gh auth git-credential','-c','credential.interactive=false','push','publish','HEAD:refs/heads/master',"refs/tags/$tag")
        $releases = & gh release list --repo $Repository --limit 100 --json tagName,isDraft
        if ($LASTEXITCODE -ne 0) { throw '无法查询发行版。' }
        # Windows PowerShell 5.1 不枚举 ConvertFrom-Json 返回的数组，先赋值再筛选。
        $parsedReleases = ConvertFrom-Json -InputObject ($releases -join "`n")
        $existing = @($parsedReleases | Where-Object { $_.tagName -eq $tag })
        if ($existing.Count -gt 1) { throw '同一版本存在多个发行草稿，已停止，避免选择错误的草稿。' }
        $assets = @(Get-ChildItem -LiteralPath $releaseDir -File | ForEach-Object FullName)
        if (!$existing.Count) {
            Run 'gh' (@('release','create',$tag,'--repo',$Repository,'--draft','--verify-tag','--title',"Captura Ye 中文版 $tag",'--notes-file',(Join-Path $releaseDir 'release-notes.md')) + $assets)
        } else {
            $remoteJson = & gh release view $tag --repo $Repository --json assets
            if ($LASTEXITCODE -ne 0) { throw '无法核对已有发行版资产。' }
            $remoteAssets = (ConvertFrom-Json -InputObject ($remoteJson -join "`n")).assets
            foreach ($asset in $assets) {
                $assetName = Split-Path $asset -Leaf
                $matching = @($remoteAssets | Where-Object { $_.name -eq $assetName })
                if (!$matching.Count) { Run 'gh' @('release','upload',$tag,$asset,'--repo',$Repository) }
                elseif ($matching.Count -ne 1 -or $matching[0].size -ne (Get-Item -LiteralPath $asset).Length -or
                    ($matching[0].digest -and $matching[0].digest -ne ('sha256:' + (Get-FileHash -LiteralPath $asset).Hash.ToLowerInvariant()))) {
                    throw "已有发行资产内容冲突，未覆盖：$assetName"
                }
            }
        }
        $downloadDir = Join-Path $logRoot 'github-download'
        New-Item -ItemType Directory -Force -Path $downloadDir | Out-Null
        $downloadDir = Download-Release $tag $downloadDir
        foreach ($asset in $assets) {
            $remoteFile = Join-Path $downloadDir (Split-Path $asset -Leaf)
            if (!(Test-Path -LiteralPath $remoteFile) -or (Get-Item -LiteralPath $remoteFile).Length -ne (Get-Item -LiteralPath $asset).Length -or (Get-FileHash -LiteralPath $remoteFile).Hash -ne (Get-FileHash -LiteralPath $asset).Hash) { throw "GitHub 回下载校验失败：$asset" }
        }
        Run 'gh' @('release','edit',$tag,'--repo',$Repository,'--draft=false','--latest')
        Run 'gh' @('release','view',$tag,'--repo',$Repository,'--json','url,isDraft,tagName,assets')
    }
    if ($Archive) { & (Join-Path $PSScriptRoot 'archive-release.ps1') -Source $releaseDir -Version $Version }
    Write-Host "全部请求步骤完成。发布目录：$releaseDir"
    Write-Host "日志：$logRoot"
} catch {
    Write-Host "流程已停止：$_" -ForegroundColor Red
    Write-Host "请查看日志：$logRoot" -ForegroundColor Yellow
    exit 1
} finally { Stop-Transcript | Out-Null }
