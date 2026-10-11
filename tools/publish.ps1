param(
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\BingWallpaper\BingWallpaper.csproj'
$output = Join-Path $root 'dist'

$args = @(
    'publish', $project,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', $(if ($SelfContained) { 'true' } else { 'false' }),
    '-p:PublishSingleFile=true',
    '-p:PublishTrimmed=false',
    '-p:DebugType=none',
    '-o', $output
)
if ($SelfContained) {
    $args += '-p:EnableCompressionInSingleFile=true'
    $args += '-p:IncludeNativeLibrariesForSelfExtract=true'   # 否则 WPF 的 native dll 会散落在 exe 旁边
}

Write-Host ("dotnet " + ($args -join ' ')) -ForegroundColor DarkGray
dotnet @args
if ($LASTEXITCODE -ne 0) { throw "发布失败，退出码 $LASTEXITCODE" }

Get-ChildItem $output -File | ForEach-Object {
    Write-Host ("  {0}  ({1:N0} KB)" -f $_.Name, ($_.Length / 1KB)) -ForegroundColor Green
}
