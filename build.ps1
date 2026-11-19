<#
.SYNOPSIS
  Compila, prueba, publica (self-contained win-x64) y genera el instalador de Kip.
.EXAMPLE
  .\build.ps1                 # versión 1.0.0
  .\build.ps1 -Version 1.2.0
  .\build.ps1 -SkipTests
#>
param(
    [string]$Version = "1.0.0",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { throw "No se encontró el SDK de .NET 10. Instálelo con: winget install Microsoft.DotNet.SDK.10" }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "No se encontró Inno Setup 6. Instálelo con: winget install JRSoftware.InnoSetup" }

function Invoke-Step([string]$name, [scriptblock]$action) {
    Write-Host "==> $name" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE -ne 0) { throw "Falló: $name (código $LASTEXITCODE)" }
}

$publishDir = Join-Path $root "artifacts\publish"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

Invoke-Step "Compilar" { & $dotnet build "$root\StickyNotes.slnx" -c Release -p:Version=$Version }

if (-not $SkipTests) {
    Invoke-Step "Pruebas" { & $dotnet test "$root\StickyNotes.slnx" -c Release --no-build }
}

Invoke-Step "Publicar (self-contained win-x64)" {
    & $dotnet publish "$root\src\StickyNotes.App\StickyNotes.App.csproj" -c Release -r win-x64 `
        --self-contained true -p:PublishReadyToRun=true -p:Version=$Version -o $publishDir
}

Invoke-Step "Generar instalador" { & $iscc "/DAppVersion=$Version" "$root\installer\Kip.iss" }

Write-Host "`nListo: $root\artifacts\installer\Kip-Setup-$Version.exe" -ForegroundColor Green
