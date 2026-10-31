# Builds the self-contained release of TidyBox.App and packs it into an .msix with makeappx.
#
# Why not just `dotnet build` + pack the output folder: `dotnet build` never produces an .msix, and the output folder
# is missing files (for example Assets) that the build maps in from elsewhere. The authoritative file list is
# TidyBox.App.build.appxrecipe, so the mapping file is generated from it.
#
#   scripts\package-msix.ps1                  # x64, unsigned (what Partner Center wants: Microsoft signs it on ingestion)
#   scripts\package-msix.ps1 -TestSign        # also signs with a throwaway certificate, for installing on a test machine
#   scripts\package-msix.ps1 -Platform arm64
#
# The package identity comes from src\TidyBox.App\Package.appxmanifest; change it there once Partner Center has issued
# the real Name and Publisher.
param(
    [ValidateSet('x64', 'arm64')][string]$Platform = 'x64',
    [string]$OutDir = (Join-Path $PSScriptRoot '..\artifacts\msix'),
    [switch]$TestSign
)
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$rid = "win-$Platform"
$msix = Join-Path $OutDir "TidyBox_$Platform.msix"
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$msix = Join-Path $OutDir "TidyBox_$Platform.msix"

# newest installed Windows SDK that has makeappx / signtool
$sdkBin = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName 'x64\makeappx.exe') } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $sdkBin) { throw 'Windows 10/11 SDK not found (makeappx.exe missing).' }
$makeappx = Join-Path $sdkBin.FullName 'x64\makeappx.exe'
$signtool = Join-Path $sdkBin.FullName 'x64\signtool.exe'

Write-Host "--- build ($Platform, self-contained)"
dotnet build (Join-Path $root 'src\TidyBox.App') -c Release "-p:Platform=$Platform" -r $rid --self-contained true -p:WindowsAppSDKSelfContained=true
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }

$recipe = Get-ChildItem (Join-Path $root "src\TidyBox.App\bin\$Platform") -Recurse -Filter 'TidyBox.App.build.appxrecipe' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $recipe) { throw 'TidyBox.App.build.appxrecipe not found after the build.' }

Write-Host "--- mapping from $($recipe.FullName)"
$xml = [xml](Get-Content $recipe.FullName -Raw)
$ns = New-Object Xml.XmlNamespaceManager($xml.NameTable)
$ns.AddNamespace('m', 'http://schemas.microsoft.com/developer/msbuild/2003')
$items = $xml.SelectNodes('//m:AppXManifest | //m:AppxPackagedFile', $ns) |
    Where-Object { $_.Include -notlike '*.pdb' } |
    ForEach-Object { [pscustomobject]@{ Source = $_.Include; Target = $_.PackagePath } } |
    Sort-Object Target -Unique
$mapping = Join-Path $OutDir 'mapping.txt'
$lines = @('[Files]') + ($items | ForEach-Object { '"{0}" "{1}"' -f $_.Source, $_.Target })
Set-Content $mapping $lines -Encoding utf8
$missing = $items | Where-Object { -not (Test-Path -LiteralPath $_.Source) }
if ($missing) { throw "mapped files missing on disk: $($missing | Select-Object -First 5 -ExpandProperty Source)" }
"$($items.Count) files"

Write-Host '--- makeappx pack'
Remove-Item $msix -ErrorAction SilentlyContinue
& $makeappx pack /f $mapping /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed ($LASTEXITCODE)" }

if ($TestSign) {
    # The certificate subject has to equal the manifest's Publisher or signing is rejected.
    $publisher = $xml.SelectSingleNode('//m:PackageIdentityPublisher', $ns).InnerText
    Write-Host "--- test-signing as $publisher (throwaway certificate, removed again afterwards)"
    $cert = New-SelfSignedCertificate -Type Custom -Subject $publisher -KeyUsage DigitalSignature -FriendlyName 'TidyBox test' `
        -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(1) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    $pfx = Join-Path ([IO.Path]::GetTempPath()) "tidybox-test-$([guid]::NewGuid().ToString('N')).pfx"
    $pw = [guid]::NewGuid().ToString('N')
    try {
        Export-PfxCertificate -Cert $cert -FilePath $pfx -Password (ConvertTo-SecureString $pw -AsPlainText -Force) | Out-Null
        & $signtool sign /fd SHA256 /f $pfx /p $pw $msix
        if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE)" }
        Export-Certificate -Cert $cert -FilePath (Join-Path $OutDir 'TidyBox-test.cer') | Out-Null
    }
    finally {
        Remove-Item $pfx -ErrorAction SilentlyContinue
        Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)" -ErrorAction SilentlyContinue
    }
}

"{0}  ({1:N1} MB){2}" -f $msix, ((Get-Item $msix).Length / 1MB), $(if ($TestSign) { '  signed with a test certificate' } else { '  unsigned' })
