# Uninstalls the TidyBox test package, removes the throwaway certificate, and lists what is left behind (Store policy 10.2.7 asks that
# a user can tell how to uninstall cleanly). Run from an administrator PowerShell, in the same folder as TidyBox-test.cer.
param([string]$Name = 'TidyBox.Dev')   # the package identity Name from Package.appxmanifest
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { Write-Host 'Run this in an administrator PowerShell.' -ForegroundColor Red; exit 1 }

$pkg = Get-AppxPackage -Name $Name
$family = $pkg.PackageFamilyName
if ($pkg) { Remove-AppxPackage -Package $pkg.PackageFullName; "package removed" } else { "package was not installed" }

$cer = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2("$here\TidyBox-test.cer")
Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Thumbprint -eq $cer.Thumbprint | Remove-Item
"test certificate removed from Trusted People: $(-not (Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Thumbprint -eq $cer.Thumbprint))"

Write-Host '--- what is still on disk (documented in docs/store-submission.md section 4.3; anything else is a surprise):'
$candidates = @(
    "$env:LOCALAPPDATA\TidyBox",
    "$env:LOCALAPPDATA\Packages\$family",
    "$env:ProgramData\TidyBox",
    "$([Environment]::GetFolderPath('MyDocuments'))\TidyBox"
)
foreach ($c in $candidates) {
    if (Test-Path $c) { $n = (Get-ChildItem $c -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count; "LEFT  $c   ($n files)" } else { "gone  $c" }
}
Write-Host '--- registry entries the context-menu module may have left in the current user hive:'
Get-ChildItem 'HKCU:\Software\Classes\Directory\shell','HKCU:\Software\Classes\Directory\Background\shell' -ErrorAction SilentlyContinue | Where-Object Name -match 'TidyBox|Analyze' | ForEach-Object { "LEFT  $($_.Name)" }
