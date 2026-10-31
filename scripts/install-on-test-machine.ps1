# Installs the local TidyBox test package on a clean Windows 11 machine. Run from an administrator PowerShell, in the folder that holds
# TidyBox_x64.msix and TidyBox-test.cer. The certificate is only a throwaway one made for this test; it is removed again by
# uninstall-and-check-leftovers.ps1.
param([string]$Name = 'TidyBox.Dev', [string]$Package = 'TidyBox_x64.msix')   # Name: identity Name from Package.appxmanifest
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { Write-Host 'Run this in an administrator PowerShell (right-click Start > Terminal (Admin)).' -ForegroundColor Red; exit 1 }

Write-Host '--- this machine (the test is only meaningful on a clean one)'
$os = Get-CimInstance Win32_OperatingSystem
"OS: $($os.Caption) build $($os.BuildNumber), architecture $env:PROCESSOR_ARCHITECTURE"
if (Get-Command dotnet -ErrorAction SilentlyContinue) { Write-Host '.NET found on this machine (the test is weaker, the app should not need it):' -ForegroundColor Yellow; dotnet --list-runtimes } else { Write-Host 'No .NET found: good, the package has to bring its own runtime.' -ForegroundColor Green }
$wasdk = Get-AppxPackage -Name 'Microsoft.WindowsAppRuntime*' -ErrorAction SilentlyContinue
if ($wasdk) { Write-Host 'Windows App SDK runtime packages are installed here (the test is weaker):' -ForegroundColor Yellow; $wasdk | ForEach-Object { "  $($_.Name) $($_.Version)" } } else { Write-Host 'No Windows App SDK runtime installed: good.' -ForegroundColor Green }

Write-Host '--- trusting the test certificate and installing'
$cert = Import-Certificate -FilePath "$here\TidyBox-test.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
"trusted: $($cert.Subject)  $($cert.Thumbprint)"
Add-AppxPackage -Path "$here\$Package"
Get-AppxPackage -Name $Name | Format-List Name, Version, Architecture, PackageFullName, InstallLocation
Write-Host 'Done. Start TidyBox from the Start menu.' -ForegroundColor Green
