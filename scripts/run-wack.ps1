# Runs the Windows App Certification Kit on a package built by package-msix.ps1. Needs an elevated (administrator) PowerShell.
#   scripts\run-wack.ps1                     # artifacts\msix\TidyBox_x64.msix -> artifacts\msix\wack-report.xml
param(
    [string]$Package = (Join-Path $PSScriptRoot '..\artifacts\msix\TidyBox_x64.msix'),
    [string]$Report = (Join-Path (Split-Path -Parent $Package) 'wack-report.xml')
)
$A = 'C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { Write-Host 'This needs an administrator PowerShell. Right-click the Start button > Terminal (Admin).' -ForegroundColor Red; exit 1 }

$Package = (Resolve-Path $Package).Path
Remove-Item $Report -ErrorAction SilentlyContinue
& $A reset
Write-Host 'Running WACK; this usually takes several minutes...'
& $A test -appxpackagepath $Package -reportoutputpath $Report
Write-Host "appcert exit code: $LASTEXITCODE"
Write-Host "report: $Report (exists: $(Test-Path $Report))"
