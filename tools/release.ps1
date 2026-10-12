<#
.SYNOPSIS
    一键出发布产物：自包含 exe + 便携 zip + Inno Setup 安装包 + SHA256SUMS。
.DESCRIPTION
    版本号由调用方传入（CI 里取自 git tag）。本机需要：
      - .NET 8 SDK
      - Inno Setup 6.3+（只出便携版可加 -SkipInstaller 跳过；非 Windows 上会自动用 wine 调 ISCC）
.EXAMPLE
    pwsh tools/release.ps1 -Version 1.0.1
    pwsh tools/release.ps1 -Version 1.0.1-beta.1 -SkipInstaller
#>
[CmdletBinding()]
param(
    # 1.0.1 / v1.0.1 / 1.0.1-beta.1 都行
    [Parameter(Mandatory = $true)]
    [string]$Version,

    # 产物目录，默认 <仓库根>\dist
    [string]$OutputDir,

    # 手动指定 ISCC.exe（非 Windows 本地验证时配合 wine 使用）
    [string]$IsccPath,

    [switch]$SkipInstaller,
    [switch]$SkipPortable,
    # 找不到 ISCC 时必须失败（CI 用，避免"以为出了安装包其实没有"）
    [switch]$RequireInstaller
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$onWindows = $true
if ($PSVersionTable.PSVersion.Major -ge 6) { $onWindows = $IsWindows }

$dist = if ($OutputDir) { [IO.Path]::GetFullPath($OutputDir) } else { Join-Path $root 'dist' }

# ---- 版本号 ----
$v = $Version.Trim().TrimStart('v')
if ($v -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$') {
    throw "版本号格式非法：'$Version'（应为 1.2.3 或 1.2.3-beta.1）"
}
$parts = ($v -split '-')[0].Split('.')
while ($parts.Count -lt 4) { $parts += '0' }
$numeric = $parts -join '.'

Write-Host "==> 版本 $v（PE 版本资源 $numeric）" -ForegroundColor Cyan

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist | Out-Null

# ---- 1. 自包含单文件 exe ----
Write-Host '==> 1/4 编译自包含单文件 exe' -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'publish.ps1') -SelfContained -Version $v -OutputDir $dist

# ---- 2. 便携版 zip ----
if (-not $SkipPortable) {
    Write-Host '==> 2/4 便携版 zip' -ForegroundColor Cyan
    $zip = Join-Path $dist "BingWallpaper-$v-win-x64-portable.zip"
    Compress-Archive -Path (Join-Path $dist 'BingWallpaper.exe') -DestinationPath $zip -CompressionLevel Optimal
}

# ---- 3. 安装包 ----
if (-not $SkipInstaller) {
    Write-Host '==> 3/4 安装包（Inno Setup）' -ForegroundColor Cyan

    $iscc = $IsccPath
    if (-not $iscc) {
        $candidates = @()
        foreach ($base in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
            if ($base) {
                $candidates += Join-Path $base 'Inno Setup 6\ISCC.exe'
                $candidates += Join-Path $base 'Inno Setup 7\ISCC.exe'
            }
        }
        $iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $iscc) {
            $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
            if ($cmd) { $iscc = $cmd.Source }
        }
    }

    if (-not $iscc) {
        $hint = '未找到 ISCC.exe，请安装 Inno Setup 6.3+（https://jrsoftware.org/isdl.php），或用 -SkipInstaller 跳过。'
        if ($RequireInstaller -or $onWindows) { throw $hint }
        Write-Warning "$hint（当前不是 Windows，已跳过安装包）"
    }
    else {
        $iss = Join-Path $root 'installer\bing-wallpaper.iss'
        $script = $iss
        $srcDir = $dist
        if (-not $onWindows) {
            # 本地用 wine 验证时，把 Unix 路径翻译成 wine 能看懂的 Z:\...
            Write-Host "（非 Windows：用 wine 调用 $iscc）" -ForegroundColor DarkGray
            $script = (winepath -w $iss).Trim()
            $srcDir = (winepath -w $dist).Trim()
        }
        $isccArgs = @(
            "/DAppVersion=$v",
            "/DAppVersionNumeric=$numeric",
            "/DSourceDir=$srcDir",
            "/DOutputDir=$srcDir",
            $script
        )
        if ($onWindows) { & $iscc @isccArgs } else { & wine $iscc @isccArgs }
        if ($LASTEXITCODE -ne 0) { throw "ISCC 编译失败，退出码 $LASTEXITCODE" }
    }
}

# ---- 4. 校验和 ----
Write-Host '==> 4/4 SHA256SUMS.txt' -ForegroundColor Cyan
$sums = Join-Path $dist 'SHA256SUMS.txt'
$lines = Get-ChildItem $dist -File |
    Where-Object { $_.Name -ne 'SHA256SUMS.txt' } |
    Sort-Object Name |
    ForEach-Object { "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name }
Set-Content -Path $sums -Value $lines -Encoding ascii

Write-Host '==> 完成，产物：' -ForegroundColor Green
Get-ChildItem $dist -File | ForEach-Object {
    Write-Host ("  {0}  ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB)) -ForegroundColor Green
}
