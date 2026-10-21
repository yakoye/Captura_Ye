param([string]$Root)
$ErrorActionPreference = 'Stop'
if (!$Root) { $Root = Split-Path $PSScriptRoot -Parent }
$languageRoot = Join-Path $Root 'src\Captura.Loc\Languages'
$english = Get-Content (Join-Path $languageRoot 'en.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$chinese = Get-Content (Join-Path $languageRoot 'zh-CN.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$keys = @($english.PSObject.Properties.Name)
foreach ($key in $keys) {
    if ([string]::IsNullOrWhiteSpace($chinese.$key)) { throw "缺少中文资源：$key" }
}
$references = 0
Get-ChildItem (Join-Path $Root 'src') -Recurse -File | Where-Object {
    $_.Extension -in '.cs', '.xaml' -and $_.FullName -notmatch '\\(bin|obj)\\'
} | ForEach-Object {
    $content = Get-Content $_.FullName -Raw -Encoding UTF8
    if ($_.Extension -eq '.cs' -and $content -match 'public\s+(?:override\s+)?string\s+Description\s*(?:\{\s*get;\s*\}\s*=|=>)\s*@?"[A-Za-z]') {
        throw "悬停说明仍然直接使用英文，可能遗漏了多行文字：$($_.FullName)"
    }
    foreach ($match in [regex]::Matches($content, 'LanguageManager\.Instance\["([^"]+)"\]|\{Binding \[([^\]]+)\], Source=\{StaticResource Loc\}')) {
        $key = $match.Groups[1].Value + $match.Groups[2].Value
        if ($keys -notcontains $key) { throw "未知语言键：$key，文件：$($_.FullName)" }
        $references++
    }
}
$uiRoot = Join-Path $Root 'src\Captura'
$allowedLabels = @('Captura', 'Captura Ye 中文版', 'FFmpeg', 'x264', 'Twitch', 'YouTube Live', 'Imgur')
Get-ChildItem $uiRoot -Recurse -Filter '*.xaml' | Where-Object FullName -NotMatch '\\(bin|obj)\\' | ForEach-Object {
    foreach ($match in [regex]::Matches((Get-Content $_.FullName -Raw -Encoding UTF8), '(?<![\w:])(Text|Content|Header|ToolTip|Title|Watermark)="([A-Za-z][^"]*)"')) {
        if ($allowedLabels -notcontains $match.Groups[2].Value) { throw "界面残留英文：$($match.Value)，文件：$($_.FullName)" }
    }
}
$settings = Get-Content (Join-Path $Root 'src\Captura.Core\Settings\Models\VisualSettings.cs') -Raw -Encoding UTF8
if ($settings -notmatch 'Get\("zh-CN"\)') { throw '默认语言不是简体中文' }
$installer = Get-Content (Join-Path $Root 'installer\CapturaYe.iss') -Raw -Encoding UTF8
if ($installer -notmatch 'ShowLanguageDialog=yes' -or $installer -notmatch 'Name: "chinesesimp"') { throw '安装程序缺少中文选择' }
Write-Host "中文资源检查通过：$($keys.Count) 项资源、$references 处引用；无空翻译；默认中文和安装语言检查通过。"
