<#
.SYNOPSIS
    编译 BingWallpaper 单文件 exe。
.EXAMPLE
    pwsh tools/publish.ps1                          # 框架依赖（需要 .NET 8 桌面运行时）
    pwsh tools/publish.ps1 -SelfContained           # 自包含（约 63 MB，任何机器直接跑）
    pwsh tools/publish.ps1 -SelfContained -Version 1.0.1
#>
[CmdletBinding()]
param(
    # 自包含发布；否则是框架依赖（体积小，但目标机器要装 .NET 8 桌面运行时）
    [switch]$SelfContained,

    # 覆盖程序集版本（tag 是唯一真源时由 release.ps1 / CI 传入，如 1.0.1 或 1.0.1-beta.1）
    [string]$Version,

    # 输出目录，默认 <仓库根>\dist
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\BingWallpaper\BingWallpaper.csproj'
$output = if ($OutputDir) { $OutputDir } else { Join-Path $root 'dist' }

if (Test-Path $output) {
    Remove-Item (Join-Path $output '*') -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $output -Force | Out-Null

$publishArgs = @(
    'publish', $project,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', $(if ($SelfContained) { 'true' } else { 'false' }),
    '-p:PublishSingleFile=true',
    '-p:PublishTrimmed=false',
    '-p:DebugType=none',
    '-o', $output
)
if ($Version) {
    $publishArgs += "-p:Version=$Version"
    # 否则 ProductVersion 会变成 1.0.1+<git sha>
    $publishArgs += '-p:IncludeSourceRevisionInInformationalVersion=false'
}
if ($SelfContained) {
    $publishArgs += '-p:EnableCompressionInSingleFile=true'
    $publishArgs += '-p:IncludeNativeLibrariesForSelfExtract=true'   # 否则 native dll 会散落在 exe 旁边
}

Write-Host ("dotnet " + ($publishArgs -join ' ')) -ForegroundColor DarkGray
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "发布失败，退出码 $LASTEXITCODE" }

Get-ChildItem $output -File | ForEach-Object {
    Write-Host ("  {0}  ({1:N0} KB)" -f $_.Name, ($_.Length / 1KB)) -ForegroundColor Green
}
