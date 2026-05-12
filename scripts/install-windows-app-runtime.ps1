#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Installs the Windows App SDK runtime (x64) matching ImageHoard.App NuGet 2.0.1.

.DESCRIPTION
  Unpackaged WinUI 3 apps need this runtime on the machine. Run this script from an
  elevated PowerShell (Run as administrator).

  The standalone installer uses --msix (not legacy /install). See:
  https://aka.ms/windowsappsdkinstall

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\scripts\install-windows-app-runtime.ps1
#>
$ErrorActionPreference = 'Stop'
$version = '2.0.1'
$url = "https://aka.ms/windowsappsdk/2.0/$version/windowsappruntimeinstall-x64.exe"
$out = Join-Path $env:TEMP "WindowsAppRuntimeInstall-x64-$version.exe"

Write-Host "Downloading Windows App Runtime $version (x64)..."
Invoke-WebRequest -Uri $url -OutFile $out -UseBasicParsing

Write-Host "Installing MSIX packages (quiet)..."
$p = Start-Process -FilePath $out -ArgumentList '--msix', '--quiet' -Wait -PassThru
if ($p.ExitCode -ne 0) {
    Write-Error "Installer exited with code $($p.ExitCode). Try: $out --msix (without --quiet) for UI output."
    exit $p.ExitCode
}

Write-Host "Done. You can run: dotnet run --project src\ImageHoard.App\ImageHoard.App.csproj -c Debug"
