# Gera o instalador do Acertô: dist\Acerto-Setup-<versão>.exe
# Uso (na raiz do projeto):  powershell -ExecutionPolicy Bypass -File installer\gerar.ps1
#
# O instalador é o próprio Acerto.exe publicado como arquivo único, com os dados embutidos (ver
# src\Acerto.Windows\Setup.cs). Aberto fora da pasta de instalação, ele mostra a janela de instalação.
# A pasta publish\ fica com o mesmo exe e a pasta data\ ao lado: ali ele roda como programa normal.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root 'publish'

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root 'src\Acerto.Windows') -c Release -r win-x64 --self-contained -nologo -v q `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou" }

$exe = Join-Path $publish 'Acerto.exe'
$version = (Get-Item $exe).VersionInfo.ProductVersion
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$setup = Join-Path $dist "Acerto-Setup-$version.exe"
Copy-Item $exe $setup -Force
'{0}  ({1:N1} MB)' -f $setup, ((Get-Item $setup).Length / 1MB)
