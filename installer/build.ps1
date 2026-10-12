<#
.SYNOPSIS
  Publica o executavel unico do InsightFlow e gera o instalador (Inno Setup).
.DESCRIPTION
  Saida: artifacts\installer\InsightFlow-Setup-<versao>.exe. A versao vem do .csproj, salvo -Version.
  Sem certificado, executavel e instalador saem sem assinatura. Para assinar, aponte INSIGHTFLOW_SIGN_PFX
  para um .pfx de assinatura de codigo e ponha a senha em INSIGHTFLOW_SIGN_PASSWORD.
  Mensagens sem acentos: o Windows PowerShell 5.1 le scripts sem BOM como ANSI.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File installer\build.ps1
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$TimestampServer = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src\InsightFlow.Desktop'
$output = Join-Path $root 'artifacts\installer'
$app = Join-Path $output 'app'
$signScript = Join-Path $PSScriptRoot 'sign.ps1'

function Find-InnoSetup {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $folders = @(${env:ProgramFiles(x86)}, $env:ProgramFiles, (Join-Path $env:LOCALAPPDATA 'Programs')) | Where-Object { $_ }
    foreach ($release in 'Inno Setup 7', 'Inno Setup 6') {
        foreach ($folder in $folders) {
            $candidate = Join-Path (Join-Path $folder $release) 'ISCC.exe'
            if (Test-Path $candidate) { return $candidate }
        }
    }
    throw 'Inno Setup nao encontrado. Instale com: winget install JRSoftware.InnoSetup'
}

function Assert-Success([string]$step) {
    if ($LASTEXITCODE -ne 0) { throw "$step falhou (codigo $LASTEXITCODE)." }
}

$compiler = Find-InnoSetup
if (-not $Version) {
    $Version = (& dotnet msbuild $project -getProperty:Version | Select-Object -Last 1).Trim()
    Assert-Success 'A leitura da versao'
}
$sign = [bool]$env:INSIGHTFLOW_SIGN_PFX
Write-Host "InsightFlow $Version | assinatura: $(if ($sign) { 'sim' } else { 'nao (INSIGHTFLOW_SIGN_PFX ausente)' })"

if (Test-Path $app) { Remove-Item $app -Recurse -Force }
& dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none `
    "-p:Version=$Version" -o $app --nologo
Assert-Success 'A publicacao'

$arguments = @("/DAppVersion=$Version", "/DSourceDir=$app", "/O$output", '/Qp')
if ($sign) {
    & $signScript -Path (Join-Path $app 'InsightFlow.Desktop.exe') -TimestampServer $TimestampServer
    # O compilador assina o desinstalador e o proprio instalador; $q e $f sao trocados por ele (aspas e arquivo).
    $arguments += '/DSign'
    $arguments += "/Sinsightflow=powershell.exe -NoProfile -ExecutionPolicy Bypass -File `$q$signScript`$q -TimestampServer `$q$TimestampServer`$q -Path `$f"
}
& $compiler @arguments (Join-Path $PSScriptRoot 'InsightFlow.iss')
Assert-Success 'A compilacao do instalador'

$installer = Join-Path $output "InsightFlow-Setup-$Version.exe"
if (-not (Test-Path $installer)) { throw "O instalador nao foi gerado em $installer." }
Write-Host "Instalador: $installer ($([math]::Round((Get-Item $installer).Length / 1MB, 1)) MB)"
