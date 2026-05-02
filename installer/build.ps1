<#
.SYNOPSIS
    S3 Browser を self-contained 単一 exe としてpublishし、Inno Setup でインストーラーを生成する。

.PARAMETER Configuration
    publish 構成 (Debug | Release)。既定は Release。

.PARAMETER Runtime
    .NET RID。win-x64 のみ動作確認済。

.PARAMETER SkipInstaller
    publish のみで Inno Setup の起動をスキップ。

.EXAMPLE
    pwsh installer\build.ps1
    pwsh installer\build.ps1 -SkipInstaller
#>

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

$repoRoot   = Split-Path -Parent $PSScriptRoot
$projFile   = Join-Path $repoRoot "S3Browser\S3Browser.csproj"
$publishDir = Join-Path $repoRoot "publish\$Runtime"
$distDir    = Join-Path $repoRoot "dist"

if (-not (Test-Path $projFile)) {
    throw "プロジェクトファイルが見つかりません: $projFile"
}

Write-Host "==> dotnet publish ($Configuration / $Runtime)" -ForegroundColor Cyan

# 既存 publish を掃除して残骸を排除
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

# .NET 8 SDK が dotnet コマンドで先行解決されると net10.0-windows をビルドできない。
# `cmd.exe /c` 経由で起動すると正しい SDK (10.x) が選択される環境があるため、まずそれを試す。
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Path
if (-not $dotnet) {
    throw "dotnet CLI が PATH に見つかりません。"
}

$publishArgs = @(
    "publish", $projFile,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:PublishReadyToRun=true",
    "-p:DebugType=none",
    "-p:DebugSymbols=false",
    "-o", $publishDir
)

# cmd.exe 経由なら .NET ホストの SDK 解決が安定する (PowerShell 経由だと .NET 8 を選ぶケースあり)
$cmdLine = ('"{0}" {1}' -f $dotnet, ($publishArgs -join ' '))
& cmd.exe /c $cmdLine
if ($LASTEXITCODE -ne 0) {
    throw "publish が失敗しました (exit $LASTEXITCODE)"
}

$exePath = Join-Path $publishDir "S3Browser.exe"
if (-not (Test-Path $exePath)) {
    throw "publish 出力に S3Browser.exe が見つかりません: $exePath"
}

$size = [Math]::Round((Get-Item $exePath).Length / 1MB, 1)
Write-Host "==> publish 完了: $exePath ($size MB)" -ForegroundColor Green

if ($SkipInstaller) {
    Write-Host "-SkipInstaller 指定のためインストーラー生成をスキップ。"
    exit 0
}

# Inno Setup の場所を探索
$iscc = $null
foreach ($candidate in @(
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
)) {
    if (Test-Path $candidate) { $iscc = $candidate; break }
}
if (-not $iscc) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Path }
}
if (-not $iscc) {
    Write-Host "Inno Setup (ISCC.exe) が見つかりませんでした。" -ForegroundColor Yellow
    Write-Host "https://jrsoftware.org/isdl.php からインストールするか、winget install JRSoftware.InnoSetup を実行してください。" -ForegroundColor Yellow
    Write-Host "publish 出力は $publishDir に作成済みです。"
    exit 0
}

Write-Host "==> Inno Setup ($iscc)" -ForegroundColor Cyan
$iss = Join-Path $PSScriptRoot "setup.iss"
& $iscc $iss
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup が失敗しました (exit $LASTEXITCODE)"
}

$installer = Get-ChildItem $distDir -Filter "S3Browser-*-Setup.exe" -ErrorAction SilentlyContinue |
             Sort-Object LastWriteTime -Descending |
             Select-Object -First 1
if ($installer) {
    $installerSize = [Math]::Round($installer.Length / 1MB, 1)
    Write-Host "==> インストーラー生成完了: $($installer.FullName) ($installerSize MB)" -ForegroundColor Green
}
