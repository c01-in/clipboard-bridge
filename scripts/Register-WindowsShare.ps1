#Requires -Version 5.1
[CmdletBinding()]
param([string]$InstallRoot="$env:ProgramFiles\ClipboardBridge",[switch]$Unregister)
$ErrorActionPreference='Stop'
Import-Module Appx -ErrorAction Stop
if($Unregister){
    Get-AppxPackage -Name ClipboardBridge.SharePreview | Remove-AppxPackage
    return
}
if([Environment]::OSVersion.Version.Build -lt 22000){throw 'Windows Share preview requires Windows 11.'}
$taskPackage=Join-Path $InstallRoot 'share\ClipboardBridge.Share.msix'
if(!(Test-Path -LiteralPath $taskPackage)){throw 'Windows Share identity package is missing.'}
# Sparse-package resource lookups use the external content root as well as its images.
Get-ChildItem -LiteralPath (Join-Path $InstallRoot 'share') -Filter '*.pri' -File | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $InstallRoot -Force}
$taskManifest=[xml](Get-Content -LiteralPath (Join-Path $InstallRoot 'share\AppxManifest.xml') -Raw)
$taskArguments=@{Path=$taskPackage;ExternalLocation=[IO.Path]::GetFullPath($InstallRoot);ForceTargetApplicationShutdown=$true;ForceUpdateFromAnyVersion=$true}
if($taskManifest.Package.Identity.Publisher -match 'OID\.2\.25\.311729368913984317654407730594956997722=1'){$taskArguments.AllowUnsigned=$true}
# Local unsigned preview or trusted signed distribution; no developer-mode or certificate-store changes.
Add-AppxPackage @taskArguments
$taskRegistered=Get-AppxPackage -Name ClipboardBridge.SharePreview
if(!$taskRegistered){throw 'Windows Share registration did not complete.'}
Write-Output ('Windows Share registered: '+$taskRegistered.PackageFullName)
