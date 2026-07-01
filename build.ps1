#!/usr/bin/env pwsh
# Packs every SDK to ./artifacts. Usage: ./build.ps1 [-Version <version>]
param([string]$Version = '0.0.1-local')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

Remove-Item -Recurse -Force artifacts -ErrorAction Ignore
# Purge cached copies so local iteration always picks up the fresh pack.
$cacheRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME '.nuget/packages' }
Get-ChildItem -Path $cacheRoot -Directory -Filter 'rocket.surgery.sdk*' -ErrorAction Ignore | Remove-Item -Recurse -Force

foreach ($project in Get-ChildItem src/*.csproj) {
    dotnet pack $project.FullName -o artifacts --nologo -v quiet -p:Version=$Version
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Get-ChildItem artifacts/*.nupkg | Select-Object -ExpandProperty Name
